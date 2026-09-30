using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record ProcedureComposerDraft(
    CampaignProcedure Procedure,
    ProcedureOriginMetadata? Origin,
    string? Attribution,
    string? Disclaimer,
    ProcedureDependencyReport Dependencies);

public sealed class ProcedureComposerService(IHexCrawlStore store)
{
    public async Task<ProcedureComposerDraft> CreateDraftAsync(
        string ownerUserId,
        string? presetKey,
        Guid? procedureId,
        int? revision,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(overrides);
        var source = await ResolveSourceAsync(owner, presetKey, procedureId, revision, cancellationToken);
        var draft = CampaignProcedureMaterializer.CreateDraft(source.Procedure, overrides);
        return new ProcedureComposerDraft(
            draft,
            source.Origin,
            source.Attribution,
            source.Disclaimer,
            draft.EvaluateDependencies());
    }

    public async Task<StoredCampaignProcedureRevision> CreateAsync(
        string ownerUserId,
        string? presetKey,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(overrides);
        var source = ResolveCreationSource(presetKey);
        var procedure = CampaignProcedureMaterializer.CreateInitialRevision(source.Procedure, overrides);
        return await store.CreateCampaignProcedureRevisionAsync(
            new StoredCampaignProcedureRevision(
                procedure,
                owner,
                campaignId,
                source.Origin,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> CreateRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }
        if (expectedRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }
        ArgumentNullException.ThrowIfNull(overrides);
        if (overrides.Count == 0)
        {
            throw new InvalidOperationException("Saving a new procedure revision requires at least one explicit change.");
        }

        var current = await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure was not found.");
        if (current.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure revision {expectedRevision} is stale; the current revision is {current.Revision}.");
        }

        var draft = CampaignProcedureMaterializer.CreateDraft(current.Procedure, overrides);
        if (draft.Modules.SequenceEqual(current.Procedure.Modules))
        {
            throw new InvalidOperationException("The submitted procedure changes do not alter the current materialized procedure.");
        }

        var next = CampaignProcedureMaterializer.CreateRevision(current.Procedure, overrides);
        return await store.CreateCampaignProcedureRevisionAsync(
            new StoredCampaignProcedureRevision(
                next,
                owner,
                current.CampaignId,
                current.ProcedureOrigin,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> GetAsync(
        string ownerUserId,
        Guid procedureId,
        int? revision = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }

        var stored = revision.HasValue
            ? await store.GetCampaignProcedureRevisionAsync(procedureId, revision.Value, owner, cancellationToken)
            : await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken);
        return stored ?? throw new HexCrawlNotFoundException("Campaign procedure revision was not found.");
    }

    public async Task<IReadOnlyList<StoredCampaignProcedureRevision>> ListRevisionsAsync(
        string ownerUserId,
        Guid procedureId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }

        return await store.ListCampaignProcedureRevisionsAsync(procedureId, owner, cancellationToken);
    }

    private async Task<Source> ResolveSourceAsync(
        string ownerUserId,
        string? presetKey,
        Guid? procedureId,
        int? revision,
        CancellationToken cancellationToken)
    {
        ValidateSource(presetKey, procedureId);

        if (procedureId.HasValue)
        {
            if (revision.HasValue && revision <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision));
            }

            var stored = revision.HasValue
                ? await store.GetCampaignProcedureRevisionAsync(
                    procedureId.Value,
                    revision.Value,
                    ownerUserId,
                    cancellationToken)
                : await store.GetLatestCampaignProcedureRevisionAsync(
                    procedureId.Value,
                    ownerUserId,
                    cancellationToken);
            if (stored is null)
            {
                throw new HexCrawlNotFoundException("Campaign procedure revision was not found.");
            }

            var catalogMetadata = FindOriginCatalogMetadata(stored.ProcedureOrigin);
            return new Source(
                stored.Procedure,
                stored.ProcedureOrigin,
                catalogMetadata?.Attribution,
                catalogMetadata?.Disclaimer);
        }

