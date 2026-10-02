using System.Text.Json.Serialization;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum ExpeditionConsequenceCategory
{
    TimeDelay,
    MovementChange,
    ResourceChange,
    ExposureFatigue,
    DamageEndurance,
    NavigationChange,
    EncounterCircumstance,
    PersistentEffectChange,
    Custom
}

public enum ExpeditionEffectScope
{
    Participant,
    Party,
    Mount,
    Vehicle,
    Expedition
}

public enum ExpeditionConsequenceSourceKind
{
    Dm,
    Procedure,
    EnvironmentResolution,
    Provider,
    ForcedTravelResult,
    JourneyEvent,
    Encounter,
    ExternalTool,
    ManualImport
}

public enum ExpeditionConsequenceStatus
{
    Applied,
    AlreadyApplied,
    Recorded,
    Deferred,
    InputRequired,
    RequiresAdjudication,
    Unsupported,
    ExternalActionRequired,
    Failed
}

public enum PersistentEffectChangeOperation
{
    AdjustLevel,
    SetLevel,
    SetMagnitude,
    SetState,
    Clear
}

public enum NavigationConsequenceOperation
{
    SetLostState,
    ClearLostState,
    AdjustVeer,
    ForceRecognitionCheck,
    Modifier,
    Custom
}

public enum TimeDelayUnit
{
    Minutes,
    Hours,
    Days
}

public sealed record ExpeditionEffectTarget(
    ExpeditionEffectScope Scope,
    Guid? TargetId = null)
{
    public void ValidateStructure()
    {
        if (TargetId == Guid.Empty)
        {
            throw new InvalidOperationException("Effect target id can not be empty.");
        }

        switch (Scope)
        {
            case ExpeditionEffectScope.Participant:
            case ExpeditionEffectScope.Mount:
            case ExpeditionEffectScope.Vehicle:
                if (!TargetId.HasValue)
                {
                    throw new InvalidOperationException($"{Scope} effect scope requires a target id.");
                }
                break;
            case ExpeditionEffectScope.Party:
            case ExpeditionEffectScope.Expedition:
                if (TargetId.HasValue)
                {
                    throw new InvalidOperationException($"{Scope} effect scope can not declare a target id.");
                }
                break;
            default:
                throw new InvalidOperationException("Effect scope is not supported.");
        }
    }

    public void ValidateAgainst(CrawlPartySheet party)
    {
        ValidateStructure();
        if (Scope == ExpeditionEffectScope.Participant
            && !party.Members.Any(value => value.Id == TargetId))
        {
            throw new InvalidOperationException("Effect target references a party participant that does not exist.");
        }

        if (Scope is ExpeditionEffectScope.Mount or ExpeditionEffectScope.Vehicle)
        {
            var expected = Scope == ExpeditionEffectScope.Mount
                ? MovementCapabilityContributorKind.Mount
                : MovementCapabilityContributorKind.Vehicle;
            if (!party.MovementContributors.Any(value => value.Id == TargetId && value.Kind == expected))
            {
                throw new InvalidOperationException(
                    $"Effect target references a {Scope.ToString().ToLowerInvariant()} movement contributor that does not exist.");
            }
        }
    }

    public string MergeIdentity => TargetId.HasValue
        ? $"{Scope}:{TargetId.Value:D}"
        : Scope.ToString();
}

public sealed record ExpeditionConsequenceProvenance(
    ExpeditionConsequenceSourceKind SourceKind,
    string SourceKey,
    string? SourceReference = null,
    string? ProviderName = null,
    string? Note = null)
{
    public void Validate()
    {
        RequireText(SourceKey, 200, "Consequence provenance source key");
        ValidateOptional(SourceReference, 512, "Consequence provenance source reference");
        ValidateOptional(ProviderName, 200, "Consequence provenance provider name");
        ValidateOptional(Note, 2000, "Consequence provenance note");
    }

    internal static void RequireText(string? value, int maxLength, string label)
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

    internal static void ValidateOptional(string? value, int maxLength, string label)
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TimeDelayConsequenceComponent), "timeDelay")]
[JsonDerivedType(typeof(MovementChangeConsequenceComponent), "movement")]
[JsonDerivedType(typeof(ResourceChangeConsequenceComponent), "resourceChange")]
[JsonDerivedType(typeof(PersistentEffectChangeConsequenceComponent), "persistentEffectChange")]
[JsonDerivedType(typeof(NavigationConsequenceComponent), "navigation")]
[JsonDerivedType(typeof(EncounterCircumstanceConsequenceComponent), "encounterCircumstance")]
[JsonDerivedType(typeof(ExternalStateConsequenceComponent), "externalState")]
[JsonDerivedType(typeof(CustomConsequenceComponent), "custom")]
public abstract record ExpeditionConsequenceComponent
{
    public abstract void Validate();
}

