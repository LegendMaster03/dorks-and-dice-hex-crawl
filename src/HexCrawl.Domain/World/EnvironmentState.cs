using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public static class EnvironmentDimensions
{
    public const string Terrain = "terrain";
    public const string Route = "route";
    public const string Weather = "weather";
    public const string Visibility = "visibility";
    public const string Elevation = "elevation";
    public const string Depth = "depth";
    public const string Water = "water";
    public const string Current = "current";
    public const string Temperature = "temperature";
    public const string Hazard = "hazard";
    public const string RegionalEffect = "regional-effect";
}

public enum EnvironmentValueKind
{
    Tag,
    Measurement
}

public sealed record EnvironmentMeasurement(double Value, string Unit)
{
    public void Validate()
    {
        if (!double.IsFinite(Value))
        {
            throw new InvalidOperationException("Environment measurements must be finite.");
        }
        if (string.IsNullOrWhiteSpace(Unit))
        {
            throw new InvalidOperationException("Environment measurements require an explicit unit.");
        }
        if (Unit.Length > 100)
        {
            throw new InvalidOperationException("Environment measurement unit is too long.");
        }
    }
}

/// <summary>
/// Ruleset-neutral statement about the world or current expedition conditions. Notes and
/// provenance are explanatory only; mechanical interpretation uses Dimension, ValueKind,
/// Tag, and Measurement.
/// </summary>
public sealed record EnvironmentFact
{
    public required Guid Id { get; init; }
    public required string Dimension { get; init; }
    public required EnvironmentValueKind ValueKind { get; init; }
    public string? Tag { get; init; }
    public EnvironmentMeasurement? Measurement { get; init; }
    public string? Provenance { get; init; }
    public string? Note { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Environment fact id is required.");
        }
        if (string.IsNullOrWhiteSpace(Dimension))
        {
            throw new InvalidOperationException("Environment fact dimension is required.");
        }
        if (Dimension.Length > 200)
        {
            throw new InvalidOperationException("Environment fact dimension is too long.");
        }
        if (Provenance is { Length: > 2000 })
        {
            throw new InvalidOperationException("Environment fact provenance is too long.");
        }
        if (Note is { Length: > 2000 })
        {
            throw new InvalidOperationException("Environment fact note is too long.");
        }

        switch (ValueKind)
        {
            case EnvironmentValueKind.Tag:
                if (string.IsNullOrWhiteSpace(Tag))
                {
                    throw new InvalidOperationException("Tag environment facts require a value.");
                }
                if (Tag.Length > 500)
                {
                    throw new InvalidOperationException("Environment tag value is too long.");
                }
                if (Measurement is not null)
                {
                    throw new InvalidOperationException("A tag environment fact can not also contain a measurement.");
                }
                break;
            case EnvironmentValueKind.Measurement:
                if (Tag is not null)
                {
                    throw new InvalidOperationException("A measurement environment fact can not also contain a tag value.");
                }
                if (Measurement is null)
                {
                    throw new InvalidOperationException("Measurement environment facts require a numeric value and unit.");
                }
                Measurement.Validate();
                break;
            default:
                throw new InvalidOperationException("Environment fact value kind is not supported.");
        }
    }
}

public enum EnvironmentAnnotationScopeKind
{
    World,
    Hex,
    SpatialFeature,
    Cell
}

public sealed record EnvironmentAnnotationScope
{
    public required EnvironmentAnnotationScopeKind Kind { get; init; }
    public HexCoordinate? Hex { get; init; }
    public Guid? FeatureId { get; init; }
    public WorldCellId? Cell { get; init; }

    public void Validate(IReadOnlySet<Guid> featureIds)
    {
        switch (Kind)
        {
            case EnvironmentAnnotationScopeKind.World:
                if (Hex is not null || FeatureId.HasValue || Cell.HasValue)
                {
                    throw new InvalidOperationException("A world environment scope can not reference a hex or spatial feature.");
                }
                break;
            case EnvironmentAnnotationScopeKind.Hex:
                if (Hex is null || FeatureId.HasValue || Cell.HasValue)
                {
                    throw new InvalidOperationException("A hex environment scope requires only a hex coordinate.");
                }
                break;
            case EnvironmentAnnotationScopeKind.SpatialFeature:
                if (Hex is not null || Cell.HasValue || !FeatureId.HasValue || FeatureId.Value == Guid.Empty)
                {
                    throw new InvalidOperationException("A spatial-feature environment scope requires only a feature id.");
                }
                if (!featureIds.Contains(FeatureId.Value))
                {
                    throw new InvalidOperationException("Environment annotation references a spatial feature that does not exist.");
                }
                break;
            case EnvironmentAnnotationScopeKind.Cell:
                if (Hex is not null || FeatureId.HasValue || Cell is null
                    || Cell.Value.TilingId == Guid.Empty || string.IsNullOrWhiteSpace(Cell.Value.Address.MotifCellId))
                    throw new InvalidOperationException("A cell environment scope requires only a qualified cell address.");
                Cell.Value.Address.Translation.ValidateWireRange();
                break;
            default:
                throw new InvalidOperationException("Environment annotation scope is not supported.");
        }
    }
}

public sealed record EnvironmentAnnotation
{
    public required Guid Id { get; init; }
    public required EnvironmentAnnotationScope Scope { get; init; }
    public IReadOnlyList<EnvironmentFact> Facts { get; init; } = [];

    public void Validate(IReadOnlySet<Guid> featureIds)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Environment annotation id is required.");
        }
        ArgumentNullException.ThrowIfNull(Scope);
        Scope.Validate(featureIds);
        ValidateFacts(Facts, "Environment annotation");
    }

    internal static void ValidateFacts(IReadOnlyList<EnvironmentFact> facts, string owner)
    {
        var ids = new HashSet<Guid>();
        foreach (var fact in facts)
        {
            fact.Validate();
            if (!ids.Add(fact.Id))
            {
                throw new InvalidOperationException($"{owner} environment fact ids must be unique.");
            }
        }
    }
}

/// <summary>
/// Expedition/session-owned current environment. CurrentFacts model transient conditions;
/// Overrides are explicit DM overrides. Static world truth remains on OverworldDefinition.
/// </summary>
public sealed record ExpeditionEnvironmentState
{
    public IReadOnlyList<EnvironmentFact> CurrentFacts { get; init; } = [];
    public IReadOnlyList<EnvironmentFact> Overrides { get; init; } = [];

    public static ExpeditionEnvironmentState Empty { get; } = new();

    public void Validate()
    {
        EnvironmentAnnotation.ValidateFacts(CurrentFacts, "Expedition current state");
        EnvironmentAnnotation.ValidateFacts(Overrides, "Expedition override state");
        var currentIds = CurrentFacts.Select(value => value.Id).ToHashSet();
        if (Overrides.Any(value => currentIds.Contains(value.Id)))
        {
            throw new InvalidOperationException("Current environment facts and DM overrides must use distinct fact ids.");
        }
    }
}
