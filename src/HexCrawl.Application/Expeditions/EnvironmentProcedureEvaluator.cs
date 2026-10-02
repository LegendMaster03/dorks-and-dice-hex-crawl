using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public enum EnvironmentProcedureEvaluationStatus
{
    Resolved,
    Partial,
    RequiresAdjudication
}

public sealed record EnvironmentProcedureEvaluation(
    EnvironmentProcedureEvaluationStatus Status,
    EffectiveEnvironmentContext Context,
    string? TerrainKey,
    string? RouteKey,
    string? WeatherAdjustmentModel,
    MovementCompositionInput MovementInput,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> UnsupportedSemantics,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Provenance);

public static class EnvironmentProcedureEvaluator
{
    public static EnvironmentProcedureEvaluation Evaluate(
        StoredExpedition expedition,
        EffectiveEnvironmentContext context)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(context);

        var policy = MovementCompositionPolicyResolver.Resolve(expedition.CampaignProcedure);
        var missing = new List<string>();
        var unsupported = new List<string>();
        var diagnostics = new List<string>();
        var provenance = new List<string>();
        var contributors = new List<MovementCapabilityContributor>();
        string? terrainKey = null;
        string? routeKey = null;

        var terrain = Tags(context, EnvironmentDimensions.Terrain);
        var route = Tags(context, EnvironmentDimensions.Route);
        var weather = Tags(context, EnvironmentDimensions.Weather);

        foreach (var fact in context.Facts.Where(x => x.Effective))
        {
            provenance.Add(DescribeSource(fact));
        }

        if (HasConflict(context, EnvironmentDimensions.Terrain))
        {
            contributors.Add(AdjudicationContributor(
                "environment:terrain-conflict",
                "terrain-conflict",
                "Effective terrain contains conflicting equally authoritative values."));
            diagnostics.Add("Effective terrain requires adjudication before movement interpretation.");
        }
        else if (terrain.Count > 0 && policy.Terrain.Support == MovementTerrainPolicySupport.Unsupported)
        {
            if (terrain.Count == 1) terrainKey = terrain[0];
            unsupported.Add(policy.Terrain.UnsupportedReason ?? "The pinned terrain policy is unsupported.");
            if (terrain.Count > 1)
            {
                contributors.Add(AdjudicationContributor(
                    "environment:unsupported-terrain-policy",
                    "unsupported-terrain-policy",
                    "Multiple terrain values can not be interpreted by the unsupported pinned terrain policy."));
            }
        }
        else if (terrain.Count > 0 && policy.Terrain.Support == MovementTerrainPolicySupport.Supported)
        {
            var understood = terrain
                .Where(policy.Terrain.TerrainAdjustments.ContainsKey)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var unknown = terrain.Except(understood, StringComparer.Ordinal).ToArray();
            foreach (var value in unknown)
            {
                unsupported.Add($"The pinned movement.terrain policy does not define terrain '{value}'.");
            }

            if (terrain.Count > 1 && understood.Length == 1 && unknown.Length > 0)
            {
                contributors.Add(AdjudicationContributor(
                    "environment:terrain-ambiguity",
                    "terrain-ambiguity",
                    "Multiple current terrain facts exist and only a subset is understood by the pinned procedure."));
                diagnostics.Add("Multiple current terrain facts require DM selection because automatically applying the one understood value would silently ignore competing world truth.");
            }
            else if (understood.Length == 1)
            {
                terrainKey = understood[0];
            }
            else if (understood.Length > 1)
            {
                var mapped = understood.Select(x => policy.Terrain.TerrainAdjustments[x]).Distinct(StringComparer.Ordinal).ToArray();
                if (mapped.Length == 1)
                {
                    terrainKey = understood[0];
                    diagnostics.Add("Multiple terrain tags map to the same pinned mechanical value; a deterministic equivalent input was selected.");
                }
                else
                {
                    contributors.Add(AdjudicationContributor(
                        "environment:terrain-ambiguity",
                        "terrain-ambiguity",
                        "Multiple applicable terrain tags map to materially different pinned values."));
                    diagnostics.Add("Multiple applicable terrain tags require DM selection because the pinned procedure defines no combination rule.");
                }
            }
        }

