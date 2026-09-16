using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed record StartExpeditionWorkbenchCommand(
    string Name,
    string ProcedureKey,
    string PresentationKey,
    HexCoordinate StartHex,
    CrawlProcedureProfile? ProcedureSnapshot = null);

public sealed record AdvanceExpeditionWorkbenchCommand
{
    public long ExpectedVersion { get; init; }
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
    public IReadOnlyList<string> Activities { get; init; } = [];
    public string NavigationAidKey { get; init; } = "none";
    public bool SuppressesNavigationCheck { get; init; }
    public bool ResetsVeerAtBoundary { get; init; }
    public double? EffectiveDistance { get; init; }
    public double? ExpectedDistance { get; init; }
    public double? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public ResolutionSource? TravelResolutionSource { get; init; }
    public string? TravelResolutionNote { get; init; }
    public ResolutionSource? NavigationResolutionSource { get; init; }
    public string? NavigationResolutionNote { get; init; }
    public ResolutionSource? EncounterResolutionSource { get; init; }
    public string? EncounterResolutionNote { get; init; }
    public ResolutionSource? BoundaryResolutionSource { get; init; }
    public string? BoundaryResolutionNote { get; init; }
    public NavigationCheckOutcome? NavigationOutcome { get; init; }
    public int? VeerSteps { get; init; }
    public EncounterOutcomeKind? EncounterOutcome { get; init; }
    public double? EncounterHour { get; init; }
    public Guid? LocationId { get; init; }
    public string? EncounterNote { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public bool ContinueAcrossBoundaries { get; init; }
    public bool? RecognizedLost { get; init; }
    public bool? Reorient { get; init; }
    public string? DmOverrideNote { get; init; }
}

public sealed class ExpeditionWorkbenchService(IHexCrawlStore store, HexCrawlService coreService)
{
    private readonly CrawlRuntimeEngine _runtime = new();

