using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public enum EnvironmentContextStatus { Resolved, RequiresAdjudication, Unavailable }
public enum EnvironmentFactSourceKind { World, Hex, SpatialFeature, ExpeditionCurrent, DmOverride }

public sealed record EnvironmentFactSource(
    EnvironmentFactSourceKind Kind,
    Guid? AnnotationId = null,
    HexCoordinate? Hex = null,
    Guid? FeatureId = null,
    string? FeatureName = null);

public sealed record EffectiveEnvironmentFact(
    EnvironmentFact Fact,
    EnvironmentFactSource Source,
    int Precedence,
    bool Effective);

public sealed record EnvironmentConflict(
    string Dimension,
    IReadOnlyList<EffectiveEnvironmentFact> Candidates,
    string Detail);

public sealed record EffectiveEnvironmentContext(
    EnvironmentContextStatus Status,
    IReadOnlyList<EffectiveEnvironmentFact> Facts,
    IReadOnlyList<EnvironmentConflict> Conflicts);

public static class EnvironmentContextResolver
{
    private sealed record Candidate(EnvironmentFact Fact, EnvironmentFactSource Source, int Precedence);

    public static EffectiveEnvironmentContext Resolve(StoredExpedition expedition, OverworldDefinition? world = null)
    {
        expedition.Environment.Validate();
        var candidates = new List<Candidate>();

        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            if (world is null || world.Id != worldContext.WorldId)
            {
                throw new InvalidOperationException("World-bound environment resolution requires the matching overworld.");
            }
            world.ValidateEnvironmentAnnotations();
            AddWorld(expedition, world, candidates);
        }
        else if (world is not null)
        {
            throw new InvalidOperationException("A non-world crawl context can not resolve environment from an overworld.");
        }

        Add(candidates, expedition.Environment.CurrentFacts,
            new EnvironmentFactSource(EnvironmentFactSourceKind.ExpeditionCurrent), 1);
        Add(candidates, expedition.Environment.Overrides,
            new EnvironmentFactSource(EnvironmentFactSourceKind.DmOverride), 2);

        if (candidates.Count == 0)
        {
            return new(EnvironmentContextStatus.Unavailable, [], []);
        }

        var facts = new List<EffectiveEnvironmentFact>();
        var conflicts = new List<EnvironmentConflict>();
        foreach (var group in candidates.GroupBy(x => x.Fact.Dimension.Trim(), StringComparer.Ordinal)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var precedence = group.Max(x => x.Precedence);
            var active = group.Where(x => x.Precedence == precedence)
                .OrderBy(x => SourceOrder(x.Source.Kind))
                .ThenBy(x => x.Fact.Id)
                .ToArray();
            var conflict = FindConflict(group.Key, active);
            if (conflict is not null) conflicts.Add(conflict);

            foreach (var candidate in group.OrderBy(x => x.Precedence)
                         .ThenBy(x => SourceOrder(x.Source.Kind))
                         .ThenBy(x => x.Fact.Id))
            {
                facts.Add(new(candidate.Fact, candidate.Source, candidate.Precedence,
                    candidate.Precedence == precedence));
            }
        }

        return new(
            conflicts.Count == 0 ? EnvironmentContextStatus.Resolved : EnvironmentContextStatus.RequiresAdjudication,
            facts,
            conflicts);
    }

    private static void AddWorld(StoredExpedition expedition, OverworldDefinition world, List<Candidate> candidates)
    {
        var currentHex = expedition.Runtime is ExpeditionState state ? state.CurrentHex : (HexCoordinate?)null;
        var intersecting = currentHex.HasValue
            ? world.FeaturesIntersecting(currentHex.Value).ToDictionary(x => x.Id)
            : new Dictionary<Guid, SpatialFeature>();

        foreach (var annotation in world.EnvironmentAnnotations)
        {
            if (annotation.Scope.Kind == EnvironmentAnnotationScopeKind.World)
            {
                Add(candidates, annotation.Facts,
                    new EnvironmentFactSource(EnvironmentFactSourceKind.World, annotation.Id), 0);
            }
            else if (annotation.Scope.Kind == EnvironmentAnnotationScopeKind.Hex
                     && currentHex.HasValue && annotation.Scope.Hex == currentHex)
            {
                Add(candidates, annotation.Facts,
                    new EnvironmentFactSource(EnvironmentFactSourceKind.Hex, annotation.Id, currentHex), 0);
            }
            else if (annotation.Scope.Kind == EnvironmentAnnotationScopeKind.SpatialFeature
                     && annotation.Scope.FeatureId is { } id && intersecting.TryGetValue(id, out var feature))
            {
                Add(candidates, annotation.Facts,
                    new EnvironmentFactSource(EnvironmentFactSourceKind.SpatialFeature, annotation.Id,
                        currentHex, feature.Id, feature.Name), 0);
            }
        }
    }

    private static void Add(List<Candidate> target, IReadOnlyList<EnvironmentFact> facts,
        EnvironmentFactSource source, int precedence)
    {
        foreach (var fact in facts)
        {
            fact.Validate();
            target.Add(new(fact, source, precedence));
        }
    }

    private static EnvironmentConflict? FindConflict(string dimension, IReadOnlyList<Candidate> active)
    {
        if (active.Count <= 1) return null;
        if (active.Select(x => x.Fact.ValueKind).Distinct().Count() > 1)
        {
            return Conflict(dimension, active, "Equally authoritative facts use incompatible value kinds.");
        }
        if (active[0].Fact.ValueKind == EnvironmentValueKind.Tag) return null;

        var values = active.Select(x => x.Fact.Measurement!)
            .Select(x => (x.Value, Unit: x.Unit.Trim())).Distinct().Count();
        return values <= 1 ? null : Conflict(dimension, active,
            "Equally authoritative scalar facts disagree and require adjudication.");
    }

    private static EnvironmentConflict Conflict(string dimension, IReadOnlyList<Candidate> active, string detail) =>
        new(dimension, active.Select(x => new EffectiveEnvironmentFact(x.Fact, x.Source, x.Precedence, true)).ToArray(), detail);

    private static int SourceOrder(EnvironmentFactSourceKind kind) => kind switch
    {
        EnvironmentFactSourceKind.World => 0,
        EnvironmentFactSourceKind.Hex => 1,
        EnvironmentFactSourceKind.SpatialFeature => 2,
        EnvironmentFactSourceKind.ExpeditionCurrent => 3,
        EnvironmentFactSourceKind.DmOverride => 4,
        _ => 5
    };
}