public sealed record TimeDelayConsequenceComponent(
    double Value,
    TimeDelayUnit Unit) : ExpeditionConsequenceComponent
{
    public override void Validate()
    {
        if (!double.IsFinite(Value) || Value <= 0)
        {
            throw new InvalidOperationException("Time-delay quantity must be positive and finite.");
        }
    }

    public TimeSpan ToTimeSpan() => Unit switch
    {
        TimeDelayUnit.Minutes => TimeSpan.FromMinutes(Value),
        TimeDelayUnit.Hours => TimeSpan.FromHours(Value),
        TimeDelayUnit.Days => TimeSpan.FromDays(Value),
        _ => throw new InvalidOperationException("Time-delay unit is not supported.")
    };
}

public sealed record MovementConsequenceComponent
{
    public required Guid Id { get; init; }
    public required string Key { get; init; }
    public required MovementCapabilityOperation Operation { get; init; }
    public double? Value { get; init; }
    public string? Unit { get; init; }
    public string? PerUnit { get; init; }
    public DistanceUnit? DistanceUnit { get; init; }
    public string? SymbolicValue { get; init; }
    public string? Note { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Movement effect component id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(Key, 200, "Movement effect component key");
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "Movement effect unit");
        ExpeditionConsequenceProvenance.ValidateOptional(PerUnit, 100, "Movement effect per-unit");
        ExpeditionConsequenceProvenance.ValidateOptional(SymbolicValue, 500, "Movement effect symbolic value");
        ExpeditionConsequenceProvenance.ValidateOptional(Note, 1000, "Movement effect note");

        if (Value.HasValue && (!double.IsFinite(Value.Value) || Value.Value < 0))
        {
            throw new InvalidOperationException("Movement effect numeric values must be finite and non-negative.");
        }
        if (Operation == MovementCapabilityOperation.SymbolicLimit)
        {
            ExpeditionConsequenceProvenance.RequireText(SymbolicValue, 500, "Movement effect symbolic value");
            if (Value.HasValue)
            {
                throw new InvalidOperationException("A symbolic movement effect can not also declare a numeric value.");
            }
        }
        else if (!Value.HasValue)
        {
            throw new InvalidOperationException($"Movement effect operation '{Operation}' requires a numeric value.");
        }
        if (DistanceUnit.HasValue && string.IsNullOrWhiteSpace(Unit))
        {
            throw new InvalidOperationException("A physical movement effect requires an explicit unit key.");
        }
    }
}

public sealed record MovementChangeConsequenceComponent(
    MovementConsequenceComponent Movement) : ExpeditionConsequenceComponent
{
    public override void Validate() => Movement.Validate();
}

public sealed record ResourceChangeConsequenceComponent(
    string ResourceKey,
    double Delta,
    string Unit) : ExpeditionConsequenceComponent
{
    public override void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(ResourceKey, 200, "Resource key");
        ExpeditionConsequenceProvenance.RequireText(Unit, 100, "Resource unit");
        if (!double.IsFinite(Delta) || Delta == 0)
        {
            throw new InvalidOperationException("Resource delta must be finite and non-zero.");
        }
    }
}

public sealed record PersistentEffectChangeConsequenceComponent : ExpeditionConsequenceComponent
{
    public required string EffectKey { get; init; }
    public required PersistentEffectChangeOperation Operation { get; init; }
    public int? LevelDelta { get; init; }
    public int? Level { get; init; }
    public double? Magnitude { get; init; }
    public string? Unit { get; init; }
    public string? State { get; init; }
    public bool ExplicitlyResolved { get; init; }
    public IReadOnlyList<MovementConsequenceComponent> MovementComponents { get; init; } = [];

