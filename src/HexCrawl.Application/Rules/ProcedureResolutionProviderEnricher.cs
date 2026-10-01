using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Rules;

public sealed class ProcedureResolutionProviderEnricher(TravelEnvironmentProviderRegistry providers)
{
    public async Task<ProcedureResolutionHelperCommand> PrepareAsync(
        StoredExpedition expedition,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(command);

        var needsDistance = command.ExpectedDistance is null
            && !string.IsNullOrWhiteSpace(command.TravelDistanceRule);
        var needsNavigation = command.NavigationDifficultyClass is null
            && command.NavigationRiskFactors.Count > 0;
        if (!needsDistance && !needsNavigation)
        {
            return command;
        }

        var prepared = command;
        TravelEnvironmentProviderSelection? selection = null;

        if (needsDistance)
        {
            var local = MovementCapabilityComposer.Compose(expedition);
            if (local.Status == MovementCompositionStatus.Resolved
                && local.SuggestedExpectedDistance is not null)
            {
                prepared = ApplyComposition(prepared, local);
            }
            else
            {
                selection = providers.Select();
                if (selection.Provider is null)
                {
                    if (local.SuggestedExpectedDistance is not null)
                    {
                        prepared = ApplyComposition(prepared, local);
                    }
                    else
                    {
                        throw ProviderProblem(
                            null,
                            TravelMechanicKey(command.TravelDistanceRule!),
                            TravelEnvironmentProviderResolutionStates.Unavailable,
                            selection.Detail);
                    }
                }
                else
                {
                    try
                    {
                        prepared = await ResolveDistanceAsync(
                            selection.Provider,
                            expedition,
                            prepared,
                            cancellationToken);
                    }
                    catch (OptionalProviderResolutionException exception)
                        when (string.Equals(
                                exception.Status,
                                TravelEnvironmentProviderResolutionStates.Unavailable,
                                StringComparison.Ordinal)
                            && local.SuggestedExpectedDistance is not null)
                    {
                        prepared = ApplyComposition(prepared, local);
                    }
                }
            }
        }

        if (needsNavigation)
        {
            selection ??= providers.Select();
            if (selection.Provider is null)
            {
                throw ProviderProblem(
                    null,
                    TravelEnvironmentMechanicKeys.AvoidGettingLost,
                    TravelEnvironmentProviderResolutionStates.Unavailable,
                    selection.Detail);
            }
            prepared = await ResolveNavigationAsync(
                selection.Provider,
                expedition,
                prepared,
                cancellationToken);
        }

        return prepared;
    }

