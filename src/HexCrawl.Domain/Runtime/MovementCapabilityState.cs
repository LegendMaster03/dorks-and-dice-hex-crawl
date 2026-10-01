using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum MovementCapabilityContributorKind
{
    Participant,
    Mount,
    Vehicle,
    Load,
    TravelMode,
    TerrainRoute,
    Environment,
    PersistentEffect,
    DmOverride
}

public enum MovementCapabilityOperation
{
    Base,
    Replace,
    Multiply,
    Add,
    Cap,
    Floor,
    Cost,
    SymbolicLimit
}

public enum MovementCapabilityScope
{
    Party,
    Participant,
    MovementUnit
}

/// <summary>
/// Expedition-owned movement consequence. This stores only the resolved movement meaning needed
/// by Hex Crawl; it deliberately does not copy inventories, character stat blocks, or named-system
/// taxonomies into the crawl session.
/// </summary>
public sealed record MovementCapabilityContributor
{
    public required Guid Id { get; init; }
    public required MovementCapabilityContributorKind Kind { get; init; }
    public required string Key { get; init; }
    public required MovementCapabilityOperation Operation { get; init; }
    public MovementCapabilityScope Scope { get; init; } = MovementCapabilityScope.Party;
    public double? Value { get; init; }
    public string? Unit { get; init; }
    public string? PerUnit { get; init; }
    public DistanceUnit? DistanceUnit { get; init; }
    public string? SymbolicValue { get; init; }
    public Guid? ParticipantId { get; init; }
    public string? MovementUnitKey { get; init; }
    public IReadOnlyList<Guid> ReplacesParticipantIds { get; init; } = [];
    public string? Provenance { get; init; }
    public string? Note { get; init; }
    public bool Enabled { get; init; } = true;

    public void Validate(IReadOnlySet<Guid> memberIds)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Movement contributor id is required.");
        }
        RequireText(Key, 200, "Movement contributor key");
        ValidateOptionalText(Unit, 100, "Movement contributor unit");
        ValidateOptionalText(PerUnit, 100, "Movement contributor per-unit");
        ValidateOptionalText(MovementUnitKey, 200, "Movement unit key");
        ValidateOptionalText(Provenance, 2000, "Movement contributor provenance");
        ValidateOptionalText(Note, 1000, "Movement contributor note");

        if (Value.HasValue && (!double.IsFinite(Value.Value) || Value.Value < 0))
        {
            throw new InvalidOperationException("Movement contributor numeric values must be finite and non-negative.");
        }

        if (Operation == MovementCapabilityOperation.SymbolicLimit)
        {
            RequireText(SymbolicValue, 500, "Symbolic movement limit");
            if (Value.HasValue)
            {
                throw new InvalidOperationException("A symbolic movement limit can not also declare a numeric value.");
            }
        }
        else if (!Value.HasValue)
        {
            throw new InvalidOperationException($"Movement operation '{Operation}' requires a numeric value.");
        }

        if (DistanceUnit.HasValue && string.IsNullOrWhiteSpace(Unit))
        {
            throw new InvalidOperationException("A physical movement contributor requires an explicit unit key in addition to its distance-unit metadata.");
        }

        if (ParticipantId.HasValue && !memberIds.Contains(ParticipantId.Value))
        {
            throw new InvalidOperationException("Movement contributor references a party member that does not exist.");
        }
        if (Scope == MovementCapabilityScope.Participant && !ParticipantId.HasValue)
        {
            throw new InvalidOperationException("A participant-scoped movement contributor requires a participant id.");
        }
        if (Kind == MovementCapabilityContributorKind.Participant && !ParticipantId.HasValue)
        {
            throw new InvalidOperationException("A participant movement contributor requires a participant id.");
        }
        if (Kind == MovementCapabilityContributorKind.DmOverride && Operation != MovementCapabilityOperation.Replace)
        {
            throw new InvalidOperationException("A DM movement override must use replacement semantics.");
        }

        if (ReplacesParticipantIds.Count != ReplacesParticipantIds.Distinct().Count())
        {
            throw new InvalidOperationException("A movement contributor can not replace the same participant more than once.");
        }
        foreach (var participantId in ReplacesParticipantIds)
        {
            if (!memberIds.Contains(participantId))
            {
                throw new InvalidOperationException("Movement contributor replacement assignment references a party member that does not exist.");
            }
        }
        if (ReplacesParticipantIds.Count > 0
            && Kind is not MovementCapabilityContributorKind.Mount
            and not MovementCapabilityContributorKind.Vehicle)
        {
            throw new InvalidOperationException("Only mount or vehicle contributors can replace participant movement units.");
        }
    }

    private static void RequireText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
        if (value.Length > maxLength)
        {
            throw new InvalidOperationException($"{label} is too long.");
        }
    }

    private static void ValidateOptionalText(string? value, int maxLength, string label)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} can not be blank.");
        }
        if (value is { Length: var length } && length > maxLength)
        {
            throw new InvalidOperationException($"{label} is too long.");
        }
    }
}
