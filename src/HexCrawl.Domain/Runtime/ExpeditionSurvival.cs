namespace HexCrawl.Domain.Runtime;

public enum ExpeditionResourceInventoryModel
{
    Counted,
    Abstract,
    SupplyDie,
    ExternalManual
}

public sealed record ExpeditionResource
{
    public required Guid Id { get; init; }
    public required string ResourceKey { get; init; }
    public required ExpeditionEffectTarget Target { get; init; }
    public required ExpeditionResourceInventoryModel InventoryModel { get; init; }
    public double? Quantity { get; init; }
    public string? Unit { get; init; }
    public string? SymbolicState { get; init; }
    public int? SupplyDieSides { get; init; }
    public IReadOnlyList<ExpeditionConsequenceProvenance> Provenance { get; init; } = [];
    public string? Note { get; init; }

    public bool IsDepleted => InventoryModel switch
    {
        ExpeditionResourceInventoryModel.Counted => Quantity <= 0,
        ExpeditionResourceInventoryModel.SupplyDie => !SupplyDieSides.HasValue,
        _ => false
    };

    public void Validate(CrawlPartySheet party)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Expedition resource id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(ResourceKey, 200, "Expedition resource key");
        Target.ValidateAgainst(party);
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "Expedition resource unit");
        ExpeditionConsequenceProvenance.ValidateOptional(SymbolicState, 500, "Expedition resource state");
        ExpeditionConsequenceProvenance.ValidateOptional(Note, 1000, "Expedition resource note");
        if (Quantity.HasValue && (!double.IsFinite(Quantity.Value) || Quantity.Value < 0))
        {
            throw new InvalidOperationException("Expedition resource quantity must be finite and non-negative.");
        }
        if (SupplyDieSides is < 2)
        {
            throw new InvalidOperationException("Supply-die sides must be at least two when a die is present.");
        }

        switch (InventoryModel)
        {
            case ExpeditionResourceInventoryModel.Counted:
                if (!Quantity.HasValue || string.IsNullOrWhiteSpace(Unit)
                    || SymbolicState is not null || SupplyDieSides.HasValue)
                {
                    throw new InvalidOperationException("Counted resources require quantity and unit only.");
                }
                break;
            case ExpeditionResourceInventoryModel.Abstract:
                if (string.IsNullOrWhiteSpace(SymbolicState)
                    || Quantity.HasValue || Unit is not null || SupplyDieSides.HasValue)
                {
                    throw new InvalidOperationException("Abstract resources require an explicit symbolic state only.");
                }
                break;
            case ExpeditionResourceInventoryModel.SupplyDie:
                if (Quantity.HasValue || Unit is not null || SymbolicState is not null)
                {
                    throw new InvalidOperationException("Supply-die resources can not also contain quantity or symbolic state.");
                }
                break;
            case ExpeditionResourceInventoryModel.ExternalManual:
                if (Quantity.HasValue || Unit is not null || SymbolicState is not null || SupplyDieSides.HasValue)
                {
                    throw new InvalidOperationException("External/manual resources can not contain authoritative Hex Crawl inventory state.");
                }
                break;
            default:
                throw new InvalidOperationException("Expedition resource inventory model is not supported.");
        }

        foreach (var provenance in Provenance)
        {
            provenance.Validate();
        }
    }
}

public sealed record ExpeditionResourceAuditRecord(
    Guid Id,
    Guid ResourceId,
    string ResourceKey,
    string Operation,
    double? BeforeQuantity,
    double? AfterQuantity,
    string? BeforeState,
    string? AfterState,
    int? BeforeSupplyDieSides,
    int? AfterSupplyDieSides,
    Guid? ConsequenceId,
    ExpeditionConsequenceProvenance Provenance)
{
    public void Validate()
    {
        if (Id == Guid.Empty || ResourceId == Guid.Empty)
        {
            throw new InvalidOperationException("Resource audit ids are required.");
        }
        ExpeditionConsequenceProvenance.RequireText(ResourceKey, 200, "Resource audit key");
        ExpeditionConsequenceProvenance.RequireText(Operation, 200, "Resource audit operation");
        if (BeforeQuantity.HasValue && !double.IsFinite(BeforeQuantity.Value)
            || AfterQuantity.HasValue && !double.IsFinite(AfterQuantity.Value))
        {
            throw new InvalidOperationException("Resource audit quantities must be finite.");
        }
        if (ConsequenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Resource audit consequence id can not be empty.");
        }
        Provenance.Validate();
    }
}