    public override void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(EffectKey, 200, "Persistent effect key");
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "Persistent effect unit");
        ExpeditionConsequenceProvenance.ValidateOptional(State, 500, "Persistent effect state");
        if (Magnitude.HasValue && !double.IsFinite(Magnitude.Value))
        {
            throw new InvalidOperationException("Persistent effect magnitude must be finite.");
        }
        if (MovementComponents.Select(value => value.Id).Distinct().Count() != MovementComponents.Count)
        {
            throw new InvalidOperationException("Persistent effect movement component ids must be unique.");
        }
        foreach (var movement in MovementComponents)
        {
            movement.Validate();
        }

        switch (Operation)
        {
            case PersistentEffectChangeOperation.AdjustLevel when !LevelDelta.HasValue || LevelDelta == 0:
                throw new InvalidOperationException("Adjust-level effect change requires a non-zero level delta.");
            case PersistentEffectChangeOperation.SetLevel when !Level.HasValue || Level < 0:
                throw new InvalidOperationException("Set-level effect change requires a non-negative level.");
            case PersistentEffectChangeOperation.SetMagnitude when !Magnitude.HasValue:
                throw new InvalidOperationException("Set-magnitude effect change requires a finite magnitude.");
            case PersistentEffectChangeOperation.SetState when string.IsNullOrWhiteSpace(State):
                throw new InvalidOperationException("Set-state effect change requires a state value.");
        }
    }
}

public sealed record NavigationConsequenceComponent : ExpeditionConsequenceComponent
{
    public required NavigationConsequenceOperation Operation { get; init; }
    public bool? IsLost { get; init; }
    public int? VeerSteps { get; init; }
    public string? ModifierKey { get; init; }
    public double? Modifier { get; init; }

    public override void Validate()
    {
        ExpeditionConsequenceProvenance.ValidateOptional(ModifierKey, 200, "Navigation modifier key");
        if (Modifier.HasValue && !double.IsFinite(Modifier.Value))
        {
            throw new InvalidOperationException("Navigation modifier must be finite.");
        }
        if (Operation == NavigationConsequenceOperation.SetLostState && !IsLost.HasValue)
        {
            throw new InvalidOperationException("Set-lost navigation consequence requires an explicit state.");
        }
        if (Operation == NavigationConsequenceOperation.AdjustVeer && !VeerSteps.HasValue)
        {
            throw new InvalidOperationException("Adjust-veer navigation consequence requires an explicit step delta.");
        }
        if (Operation == NavigationConsequenceOperation.Modifier
            && (string.IsNullOrWhiteSpace(ModifierKey) || !Modifier.HasValue))
        {
            throw new InvalidOperationException("Navigation modifier consequence requires a key and finite modifier.");
        }
    }
}

public sealed record EncounterCircumstanceConsequenceComponent(
    string CircumstanceKey,
    string? Value = null) : ExpeditionConsequenceComponent
{
    public override void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(CircumstanceKey, 200, "Encounter circumstance key");
        ExpeditionConsequenceProvenance.ValidateOptional(Value, 500, "Encounter circumstance value");
    }
}

public sealed record ExternalStateConsequenceComponent(
    string StateKey,
    double Delta,
    string? Unit = null) : ExpeditionConsequenceComponent
{
    public override void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(StateKey, 200, "External state key");
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "External state unit");
        if (!double.IsFinite(Delta) || Delta == 0)
        {
            throw new InvalidOperationException("External state delta must be finite and non-zero.");
        }
    }
}

public sealed record CustomConsequenceComponent(
    string ComponentKey,
    double? Quantity = null,
    string? Unit = null,
    string? State = null) : ExpeditionConsequenceComponent
{
    public override void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(ComponentKey, 200, "Custom consequence component key");
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "Custom consequence unit");
        ExpeditionConsequenceProvenance.ValidateOptional(State, 1000, "Custom consequence state");
        if (Quantity.HasValue && !double.IsFinite(Quantity.Value))
        {
            throw new InvalidOperationException("Custom consequence quantity must be finite.");
        }
    }
}

public sealed record ExpeditionConsequence
{
    public required Guid Id { get; init; }
    public required string ConsequenceKey { get; init; }
    public required ExpeditionConsequenceCategory Category { get; init; }
    public required ExpeditionEffectTarget Target { get; init; }
    public required IReadOnlyList<ExpeditionConsequenceComponent> Components { get; init; }
    public required ExpeditionConsequenceProvenance Provenance { get; init; }
    public string? SourceReference { get; init; }
    public string? Note { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Consequence id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(ConsequenceKey, 200, "Consequence key");
        Target.ValidateStructure();
        Provenance.Validate();
        ExpeditionConsequenceProvenance.ValidateOptional(SourceReference, 512, "Consequence source reference");
        ExpeditionConsequenceProvenance.ValidateOptional(Note, 2000, "Consequence note");
        if (Components.Count == 0)
        {
            throw new InvalidOperationException("A consequence requires at least one structured component.");
        }
        foreach (var component in Components)
        {
            component.Validate();
        }
        ValidateCategoryComponents();
    }

