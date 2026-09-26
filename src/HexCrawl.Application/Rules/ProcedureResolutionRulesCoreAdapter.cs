using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Rules;

public sealed class ProcedureResolutionRulesCoreAdapter(IRulesCoreTravelGateway gateway)
{
    public async Task<ProcedureResolutionHelperCommand> PrepareAsync(
        StoredExpedition expedition,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(command);

        var prepared = command;
        if (prepared.ExpectedDistance is null && !string.IsNullOrWhiteSpace(prepared.TravelDistanceRule))
        {
            prepared = await ResolveExpectedDistanceAsync(expedition, prepared, cancellationToken);
        }

        if (prepared.NavigationDifficultyClass is null && prepared.NavigationRiskFactors.Count > 0)
        {
            prepared = await ResolveNavigationDifficultyAsync(expedition, prepared, cancellationToken);
        }

        return prepared;
    }

    private async Task<ProcedureResolutionHelperCommand> ResolveExpectedDistanceAsync(
        StoredExpedition expedition,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken)
    {
        if (expedition.Runtime is not ExpeditionState spatial)
        {
            throw new InvalidOperationException("Source-backed travel distance is available only for spatial crawl sessions.");
        }
        if (command.BaseSpeedFeet is null or <= 0)
        {
            throw new InvalidOperationException("Source-backed walking or hustling distance requires a positive base speed in feet.");
        }

        var travelRule = command.TravelDistanceRule!.Trim().ToLowerInvariant();
        var mechanicKey = travelRule switch
        {
            "walk" => TravelEnvironmentMechanicKeys.WalkDistance,
            "hustle" => TravelEnvironmentMechanicKeys.HustleDistance,
            _ => throw new InvalidOperationException("Source-backed travel distance must use either the walk or hustle mechanic.")
        };

        var integerInputs = new Dictionary<string, int>
        {
            ["base-speed-feet"] = command.BaseSpeedFeet.Value
        };
        Dictionary<string, string>? stringInputs = travelRule == "walk"
            ? new Dictionary<string, string> { ["period"] = "hour" }
            : null;
        var distanceEvaluation = await ResolveRequiredAsync(
            expedition,
            mechanicKey,
            new TravelEnvironmentResolutionRequest(
                IntegerInputs: integerInputs,
                StringInputs: stringInputs),
            cancellationToken);
        var quantity = distanceEvaluation.Quantity
            ?? throw new InvalidOperationException($"Rules Core resolved '{mechanicKey}' without a quantity.");
        if (!string.Equals(quantity.PerUnit, "hour", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core '{mechanicKey}' did not return an hourly distance quantity, so Hex Crawl will not reinterpret it.");
        }

        var hourly = ToDistanceMeasure(quantity);
        DistanceMeasure converted;
        try
        {
            converted = hourly.ConvertTo(spatial.DistanceTraveled.Unit);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "The source-backed travel distance can not be converted into this crawl session's distance unit.",
                exception);
        }

        var watchHours = spatial.ActiveWatch?.Remaining.TotalHours ?? expedition.Procedure.WatchLength.TotalHours;
        var expected = converted.Value * watchHours;
        var provenanceParts = new List<string>
        {
            DescribeEvaluation(expedition, distanceEvaluation, DescribeQuantity(quantity)),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Applied for {watchHours:0.###}h in {spatial.DistanceTraveled.Unit.Symbol}.")
        };

        var hasTerrain = !string.IsNullOrWhiteSpace(command.Terrain);
        var hasRoute = !string.IsNullOrWhiteSpace(command.Route);
        if (hasTerrain != hasRoute)
        {
            throw new InvalidOperationException(
                "Source-backed terrain travel requires both terrain and route, or neither.");
        }

        if (hasTerrain)
        {
            var catalog = await gateway.GetCatalogAsync(expedition.CampaignId, cancellationToken);
            var terrainMechanic = catalog.Mechanics.SingleOrDefault(value =>
                string.Equals(
                    value.MechanicKey,
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    StringComparison.Ordinal));
            if (terrainMechanic is null)
            {
                throw new InvalidOperationException("Rules Core does not expose the terrain-distance mechanic in the effective rules.");
            }
            EnsureCatalogMechanicResolvable(terrainMechanic);
            var semantic = terrainMechanic.Definition?.FactorSemantic;
            if (!string.Equals(semantic, "distance-multiplier", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Rules Core terrain factor semantic is '{semantic ?? "unspecified"}', not 'distance-multiplier'; Hex Crawl will not reinterpret it.");
            }

            var factorEvaluation = await ResolveRequiredAsync(
                expedition,
                TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                new TravelEnvironmentResolutionRequest(
                    StringInputs: new Dictionary<string, string>
                    {
                        ["terrain"] = command.Terrain!.Trim(),
                        ["route"] = command.Route!.Trim()
                    }),
                cancellationToken);
            var factor = factorEvaluation.Factor
                ?? throw new InvalidOperationException("Rules Core resolved the terrain-distance mechanic without a factor.");
            if (factor < 0)
            {
                throw new InvalidOperationException("Rules Core returned a negative terrain distance factor.");
            }
            expected *= (double)factor;
            provenanceParts.Add(DescribeEvaluation(
                expedition,
                factorEvaluation,
                $"factor={factor.ToString(CultureInfo.InvariantCulture)}; semantic={semantic}"));
        }

        if (!double.IsFinite(expected) || expected < 0)
        {
            throw new InvalidOperationException("Source-backed travel mechanics produced an invalid expected distance.");
        }

        return command with
        {
            ExpectedDistance = expected,
            ExpectedDistanceRulesNote = string.Join(" ", provenanceParts)
        };
    }

    private async Task<ProcedureResolutionHelperCommand> ResolveNavigationDifficultyAsync(
        StoredExpedition expedition,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken)
    {
        var riskFactors = command.NavigationRiskFactors
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (riskFactors.Length == 0)
        {
            return command;
        }

        var evaluation = await ResolveRequiredAsync(
            expedition,
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            new TravelEnvironmentResolutionRequest(
                StringListInputs: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["risk-factors"] = riskFactors
                }),
            cancellationToken);
        var check = evaluation.Check
            ?? throw new InvalidOperationException("Rules Core resolved the navigation mechanic without a check DC.");

        return command with
        {
            NavigationDifficultyClass = check.Dc,
            NavigationDifficultyRulesNote = DescribeEvaluation(
                expedition,
                evaluation,
                $"DC={check.Dc}; cadence={check.Cadence ?? "unspecified"}; competency={check.CompetencyConceptKey ?? "unspecified"}")
        };
    }

    private async Task<TravelEnvironmentEvaluationView> ResolveRequiredAsync(
        StoredExpedition expedition,
        string mechanicKey,
        TravelEnvironmentResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var evaluation = await gateway.ResolveAsync(
            expedition.CampaignId,
            mechanicKey,
            request,
            cancellationToken)
            ?? throw new InvalidOperationException($"Rules Core does not expose mechanic '{mechanicKey}' in the effective rules.");

        if (string.Equals(
                evaluation.MechanicState,
                TravelEnvironmentMechanicStates.Conflicted,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core reports a conflict for '{mechanicKey}'. Adjudicate the effective rule or enter an explicit DM override instead.");
        }
        if (string.Equals(
                evaluation.MechanicState,
                TravelEnvironmentMechanicStates.RequiresAdjudication,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core requires adjudication for '{mechanicKey}'. Resolve it in Rules Core or enter an explicit DM override instead.");
        }
        if (!string.Equals(
                evaluation.MechanicState,
                TravelEnvironmentMechanicStates.Resolved,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core mechanic '{mechanicKey}' is in unsupported state '{evaluation.MechanicState}'.");
        }
        if (string.Equals(
                evaluation.EvaluationState,
                TravelEnvironmentEvaluationStates.InputRequired,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core mechanic '{mechanicKey}' requires: {string.Join(", ", evaluation.MissingInputKeys)}.");
        }
        if (string.Equals(
                evaluation.EvaluationState,
                TravelEnvironmentEvaluationStates.NotApplicable,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core mechanic '{mechanicKey}' is not applicable to the supplied context.");
        }
        if (!string.Equals(
                evaluation.EvaluationState,
                TravelEnvironmentEvaluationStates.Resolved,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core mechanic '{mechanicKey}' returned unsupported evaluation state '{evaluation.EvaluationState}'.");
        }

        return evaluation;
    }

    private static void EnsureCatalogMechanicResolvable(TravelEnvironmentMechanicView mechanic)
    {
        if (string.Equals(mechanic.State, TravelEnvironmentMechanicStates.Conflicted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core reports a conflict for '{mechanic.MechanicKey}'. Adjudicate the effective rule or use an explicit DM override.");
        }
        if (string.Equals(mechanic.State, TravelEnvironmentMechanicStates.RequiresAdjudication, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rules Core requires adjudication for '{mechanic.MechanicKey}'. Resolve it or use an explicit DM override.");
        }
        if (!mechanic.CanResolve || mechanic.Definition is null)
        {
            throw new InvalidOperationException(
                $"Rules Core mechanic '{mechanic.MechanicKey}' can not currently resolve.");
        }
    }

    private static DistanceMeasure ToDistanceMeasure(TravelEnvironmentQuantity quantity)
    {
        var unit = quantity.Unit.Trim().ToLowerInvariant() switch
        {
            "mile" or "miles" => DistanceUnit.Miles,
            "kilometer" or "kilometers" => DistanceUnit.Kilometers,
            _ => throw new InvalidOperationException(
                $"Rules Core returned unsupported travel distance unit '{quantity.Unit}'. Hex Crawl will not guess a conversion.")
        };
        return new DistanceMeasure((double)quantity.Value, unit);
    }

    private static string DescribeQuantity(TravelEnvironmentQuantity quantity) =>
        quantity.PerUnit is null
            ? $"{quantity.Value.ToString(CultureInfo.InvariantCulture)} {quantity.Unit}"
            : $"{quantity.Value.ToString(CultureInfo.InvariantCulture)} {quantity.Unit}/{quantity.PerUnit}";

    private static string DescribeEvaluation(
        StoredExpedition expedition,
        TravelEnvironmentEvaluationView evaluation,
        string outcome)
    {
        var scope = expedition.CampaignId.HasValue
            ? $"campaign {expedition.CampaignId.Value:D}"
            : "global effective rules";
        var sources = evaluation.SourceAttributions
            .Select(source => source.SourceCode ?? source.WorkDisplayName ?? source.WorkKey ?? source.Provider)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceText = sources.Length == 0
            ? "source provenance retained by Rules Core"
            : "sources=" + string.Join(", ", sources);
        return $"Rules Core {evaluation.MechanicKey} ({scope}; {sourceText}) resolved {outcome}.";
    }
}