public sealed record ExpeditionResourceState
{
    public IReadOnlyList<ExpeditionResource> Resources { get; init; } = [];
    public IReadOnlyList<ExpeditionResourceAuditRecord> History { get; init; } = [];

    public static ExpeditionResourceState Empty { get; } = new();

    public void Validate(CrawlPartySheet party)
    {
        var ids = new HashSet<Guid>();
        foreach (var resource in Resources)
        {
            resource.Validate(party);
            if (!ids.Add(resource.Id))
            {
                throw new InvalidOperationException("Expedition resource ids must be unique.");
            }
        }

        var historyIds = new HashSet<Guid>();
        foreach (var record in History)
        {
            record.Validate();
            if (!historyIds.Add(record.Id))
            {
                throw new InvalidOperationException("Expedition resource audit ids must be unique.");
            }
        }
    }
}

public sealed record PendingForcedTravelCheck(
    Guid CheckId,
    Guid ConsequenceId,
    double AmountAtDue,
    string Unit,
    ExpeditionConsequenceProvenance Provenance)
{
    public void Validate()
    {
        if (CheckId == Guid.Empty || ConsequenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Forced-travel pending check ids are required.");
        }
        if (!double.IsFinite(AmountAtDue) || AmountAtDue < 0)
        {
            throw new InvalidOperationException("Forced-travel pending amount must be finite and non-negative.");
        }
        ExpeditionConsequenceProvenance.RequireText(Unit, 100, "Forced-travel unit");
        Provenance.Validate();
    }
}

public sealed record ForcedTravelResolutionRecord(
    Guid CheckId,
    bool Success,
    double AmountAtResolution,
    string Unit,
    Guid? ConsequenceId,
    ExpeditionConsequenceProvenance Provenance)
{
    public void Validate()
    {
        if (CheckId == Guid.Empty)
        {
            throw new InvalidOperationException("Forced-travel resolution check id is required.");
        }
        if (!double.IsFinite(AmountAtResolution) || AmountAtResolution < 0)
        {
            throw new InvalidOperationException("Forced-travel resolved amount must be finite and non-negative.");
        }
        if (ConsequenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Forced-travel consequence id can not be empty.");
        }
        ExpeditionConsequenceProvenance.RequireText(Unit, 100, "Forced-travel unit");
        Provenance.Validate();
    }
}

public sealed record ForcedTravelState
{
    public double AmountSinceReset { get; init; }
    public string? Unit { get; init; }
    public double LastResolvedAmount { get; init; }
    public PendingForcedTravelCheck? PendingCheck { get; init; }
    public ForcedTravelResolutionRecord? LastResolution { get; init; }
    public IReadOnlyList<string> AccountedTravelOccurrences { get; init; } = [];

    public static ForcedTravelState Empty { get; } = new();