    private static async Task<ProcedureResolutionHelperCommand> ResolveDistanceAsync(
        ITravelEnvironmentProvider provider,
        StoredExpedition expedition,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken)
    {
        if (expedition.Runtime is not ExpeditionState)
        {
            throw new InvalidOperationException(
                "Provider-backed travel distance is available only for spatial crawl sessions.");
        }
        if (command.BaseSpeedFeet is null or <= 0)
        {
            throw new InvalidOperationException(
                "Provider-backed walking or hustling distance requires a positive base speed in feet.");
        }

        var rule = command.TravelDistanceRule!.Trim().ToLowerInvariant();
        var mechanicKey = TravelMechanicKey(rule);
        var resolved = await ResolveRequiredAsync(
            provider,
            expedition.CampaignId,
            mechanicKey,
            new TravelEnvironmentResolutionRequest(
                IntegerInputs: new Dictionary<string, int>
                {
                    ["base-speed-feet"] = command.BaseSpeedFeet.Value
                },
                StringInputs: rule == "walk"
                    ? new Dictionary<string, string> { ["period"] = "hour" }
                    : null),
            cancellationToken);
        var metadata = resolved.Provider ?? provider.Metadata;
        var evaluation = resolved.Evaluation!;
        var quantity = evaluation.Quantity
            ?? throw new InvalidOperationException(
                $"{ProviderLabel(metadata)} resolved '{mechanicKey}' without a quantity.");
        if (!string.Equals(quantity.PerUnit, "hour", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ProviderLabel(metadata)} '{mechanicKey}' did not return an hourly distance quantity, so Hex Crawl will not reinterpret it.");
        }

        var hourly = ToDistanceMeasure(quantity, metadata);
        var contributors = new List<MovementCapabilityContributor>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Kind = MovementCapabilityContributorKind.Environment,
                Key = $"provider:{mechanicKey}",
                Operation = MovementCapabilityOperation.Base,
                Scope = MovementCapabilityScope.Party,
                Value = hourly.Value,
                Unit = hourly.Unit.Symbol,
                PerUnit = "hour",
                DistanceUnit = hourly.Unit,
                Provenance = DescribeEvaluation(
                    expedition,
                    metadata,
                    evaluation,
                    DescribeQuantity(quantity))
            }
        };

        var hasTerrain = !string.IsNullOrWhiteSpace(command.Terrain);
        var hasRoute = !string.IsNullOrWhiteSpace(command.Route);
        if (hasTerrain != hasRoute)
        {
            throw new InvalidOperationException(
                "Provider-backed terrain travel requires both terrain and route, or neither.");
        }
        if (hasTerrain)
        {
            EnsureProviderTerrainCanCompose(expedition);
            var catalogResult = await provider.GetCatalogAsync(expedition.CampaignId, cancellationToken);
            var catalog = RequireCatalog(provider, catalogResult, TravelEnvironmentMechanicKeys.TerrainDistanceFactor);
            var mechanic = catalog.Mechanics.SingleOrDefault(value =>
                string.Equals(
                    value.MechanicKey,
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    StringComparison.Ordinal));
            if (mechanic is null)
            {
                throw ProviderProblem(
                    provider.Metadata,
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    TravelEnvironmentProviderResolutionStates.Unsupported,
                    "The provider does not expose a resolvable terrain-distance capability.");
            }
            if (string.Equals(mechanic.State, TravelEnvironmentMechanicStates.Conflicted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mechanic.State, TravelEnvironmentMechanicStates.RequiresAdjudication, StringComparison.OrdinalIgnoreCase))
            {
                throw ProviderProblem(
                    provider.Metadata,
                    mechanic.MechanicKey,
                    TravelEnvironmentProviderResolutionStates.RequiresAdjudication,
                    "The provider reports an unresolved conflict for the terrain-distance capability.");
            }
            if (!mechanic.CanResolve || mechanic.Definition is null)
            {
                throw ProviderProblem(
                    provider.Metadata,
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    TravelEnvironmentProviderResolutionStates.Unsupported,
                    "The provider does not expose a resolvable terrain-distance capability.");
            }
            var semantic = mechanic.Definition.FactorSemantic;
            if (!string.Equals(semantic, "distance-multiplier", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{ProviderLabel(provider.Metadata)} terrain factor semantic is '{semantic ?? "unspecified"}', not 'distance-multiplier'; Hex Crawl will not reinterpret it.");
            }

            var factorResult = await ResolveRequiredAsync(
                provider,
                expedition.CampaignId,
                TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                new TravelEnvironmentResolutionRequest(
                    StringInputs: new Dictionary<string, string>
                    {
                        ["terrain"] = command.Terrain!.Trim(),
                        ["route"] = command.Route!.Trim()
                    }),
                cancellationToken);
            var factorMetadata = factorResult.Provider ?? provider.Metadata;
            var factorEvaluation = factorResult.Evaluation!;
            var factor = factorEvaluation.Factor
                ?? throw new InvalidOperationException(
                    $"{ProviderLabel(factorMetadata)} resolved the terrain-distance capability without a factor.");
            if (factor < 0)
            {
                throw new InvalidOperationException(
                    $"{ProviderLabel(factorMetadata)} returned a negative terrain distance factor.");
            }

            contributors.Add(new MovementCapabilityContributor
            {
                Id = Guid.NewGuid(),
                Kind = MovementCapabilityContributorKind.Environment,
                Key = "provider:terrain-distance-factor",
                Operation = MovementCapabilityOperation.Multiply,
                Scope = MovementCapabilityScope.Party,
                Value = (double)factor,
                Unit = "factor",
                Provenance = DescribeEvaluation(
                    expedition,
                    factorMetadata,
                    factorEvaluation,
                    $"factor={factor.ToString(CultureInfo.InvariantCulture)}; semantic={semantic}")
            });
        }

        var composition = MovementCapabilityComposer.Compose(
            expedition,
            new MovementCompositionInput(ResolvedContributors: contributors));
        if (composition.Status != MovementCompositionStatus.Resolved
            || composition.SuggestedExpectedDistance is not { } expected)
        {
            var detail = composition.Diagnostics.Count == 0
                ? composition.Status.ToString()
                : string.Join(" ", composition.Diagnostics);
            throw new InvalidOperationException(
                $"Provider-backed movement capabilities could not produce a resolved physical watch-distance suggestion. {detail}");
        }

        return command with
        {
            ExpectedDistance = expected.Value,
            ExpectedDistanceRulesNote = DescribeComposition(composition)
        };
    }

    private static void EnsureProviderTerrainCanCompose(StoredExpedition expedition)
    {
        var policy = MovementCompositionPolicyResolver.Resolve(expedition.CampaignProcedure);
        if (policy.Terrain.Support != MovementTerrainPolicySupport.Supported)
        {
            return;
        }
        if (policy.Terrain.AdjustmentModel is "multiplier" or "distance-per-hour-multiplier")
        {
            return;
        }

        throw new InvalidOperationException(
            $"The pinned movement.terrain policy uses '{policy.Terrain.AdjustmentModel}', so a provider distance multiplier can not be substituted for that semantic.");
    }

    private static ProcedureResolutionHelperCommand ApplyComposition(
        ProcedureResolutionHelperCommand command,
        MovementCapabilityComposition composition)
    {
        var suggestion = composition.SuggestedExpectedDistance
            ?? throw new InvalidOperationException(
                "Movement composition did not produce a physical watch-distance suggestion.");
        return command with
        {
            ExpectedDistance = suggestion.Value,
            ExpectedDistanceRulesNote = DescribeComposition(composition)
        };
    }

    private static string DescribeComposition(MovementCapabilityComposition composition)
    {
        var parts = composition.Provenance
            .Concat(composition.Diagnostics)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return parts.Length == 0
            ? "Movement capability composition supplied the expected distance."
            : string.Join(" ", parts);
    }

    private static async Task<ProcedureResolutionHelperCommand> ResolveNavigationAsync(
        ITravelEnvironmentProvider provider,
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

        var resolved = await ResolveRequiredAsync(
            provider,
            expedition.CampaignId,
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            new TravelEnvironmentResolutionRequest(
                StringListInputs: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["risk-factors"] = riskFactors
                }),
            cancellationToken);
        var metadata = resolved.Provider ?? provider.Metadata;
        var evaluation = resolved.Evaluation!;
        var check = evaluation.Check
            ?? throw new InvalidOperationException(
                $"{ProviderLabel(metadata)} resolved the navigation capability without a check DC.");
        return command with
        {
            NavigationDifficultyClass = check.Dc,
            NavigationDifficultyRulesNote = DescribeEvaluation(
                expedition,
                metadata,
                evaluation,
                $"DC={check.Dc}; cadence={check.Cadence ?? "unspecified"}; competency={check.CompetencyConceptKey ?? "unspecified"}")
        };
    }

    private static async Task<TravelEnvironmentProviderResolutionResult> ResolveRequiredAsync(
        ITravelEnvironmentProvider provider,
        Guid? campaignId,
        string mechanicKey,
        TravelEnvironmentResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await provider.ResolveAsync(campaignId, mechanicKey, request, cancellationToken);
        if (string.Equals(result.Status, TravelEnvironmentProviderResolutionStates.Resolved, StringComparison.Ordinal))
        {
            return result.Evaluation is null
                ? throw new InvalidOperationException(
                    $"{ProviderLabel(result.Provider ?? provider.Metadata)} reported '{mechanicKey}' as resolved without an evaluation.")
                : result;
        }
        throw ProviderProblem(
            result.Provider ?? provider.Metadata,
            mechanicKey,
            result.Status,
            result.Detail,
            result.MissingInputKeys);
    }

    private static TravelEnvironmentCatalogView RequireCatalog(
        ITravelEnvironmentProvider provider,
        TravelEnvironmentProviderCatalogResult result,
        string mechanicKey)
    {
        if (string.Equals(result.Availability, TravelEnvironmentProviderAvailabilityStates.Available, StringComparison.Ordinal)
            && result.Catalog is not null)
        {
            return result.Catalog;
        }
        var status = string.Equals(result.Availability, TravelEnvironmentProviderAvailabilityStates.Failed, StringComparison.Ordinal)
            ? TravelEnvironmentProviderResolutionStates.Failed
            : TravelEnvironmentProviderResolutionStates.Unavailable;
        throw ProviderProblem(result.Provider ?? provider.Metadata, mechanicKey, status, result.Detail);
    }

    private static OptionalProviderResolutionException ProviderProblem(
        TravelEnvironmentProviderMetadata? provider,
        string mechanicKey,
        string status,
        string? detail,
        IReadOnlyList<string>? missingInputKeys = null)
    {
        var missing = missingInputKeys ?? [];
        var label = ProviderLabel(provider);
        var message = status switch
        {
            TravelEnvironmentProviderResolutionStates.InputRequired => missing.Count == 0
                ? $"{label} requires additional input for '{mechanicKey}'. Enter provider inputs or an explicit DM value."
                : $"{label} requires: {string.Join(", ", missing)} for '{mechanicKey}'. Enter provider inputs or an explicit DM value.",
            TravelEnvironmentProviderResolutionStates.NotApplicable =>
                $"{label} reports '{mechanicKey}' as not applicable. Enter an explicit DM value if the procedure still requires one.",
            TravelEnvironmentProviderResolutionStates.RequiresAdjudication =>
                $"{label} requires adjudication for '{mechanicKey}'. Resolve the external rule or enter an explicit DM value.",
            TravelEnvironmentProviderResolutionStates.Unsupported =>
                $"{label} does not provide '{mechanicKey}'. Enter an explicit DM value if the procedure requires one.",
            TravelEnvironmentProviderResolutionStates.Failed =>
                $"{label} failed while resolving '{mechanicKey}'. Enter an explicit DM value or retry later.",
            _ =>
                $"{label} is unavailable for '{mechanicKey}'. Enter an explicit DM value or use another provider when available."
        };
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" {detail.Trim()}";
        }
        return new OptionalProviderResolutionException(mechanicKey, status, message, missing, provider);
    }

    private static string TravelMechanicKey(string rule) => rule.Trim().ToLowerInvariant() switch
    {
        "walk" => TravelEnvironmentMechanicKeys.WalkDistance,
        "hustle" => TravelEnvironmentMechanicKeys.HustleDistance,
        _ => throw new InvalidOperationException(
            "Provider-backed travel distance must use either the walk or hustle capability.")
    };

    private static DistanceMeasure ToDistanceMeasure(
        TravelEnvironmentQuantity quantity,
        TravelEnvironmentProviderMetadata provider)
    {
        var unit = quantity.Unit.Trim().ToLowerInvariant() switch
        {
            "mile" or "miles" => DistanceUnit.Miles,
            "kilometer" or "kilometers" => DistanceUnit.Kilometers,
            _ => throw new InvalidOperationException(
                $"{ProviderLabel(provider)} returned unsupported travel distance unit '{quantity.Unit}'. Hex Crawl will not guess a conversion.")
        };
        return new DistanceMeasure((double)quantity.Value, unit);
    }

    private static string DescribeQuantity(TravelEnvironmentQuantity quantity) =>
        quantity.PerUnit is null
            ? $"{quantity.Value.ToString(CultureInfo.InvariantCulture)} {quantity.Unit}"
            : $"{quantity.Value.ToString(CultureInfo.InvariantCulture)} {quantity.Unit}/{quantity.PerUnit}";

    private static string DescribeEvaluation(
        StoredExpedition expedition,
        TravelEnvironmentProviderMetadata provider,
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
            ? "provider source provenance retained"
            : "sources=" + string.Join(", ", sources);
        return $"Provider: {provider.DisplayName}; mechanic={evaluation.MechanicKey}; scope={scope}; {sourceText}; resolved {outcome}.";
    }

    private static string ProviderLabel(TravelEnvironmentProviderMetadata? provider) => provider is null
        ? "External travel/environment provider"
        : $"External travel/environment provider '{provider.DisplayName}'";
}