    public void ValidateAgainst(CrawlPartySheet party)
    {
        Validate();
        Target.ValidateAgainst(party);
    }

    private void ValidateCategoryComponents()
    {
        bool Has<T>() where T : ExpeditionConsequenceComponent => Components.Any(value => value is T);
        var valid = Category switch
        {
            ExpeditionConsequenceCategory.TimeDelay => Has<TimeDelayConsequenceComponent>(),
            ExpeditionConsequenceCategory.MovementChange => Has<MovementChangeConsequenceComponent>(),
            ExpeditionConsequenceCategory.ResourceChange => Has<ResourceChangeConsequenceComponent>(),
            ExpeditionConsequenceCategory.ExposureFatigue => Has<PersistentEffectChangeConsequenceComponent>(),
            ExpeditionConsequenceCategory.DamageEndurance => Has<ExternalStateConsequenceComponent>(),
            ExpeditionConsequenceCategory.NavigationChange => Has<NavigationConsequenceComponent>(),
            ExpeditionConsequenceCategory.EncounterCircumstance => Has<EncounterCircumstanceConsequenceComponent>(),
            ExpeditionConsequenceCategory.PersistentEffectChange => Has<PersistentEffectChangeConsequenceComponent>(),
            ExpeditionConsequenceCategory.Custom => Has<CustomConsequenceComponent>(),
            _ => false
        };
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Consequence category '{Category}' does not contain a compatible structured component.");
        }
    }
}

public sealed record ExpeditionEffect
{
    public required Guid Id { get; init; }
    public required string EffectKey { get; init; }
    public required ExpeditionEffectTarget Target { get; init; }
    public string? MergeKey { get; init; }
    public int? Level { get; init; }
    public double? Magnitude { get; init; }
    public string? Unit { get; init; }
    public string? State { get; init; }
    public IReadOnlyList<MovementConsequenceComponent> MovementComponents { get; init; } = [];
    public IReadOnlyList<Guid> SourceConsequenceIds { get; init; } = [];
    public IReadOnlyList<ExpeditionConsequenceProvenance> Provenance { get; init; } = [];
    public string? RecoveryModel { get; init; }

    public void Validate(CrawlPartySheet party)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Expedition effect id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(EffectKey, 200, "Expedition effect key");
        ExpeditionConsequenceProvenance.ValidateOptional(MergeKey, 500, "Expedition effect merge key");
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "Expedition effect unit");
        ExpeditionConsequenceProvenance.ValidateOptional(State, 500, "Expedition effect state");
        ExpeditionConsequenceProvenance.ValidateOptional(RecoveryModel, 200, "Expedition effect recovery model");
        if (Level is < 0)
        {
            throw new InvalidOperationException("Expedition effect level can not be negative.");
        }
        if (Magnitude.HasValue && !double.IsFinite(Magnitude.Value))
        {
            throw new InvalidOperationException("Expedition effect magnitude must be finite.");
        }
        Target.ValidateAgainst(party);
        if (MovementComponents.Select(value => value.Id).Distinct().Count() != MovementComponents.Count)
        {
            throw new InvalidOperationException("Expedition effect movement component ids must be unique.");
        }
        foreach (var movement in MovementComponents)
        {
            movement.Validate();
        }
        if (SourceConsequenceIds.Any(value => value == Guid.Empty)
            || SourceConsequenceIds.Distinct().Count() != SourceConsequenceIds.Count)
        {
            throw new InvalidOperationException("Expedition effect source consequence ids must be non-empty and unique.");
        }
        foreach (var provenance in Provenance)
        {
            provenance.Validate();
        }
    }
}