    public void Validate()
    {
        if (!double.IsFinite(AmountSinceReset) || AmountSinceReset < 0
            || !double.IsFinite(LastResolvedAmount) || LastResolvedAmount < 0
            || LastResolvedAmount > AmountSinceReset)
        {
            throw new InvalidOperationException("Forced-travel progress must be finite, non-negative, and internally ordered.");
        }
        if ((AmountSinceReset > 0 || LastResolvedAmount > 0 || PendingCheck is not null || LastResolution is not null)
            && string.IsNullOrWhiteSpace(Unit))
        {
            throw new InvalidOperationException("Forced-travel progress requires an explicit unit.");
        }
        if (Unit is not null)
        {
            ExpeditionConsequenceProvenance.RequireText(Unit, 100, "Forced-travel unit");
        }
        if (AccountedTravelOccurrences.Any(string.IsNullOrWhiteSpace)
            || AccountedTravelOccurrences.Distinct(StringComparer.Ordinal).Count() != AccountedTravelOccurrences.Count)
        {
            throw new InvalidOperationException("Forced-travel occurrence identities must be nonblank and unique.");
        }
        PendingCheck?.Validate();
        LastResolution?.Validate();
        if (PendingCheck is not null && !string.Equals(PendingCheck.Unit, Unit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Forced-travel pending check unit must match current progress unit.");
        }
    }
}

public sealed record ExpeditionExposureProgress
{
    public required Guid Id { get; init; }
    public required string ExposureKey { get; init; }
    public required ExpeditionEffectTarget Target { get; init; }
    public required double Amount { get; init; }
    public required string Unit { get; init; }
    public IReadOnlyList<Guid> SourceOccurrenceIds { get; init; } = [];
    public IReadOnlyList<ExpeditionConsequenceProvenance> Provenance { get; init; } = [];

    public void Validate(CrawlPartySheet party)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Exposure progress id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(ExposureKey, 200, "Exposure key");
        ExpeditionConsequenceProvenance.RequireText(Unit, 100, "Exposure unit");
        if (!double.IsFinite(Amount) || Amount < 0)
        {
            throw new InvalidOperationException("Exposure progress must be finite and non-negative.");
        }
        Target.ValidateAgainst(party);
        if (SourceOccurrenceIds.Any(value => value == Guid.Empty)
            || SourceOccurrenceIds.Distinct().Count() != SourceOccurrenceIds.Count)
        {
            throw new InvalidOperationException("Exposure source occurrence ids must be non-empty and unique.");
        }
        foreach (var provenance in Provenance)
        {
            provenance.Validate();
        }
    }
}

public sealed record ExpeditionCampState
{
    public required Guid ResolutionId { get; init; }
    public required bool Established { get; init; }
    public required IReadOnlyList<Guid> ActivityAssignmentIds { get; init; }
    public string? ResolutionModel { get; init; }
    public string? RestTriggerKey { get; init; }
    public bool? RestSafe { get; init; }
    public bool? RestProlonged { get; init; }
    public required ExpeditionConsequenceProvenance Provenance { get; init; }

    public void Validate(CrawlPartySheet party)
    {
        if (ResolutionId == Guid.Empty)
        {
            throw new InvalidOperationException("Camp resolution id is required.");
        }
        if (ActivityAssignmentIds.Any(value => value == Guid.Empty)
            || ActivityAssignmentIds.Distinct().Count() != ActivityAssignmentIds.Count)
        {
            throw new InvalidOperationException("Camp activity assignment ids must be non-empty and unique.");
        }
        foreach (var id in ActivityAssignmentIds)
        {
            if (!party.ActivityAssignments.Any(value => value.Id == id))
            {
                throw new InvalidOperationException("Camp state references an activity assignment that does not exist.");
            }
        }
        ExpeditionConsequenceProvenance.ValidateOptional(ResolutionModel, 200, "Camp resolution model");
        ExpeditionConsequenceProvenance.ValidateOptional(RestTriggerKey, 200, "Camp rest trigger");
        Provenance.Validate();
    }
}

public sealed record ExpeditionSurvivalState
{
    public ForcedTravelState ForcedTravel { get; init; } = ForcedTravelState.Empty;
    public IReadOnlyList<ExpeditionExposureProgress> Exposure { get; init; } = [];
    public ExpeditionCampState? Camp { get; init; }

    public static ExpeditionSurvivalState Empty { get; } = new();

    public void Validate(CrawlPartySheet party)
    {
        ForcedTravel.Validate();
        var exposureIds = new HashSet<Guid>();
        foreach (var progress in Exposure)
        {
            progress.Validate(party);
            if (!exposureIds.Add(progress.Id))
            {
                throw new InvalidOperationException("Exposure progress ids must be unique.");
            }
        }
        Camp?.Validate(party);
    }
}