    public async Task<StoredExpedition> StartAsync(
        Guid overworldId,
        string ownerUserId,
        StartExpeditionWorkbenchCommand command,
        CancellationToken cancellationToken = default)
    {
        var world = await coreService.GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        var preset = CrawlProcedureCatalog.Resolve(command.ProcedureKey);
        var profile = command.ProcedureSnapshot ?? preset;
        if (!string.Equals(profile.Key, preset.Key, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A customized procedure snapshot must retain the selected preset key as provenance.", nameof(command));
        }
        profile.Validate();

        var presentation = MapPresentationPolicyCatalog.Resolve(command.PresentationKey);
        presentation.Validate();

        var expeditionId = Guid.NewGuid();
        var state = new ExpeditionState
        {
            Id = expeditionId,
            OverworldId = world.World.Id,
            Position = HexGeometry.HexToWorld(world.World.Grid, command.StartHex),
            PositionPrecision = WorldPositionPrecision.HexAnchor,
            Traversal = HexTraversalState.StartingIn(command.StartHex, world.World.Grid.NeighborCenterDistance.Unit),
            Navigation = new NavigationRuntimeState(false, 0),
            DistanceTraveled = new DistanceMeasure(0, world.World.Grid.NeighborCenterDistance.Unit)
        };
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.World.Id,
            PresentationPolicy = presentation
        };
        knowledge = PresentationKnowledgeProjection.Initialize(world.World, presentation, knowledge, command.StartHex);

        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            RequiredText(command.Name, "Expedition name"),
            state,
            knowledge,
            profile,
            null,
            TimeSpan.Zero,
            world.OwnerUserId,
            1,
            now,
            now), cancellationToken);
    }

    public async Task<StoredExpedition> AdvanceAsync(
        Guid expeditionId,
        string ownerUserId,
        AdvanceExpeditionWorkbenchCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var world = await coreService.GetOverworldAsync(expedition.State.OverworldId, ownerUserId, cancellationToken);
        var profile = expedition.Procedure;
        profile.Validate();

        var presentation = expedition.Knowledge.PresentationPolicy ?? MapPresentationPolicy.DmControlled();
        presentation.Validate();
        var knowledge = expedition.Knowledge.PresentationPolicy is null
            ? expedition.Knowledge with { PresentationPolicy = presentation }
            : expedition.Knowledge;

        var travelProvenance = Provenance(command.TravelResolutionSource, command.ResolutionSource, command.TravelResolutionNote, command.DmOverrideNote);
        var navigationProvenance = Provenance(command.NavigationResolutionSource, command.ResolutionSource, command.NavigationResolutionNote, command.DmOverrideNote);
        var encounterProvenance = Provenance(command.EncounterResolutionSource, command.ResolutionSource, command.EncounterResolutionNote, command.DmOverrideNote);
        var boundaryProvenance = Provenance(command.BoundaryResolutionSource, command.ResolutionSource, command.BoundaryResolutionNote, command.DmOverrideNote);

        var encounterDue = ExpeditionProcedureRequirements.IsEncounterCheckDue(profile, expedition.State);
        var runtimeProfile = profile.EncounterCadence == EncounterCheckCadence.PerDay
            && expedition.State.ActiveWatch is null
            && !encounterDue
                ? profile with { EncounterCadence = EncounterCheckCadence.None }
                : profile;

        var travel = BuildTravel(profile, world.World.Grid.NeighborCenterDistance.Unit, command, travelProvenance);
        var navigation = BuildNavigation(profile, expedition.State, command, navigationProvenance);
        var encounter = BuildEncounter(runtimeProfile, expedition.State, command, encounterProvenance);
        var boundaryDecision = command.RecognizedLost.HasValue || command.Reorient.HasValue
            ? new BoundaryNavigationDecision(command.RecognizedLost ?? false, command.Reorient ?? false, boundaryProvenance)
            : null;
        var plan = new WatchTravelPlan(
            new HexDirection(command.IntendedDirection),
            new TravelModeSelection(RequiredText(command.PaceKey, "Pace key"), command.Activities ?? []),
            new NavigationAidSelection(
                string.IsNullOrWhiteSpace(command.NavigationAidKey) ? "none" : command.NavigationAidKey.Trim(),
                command.SuppressesNavigationCheck,
                command.ResetsVeerAtBoundary),
            command.DeliberateDoubleBack,
            command.ContinueAcrossBoundaries);

        var result = _runtime.Advance(
            world.World,
            runtimeProfile,
            expedition.State,
            knowledge,
            plan,
            new WatchAdvanceInputs(travel, navigation, encounter, boundaryDecision, command.DmOverrideNote));

        var knowledgeAfterRuntime = presentation.AutomationMode == PresentationAutomationMode.DmControlled
            ? knowledge
            : result.Knowledge;
        var projectedKnowledge = PresentationKnowledgeProjection.ApplyEnteredHexes(
            presentation,
            knowledgeAfterRuntime,
            result.Events
                .Where(runtimeEvent => runtimeEvent.Kind == CrawlRuntimeEventKind.HexEntered)
                .Select(runtimeEvent => runtimeEvent.Hex));
        var stateWithProvenance = AppendProvenanceEvent(
            result.Expedition,
            result.Events,
            travelProvenance,
            navigation?.Provenance,
            encounter?.Provenance,
            boundaryDecision?.Provenance);

        var updated = expedition with
        {
            State = stateWithProvenance,
            Knowledge = projectedKnowledge,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime
        };
        return await SaveAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    private static ExpeditionState AppendProvenanceEvent(
        ExpeditionState state,
        IReadOnlyList<CrawlRuntimeEvent> events,
        ResolutionProvenance travel,
        ResolutionProvenance? navigation,
        ResolutionProvenance? encounter,
        ResolutionProvenance? boundary)
    {
        var parts = new List<string> { $"travel={Describe(travel)}" };
        if (navigation is not null) parts.Add($"navigation={Describe(navigation)}");
        if (encounter is not null) parts.Add($"encounter={Describe(encounter)}");
        if (boundary is not null) parts.Add($"boundary={Describe(boundary)}");

        var sequence = state.History.Count == 0 ? 1 : state.History[^1].Sequence + 1;
        var watchNumber = events.Count > 0
            ? events[^1].WatchNumber
            : state.ActiveWatch?.WatchNumber ?? Math.Max(1, state.CompletedWatches);
        var audit = new CrawlRuntimeEvent(
            sequence,
            watchNumber,
            CrawlRuntimeEventKind.ResolutionProvenanceRecorded,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Resolved input provenance: {string.Join("; ", parts)}.");
        return state with { History = [.. state.History, audit] };
    }

    private static string Describe(ResolutionProvenance provenance) =>
        string.IsNullOrWhiteSpace(provenance.Note)
            ? provenance.Source.ToString()
            : $"{provenance.Source} ({provenance.Note.Trim()})";

    private async Task<StoredExpedition> SaveAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The expedition was changed by another request. Reload it before advancing again."),
            _ => throw new HexCrawlNotFoundException("Expedition was not found.")
        };
    }

    private static ResolvedTravelAmount BuildTravel(
        CrawlProcedureProfile profile,
        DistanceUnit unit,
        AdvanceExpeditionWorkbenchCommand command,
        ResolutionProvenance provenance)
    {
        if (profile.TravelResolution == TravelResolutionMode.HexSteps)
        {
            return command.HexSteps.HasValue
                ? ResolvedTravelAmount.Steps(command.HexSteps.Value, provenance)
                : throw new InvalidOperationException("The selected procedure requires a resolved hex-step count.");
        }

        if (profile.ActualDistanceResolution == ActualDistanceResolutionMode.Fixed)
        {
            var effective = command.EffectiveDistance ?? command.ActualDistance ?? command.ExpectedDistance
                ?? throw new InvalidOperationException("The selected fixed-distance procedure requires an effective travel distance.");
            var distance = new DistanceMeasure(effective, unit);
            return ResolvedTravelAmount.Distance(distance, distance, provenance);
        }

        if (!command.ExpectedDistance.HasValue || !command.ActualDistance.HasValue)
        {
            throw new InvalidOperationException("The selected variable-distance procedure requires expected and actual travel distance.");
        }
        return ResolvedTravelAmount.Distance(
            new DistanceMeasure(command.ExpectedDistance.Value, unit),
            new DistanceMeasure(command.ActualDistance.Value, unit),
            provenance);
    }

    private static ResolvedNavigation? BuildNavigation(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        AdvanceExpeditionWorkbenchCommand command,
        ResolutionProvenance provenance)
    {
        if (state.ActiveWatch is not null
            || !profile.UsesNavigationChecks
            || command.SuppressesNavigationCheck
            || command.DeliberateDoubleBack)
        {
            return null;
        }
        var outcome = command.NavigationOutcome
            ?? throw new InvalidOperationException("This watch requires an explicit navigation outcome.");
        if (outcome == NavigationCheckOutcome.NotRequired)
        {
            throw new InvalidOperationException("Navigation outcome must be Succeeded or Failed when a check is required.");
        }
        return new ResolvedNavigation(
            outcome,
            outcome == NavigationCheckOutcome.Failed ? command.VeerSteps : null,
            provenance);
    }

    private static ResolvedEncounter? BuildEncounter(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        AdvanceExpeditionWorkbenchCommand command,
        ResolutionProvenance provenance)
    {
        if (state.ActiveWatch is not null || profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return null;
        }
        var kind = command.EncounterOutcome ?? EncounterOutcomeKind.None;
        TimeSpan? occursAt = kind == EncounterOutcomeKind.None
            ? null
            : TimeSpan.FromHours(command.EncounterHour
                ?? throw new InvalidOperationException("A triggered encounter requires an encounter hour."));
        return new ResolvedEncounter(kind, occursAt, command.LocationId, command.EncounterNote, provenance);
    }

    private static ResolutionProvenance Provenance(
        ResolutionSource? specific,
        ResolutionSource fallback,
        string? note,
        string? dmOverrideNote) =>
        new(specific ?? fallback, string.IsNullOrWhiteSpace(note) ? dmOverrideNote : note.Trim());

    private static string RequiredText(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"{label} is required.");

    private static void RequireVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new HexCrawlConcurrencyException("The resource version is stale. Reload it before saving again.");
        }
    }
}