        if (route.Count > 0 && policy.Terrain.Support == MovementTerrainPolicySupport.Supported)
        {
            if (string.Equals(policy.Terrain.RouteAdjustmentModel, "none", StringComparison.Ordinal))
            {
                diagnostics.Add("The pinned route adjustment model is 'none'; route facts remain visible but do not alter movement.");
            }
            else if (route.Count == 1)
            {
                routeKey = route[0];
            }
            else
            {
                contributors.Add(AdjudicationContributor(
                    "environment:route-ambiguity",
                    "route-ambiguity",
                    "Multiple current route facts require adjudication for the pinned route model."));
            }
        }
        else if (route.Count > 0 && policy.Terrain.Support == MovementTerrainPolicySupport.Unsupported)
        {
            unsupported.Add("Route facts are preserved, but the pinned movement.terrain mechanic is unsupported.");
        }

        if (weather.Count > 0 && policy.Terrain.Support == MovementTerrainPolicySupport.Supported)
        {
            var model = policy.Terrain.WeatherAdjustmentModel;
            if (!string.Equals(model, "none", StringComparison.Ordinal))
            {
                contributors.Add(AdjudicationContributor(
                    "environment:weather",
                    $"weather:{model}",
                    $"Current weather is available, but pinned weather model '{model}' does not define a safe local formula."));
                diagnostics.Add($"Weather model '{model}' remains manual/provider-resolved; no weather formula was invented.");
            }
        }

        var relevantConflict = context.Conflicts.Any(x =>
            x.Dimension is EnvironmentDimensions.Terrain or EnvironmentDimensions.Route or EnvironmentDimensions.Weather);
        var status = relevantConflict || contributors.Any(x => x.Operation == MovementCapabilityOperation.SymbolicLimit)
            ? EnvironmentProcedureEvaluationStatus.RequiresAdjudication
            : unsupported.Count > 0
                ? EnvironmentProcedureEvaluationStatus.Partial
                : EnvironmentProcedureEvaluationStatus.Resolved;

        var externalDiagnostic = provenance.Count == 0
            ? null
            : "Environment sources: " + string.Join(" | ", provenance.Distinct(StringComparer.Ordinal));
        var movementInput = new MovementCompositionInput(
            TerrainKey: terrainKey,
            RouteKey: routeKey,
            ResolvedContributors: contributors,
            MissingInputs: missing,
            ExternalDiagnostic: externalDiagnostic);

        return new(
            status,
            context,
            terrainKey,
            routeKey,
            policy.Terrain.WeatherAdjustmentModel,
            movementInput,
            missing,
            unsupported,
            diagnostics,
            provenance.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> Tags(EffectiveEnvironmentContext context, string dimension) =>
        context.Facts
            .Where(x => x.Effective)
            .Where(x => string.Equals(x.Fact.Dimension, dimension, StringComparison.Ordinal))
            .Where(x => x.Fact.ValueKind == EnvironmentValueKind.Tag)
            .Select(x => x.Fact.Tag!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static bool HasConflict(EffectiveEnvironmentContext context, string dimension) =>
        context.Conflicts.Any(x => string.Equals(x.Dimension, dimension, StringComparison.Ordinal));

    private static MovementCapabilityContributor AdjudicationContributor(string key, string symbolic, string provenance) => new()
    {
        Id = StableGuid(key + "|" + symbolic),
        Kind = MovementCapabilityContributorKind.Environment,
        Key = key,
        Operation = MovementCapabilityOperation.SymbolicLimit,
        Scope = MovementCapabilityScope.Party,
        SymbolicValue = symbolic,
        Provenance = provenance
    };

    private static Guid StableGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string DescribeSource(EffectiveEnvironmentFact value)
    {
        var source = value.Source.Kind switch
        {
            EnvironmentFactSourceKind.World => "world",
            EnvironmentFactSourceKind.Hex => $"hex {value.Source.Hex}",
            EnvironmentFactSourceKind.SpatialFeature => $"feature {value.Source.FeatureName ?? value.Source.FeatureId?.ToString("D")}",
            EnvironmentFactSourceKind.ExpeditionCurrent => "expedition current state",
            EnvironmentFactSourceKind.DmOverride => "DM override",
            _ => "environment"
        };
        var declared = string.IsNullOrWhiteSpace(value.Fact.Provenance) ? "" : $" ({value.Fact.Provenance.Trim()})";
        return $"{value.Fact.Dimension} from {source}{declared}";
    }
}