public sealed record AppliedConsequenceRecord(
    Guid ConsequenceId,
    string ConsequenceKey,
    ExpeditionConsequenceStatus Status,
    IReadOnlyList<Guid> EffectIds,
    ExpeditionConsequenceProvenance Provenance,
    string? Detail = null)
{
    public ExpeditionConsequenceProvenance? ResolutionProvenance { get; init; }

    public void Validate()
    {
        if (ConsequenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Applied consequence id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(ConsequenceKey, 200, "Applied consequence key");
        Provenance.Validate();
        ResolutionProvenance?.Validate();
        if (EffectIds.Any(value => value == Guid.Empty) || EffectIds.Distinct().Count() != EffectIds.Count)
        {
            throw new InvalidOperationException("Applied consequence effect ids must be non-empty and unique.");
        }
        ExpeditionConsequenceProvenance.ValidateOptional(Detail, 2000, "Applied consequence detail");
    }
}

public sealed record PendingExpeditionConsequence(
    ExpeditionConsequence Consequence,
    ExpeditionConsequenceStatus Status,
    string Reason,
    string RequiredAction)
{
    public IReadOnlyList<ExpeditionConsequenceComponent> UnresolvedComponents { get; init; } = Consequence.Components;

    public void Validate(CrawlPartySheet party)
    {
        Consequence.ValidateAgainst(party);
        if (Status is not (ExpeditionConsequenceStatus.Deferred
            or ExpeditionConsequenceStatus.InputRequired
            or ExpeditionConsequenceStatus.RequiresAdjudication
            or ExpeditionConsequenceStatus.Unsupported
            or ExpeditionConsequenceStatus.ExternalActionRequired))
        {
            throw new InvalidOperationException("Pending consequence status must describe unresolved work.");
        }
        if (UnresolvedComponents.Count == 0)
        {
            throw new InvalidOperationException("A pending consequence requires at least one unresolved structured component.");
        }
        foreach (var component in UnresolvedComponents)
        {
            component.Validate();
        }
        ExpeditionConsequenceProvenance.RequireText(Reason, 2000, "Pending consequence reason");
        ExpeditionConsequenceProvenance.RequireText(RequiredAction, 1000, "Pending consequence required action");
    }
}

public sealed record ExpeditionEffectAuditRecord(
    Guid Id,
    Guid EffectId,
    string EffectKey,
    string Operation,
    int? BeforeLevel,
    int? AfterLevel,
    double? BeforeMagnitude,
    double? AfterMagnitude,
    Guid? ConsequenceId,
    ExpeditionConsequenceProvenance Provenance)
{
    public void Validate()
    {
        if (Id == Guid.Empty || EffectId == Guid.Empty)
        {
            throw new InvalidOperationException("Effect audit ids are required.");
        }
        ExpeditionConsequenceProvenance.RequireText(EffectKey, 200, "Effect audit key");
        ExpeditionConsequenceProvenance.RequireText(Operation, 200, "Effect audit operation");
        if (BeforeMagnitude.HasValue && !double.IsFinite(BeforeMagnitude.Value)
            || AfterMagnitude.HasValue && !double.IsFinite(AfterMagnitude.Value))
        {
            throw new InvalidOperationException("Effect audit magnitudes must be finite.");
        }
        if (ConsequenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Effect audit consequence id can not be empty.");
        }
        Provenance.Validate();
    }
}

public sealed record ExpeditionEffectState
{
    public IReadOnlyList<ExpeditionEffect> ActiveEffects { get; init; } = [];
    public IReadOnlyList<AppliedConsequenceRecord> AppliedConsequences { get; init; } = [];
    public IReadOnlyList<PendingExpeditionConsequence> PendingConsequences { get; init; } = [];
    public IReadOnlyList<ExpeditionEffectAuditRecord> History { get; init; } = [];

    public static ExpeditionEffectState Empty { get; } = new();

    public void Validate(CrawlPartySheet party)
    {
        var effectIds = new HashSet<Guid>();
        var mergeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in ActiveEffects)
        {
            effect.Validate(party);
            if (!effectIds.Add(effect.Id))
            {
                throw new InvalidOperationException("Active expedition effect ids must be unique.");
            }
            if (effect.MergeKey is { } mergeKey && !mergeKeys.Add(mergeKey))
            {
                throw new InvalidOperationException("Active expedition effect merge keys must be unique.");
            }
        }

        var consequenceIds = new HashSet<Guid>();
        foreach (var applied in AppliedConsequences)
        {
            applied.Validate();
            if (!consequenceIds.Add(applied.ConsequenceId))
            {
                throw new InvalidOperationException("Applied consequence ids must be unique.");
            }
        }

        var pendingIds = new HashSet<Guid>();
        foreach (var pending in PendingConsequences)
        {
            pending.Validate(party);
            if (!pendingIds.Add(pending.Consequence.Id))
            {
                throw new InvalidOperationException("Pending consequence ids must be unique.");
            }
            if (!consequenceIds.Contains(pending.Consequence.Id))
            {
                throw new InvalidOperationException("A pending consequence requires a matching consumed consequence record.");
            }
        }

        var auditIds = new HashSet<Guid>();
        foreach (var audit in History)
        {
            audit.Validate();
            if (!auditIds.Add(audit.Id))
            {
                throw new InvalidOperationException("Effect audit ids must be unique.");
            }
        }
    }
}