        return ResolveCreationSource(presetKey);
    }

    private static Source ResolveCreationSource(string? presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
        {
            return new Source(CreateCustomProcedure(), null, null, null);
        }

        var preset = CrawlProcedureCatalog.Resolve(presetKey);
        var materialized = preset.MaterializeGeneric();
        return new Source(
            materialized.Procedure,
            materialized.Origin,
            preset.Attribution,
            preset.Disclaimer);
    }

    private static CrawlProcedurePresetDefinition? FindOriginCatalogMetadata(ProcedureOriginMetadata? origin)
    {
        if (string.IsNullOrWhiteSpace(origin?.PresetKey))
        {
            return null;
        }

        return CrawlProcedureCatalog.Catalog.FirstOrDefault(value =>
            string.Equals(value.PresetKey, origin.PresetKey, StringComparison.OrdinalIgnoreCase)
            && (!origin.PresetRevision.HasValue || value.PresetRevision == origin.PresetRevision));
    }

    private static CampaignProcedure CreateCustomProcedure()
    {
        var modules = new[]
        {
            Select(GenericProcedureCatalog.TimeIntervalModule, GenericProcedureCatalog.FixedIntervalDurationMechanic,
                ("durationTicks", TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture))),
            Select(GenericProcedureCatalog.MovementResolutionModule, GenericProcedureCatalog.MovementResolutionPolicyMechanic,
                ("travelResolution", TravelResolutionMode.ContinuousDistance.ToString()),
                ("actualDistanceResolution", ActualDistanceResolutionMode.Fixed.ToString()),
                ("tracksIntraHexProgress", "true")),
            Select(GenericProcedureCatalog.HexProgressModule, GenericProcedureCatalog.HexProgressPolicyMechanic,
                ("startingExitProgressFactor", "0.5"),
                ("nearExitProgressFactor", "0.5"),
                ("farExitProgressFactor", "1"),
                ("backExitProgressFactor", "0.5"),
                ("directionChangesCostProgress", "false"),
                ("directionChangeProgressCostFactor", "0"),
                ("supportsDeliberateDoubleBack", "false")),
            Select(GenericProcedureCatalog.NavigationModule, GenericProcedureCatalog.NavigationCheckPolicyMechanic,
                ("usesNavigationChecks", "false"),
                ("usesPersistentVeer", "false")),
            Select(GenericProcedureCatalog.EncounterCadenceModule, GenericProcedureCatalog.EncounterCheckCadenceMechanic,
                ("cadence", EncounterCheckCadence.None.ToString())),
            Select(GenericProcedureCatalog.ResolutionHelpersModule, GenericProcedureCatalog.DeterministicResolutionHelpersMechanic,
                ("travel.enabled", "false"),
                ("navigation.enabled", "false"),
                ("encounter.enabled", "false")),
            Select(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                ("budgetModel", "fixed-per-interval"),
                ("baseBudget", "1"),
                ("budgetUnit", "interval"),
                ("limitingScope", "party")),
            Select(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                ("adjustmentModel", "multiplier"),
                ("terrainAdjustments", "default=1"),
                ("routeAdjustmentModel", "none"),
                ("weatherAdjustmentModel", "manual")),
            Select(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                ("assignmentScope", "participant"),
                ("activityBudgetModel", "per-interval"),
                ("activityKeys", "travel;navigate;forage;search;watch"),
                ("roleKeys", "navigator;lookout")),
            Select(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                ("checkTriggerModel", "manual-or-procedure"),
                ("failureStateModel", "lost-state"),
                ("directionalErrorModel", "manual-off-course"),
                ("recognitionModel", "manual"),
                ("reorientationModel", "manual")),
            Select(GenericProcedureCatalog.EncounterScheduleModule, GenericProcedureCatalog.EncounterSchedulePolicyMechanic,
                ("scheduleModel", "cadence-backed"),
                ("travelChecksPerInterval", "0"),
                ("campCheck", "false"),
                ("terrainProbabilityModel", "none")),
            Select(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                ("resourceKinds", "food;water"),
                ("inventoryModel", "counted"),
                ("consumptionModel", "manual"),
                ("consumptionInterval", "interval")),
            Select(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ActivityForagingPolicyMechanic,
                ("resolutionModel", "manual-check"),
                ("timeCost", "1"),
                ("timeUnit", "activity"),
                ("movementTradeoff", "replaces-activity")),
            Select(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.ActivityCampingPolicyMechanic,
                ("resolutionModel", "manual-camp"),
                ("timeCost", "1"),
                ("timeUnit", "activity"),
                ("watchModel", "manual")),
            Select(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                ("normalTravelLimit", "2"),
                ("limitUnit", "intervals"),
                ("checkModel", "manual-check"),
                ("failureConsequence", "fatigue")),
            Select(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                ("effectKinds", "fatigue"),
                ("accumulationModel", "levels"),
                ("recoveryModel", "rest"),
                ("scope", "participant")),
            Select(GenericProcedureCatalog.JourneyEventsModule, GenericProcedureCatalog.JourneyEventPolicyMechanic,
                ("triggerModel", "manual-or-landmark"),
                ("targetingModel", "travel-role"),
                ("terrainInfluence", "manual"),
                ("consequenceModel", "event")),
            Select(GenericProcedureCatalog.JourneyProcessModule, GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
                ("stageModel", "manual-stages"),
                ("progressModel", "progress-points"),
                ("completionModel", "explicit-completion"),
                ("roleDriven", "true"))
        };

        var procedure = new CampaignProcedure
        {
            ProcedureId = Guid.NewGuid(),
            Revision = 1,
            Key = "custom-expedition-procedure",
            Name = "Custom expedition procedure",
            Modules = modules,
            Overrides = []
        };
        procedure.Validate();
        return procedure;
    }

    private static MaterializedProcedureModule Select(
        string moduleKey,
        string mechanicKey,
        params (string Key, string Value)[] parameters) =>
        new(
            CampaignProcedureSnapshot.Copy(GenericProcedureCatalog.ResolveModule(moduleKey)),
            CampaignProcedureSnapshot.Copy(GenericProcedureCatalog.ResolveMechanic(mechanicKey)),
            parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));

    private static void ValidateSource(string? presetKey, Guid? procedureId)
    {
        if (!string.IsNullOrWhiteSpace(presetKey) && procedureId.HasValue)
        {
            throw new ArgumentException("A Composer draft can start from either a preset or a saved procedure, not both.");
        }
    }

    private static string RequireOwner(string? ownerUserId) =>
        !string.IsNullOrWhiteSpace(ownerUserId)
            ? ownerUserId.Trim()
            : throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));

    private sealed record Source(
        CampaignProcedure Procedure,
        ProcedureOriginMetadata? Origin,
        string? Attribution,
        string? Disclaimer);
}
