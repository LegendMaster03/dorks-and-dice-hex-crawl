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
    public Guid? GeneratedProcedureResolutionId { get; init; }
}

public sealed class ExpeditionWorkbenchService(IHexCrawlStore store, HexCrawlService coreService, CrawlSessionContextResolver contextResolver)
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
            new WorldBoundCrawlSessionContext(world.World.Id),
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
        var state = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("The full crawl workbench requires a spatial crawl session.");
        var resolvedContext = await contextResolver.ResolveAsync(expedition, ownerUserId, cancellationToken);
        var runtimeContext = resolvedContext.RuntimeContext
            ?? throw new InvalidOperationException("The full crawl workbench requires a spatial crawl session.");
        var world = resolvedContext.World;
        var profile = expedition.Procedure;
        profile.Validate();

        MapPresentationPolicy? presentation = null;
        var knowledge = expedition.Knowledge;
        if (world is not null)
        {
            if (knowledge is null)
            {
                throw new InvalidOperationException("A world-bound crawl session requires player-knowledge state.");
            }
            presentation = knowledge.PresentationPolicy ?? MapPresentationPolicy.DmControlled();
            presentation.Validate();
            if (knowledge.PresentationPolicy is null)
            {
                knowledge = knowledge with { PresentationPolicy = presentation };
            }
        }

        var encounterDue = ExpeditionProcedureRequirements.IsEncounterCheckDue(profile, state);
        var runtimeProfile = profile.EncounterCadence == EncounterCheckCadence.PerDay
            && state.ActiveWatch is null
            && !encounterDue
                ? profile with { EncounterCadence = EncounterCheckCadence.None }
                : profile;

        var automaticResolution = ValidateAutomaticResolution(expedition, state, runtimeProfile, command);
        var travelProvenance = Provenance(
            command.TravelResolutionSource,
            command.ResolutionSource,
            command.TravelResolutionNote,
            command.DmOverrideNote,
            automaticResolution?.Travel?.Provenance);
        var navigationProvenance = Provenance(
            command.NavigationResolutionSource,
            command.ResolutionSource,
            command.NavigationResolutionNote,
            command.DmOverrideNote,
            automaticResolution?.Navigation?.Provenance);
        var encounterProvenance = Provenance(
            command.EncounterResolutionSource,
            command.ResolutionSource,
            command.EncounterResolutionNote,
            command.DmOverrideNote,
            automaticResolution?.Encounter?.Provenance);
        var boundaryProvenance = Provenance(
            command.BoundaryResolutionSource,
            command.ResolutionSource,
            command.BoundaryResolutionNote,
            command.DmOverrideNote);

        var travel = BuildTravel(profile, runtimeContext.HexCenterDistance.Unit, command, travelProvenance);
        var navigation = BuildNavigation(profile, state, command, navigationProvenance);
        var encounter = BuildEncounter(runtimeProfile, state, command, encounterProvenance);
        if (world is null && encounter?.Kind == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            throw new InvalidOperationException("Keyed-location discovery requires a world-bound crawl session.");
        }
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
            runtimeContext,
            runtimeProfile,
            state,
            plan,
            new WatchAdvanceInputs(travel, navigation, encounter, boundaryDecision, command.DmOverrideNote));

        var projectedState = result.Expedition;
        var projectedKnowledge = knowledge;
        if (world is not null)
        {
            var worldProjection = ExpeditionWorldComposition.Apply(
                world.World,
                result,
                knowledge!,
                presentation!.AutomationMode != PresentationAutomationMode.DmControlled);
            projectedState = worldProjection.State;
            projectedKnowledge = PresentationKnowledgeProjection.ApplyEnteredHexes(
                presentation,
                worldProjection.Knowledge,
                result.Events
                    .Where(runtimeEvent => runtimeEvent.Kind == CrawlRuntimeEventKind.HexEntered && runtimeEvent.Hex.HasValue)
                    .Select(runtimeEvent => runtimeEvent.Hex!.Value));
        }

        var stateWithProvenance = AppendProvenanceEvent(
            projectedState,
            result.Events,
            travelProvenance,
            navigation?.Provenance,
            encounter?.Provenance,
            boundaryDecision?.Provenance);

        var finalized = FinalizeGeneratedResolutionUse(
            expedition,
            stateWithProvenance,
            automaticResolution,
            command.ExpectedVersion);
        var updated = expedition with
        {
            Runtime = finalized.State,
            Knowledge = projectedKnowledge,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime,
            GeneratedProcedureResolutions = finalized.Resolutions
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
        string? dmOverrideNote,
        ResolutionProvenance? verifiedAutomatic = null)
    {
        var source = specific ?? fallback;
        if (source == ResolutionSource.AutomaticRoll)
        {
            return verifiedAutomatic
                ?? throw new InvalidOperationException("AutomaticRoll provenance requires a verified persisted helper generation.");
        }
        return new ResolutionProvenance(
            source,
            string.IsNullOrWhiteSpace(note) ? dmOverrideNote : note.Trim());
    }

    private static GeneratedProcedureResolution? ValidateAutomaticResolution(
        StoredExpedition expedition,
        ExpeditionState state,
        CrawlProcedureProfile runtimeProfile,
        AdvanceExpeditionWorkbenchCommand command)
    {
        if (command.ResolutionSource == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException(
                "AutomaticRoll can not be supplied as a blanket resolution source. Use a server-generated resolution id with the specific generated component.");
        }
        if (command.BoundaryResolutionSource == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException("The procedure helper does not generate lost-boundary decisions.");
        }

        var travelAutomatic = command.TravelResolutionSource == ResolutionSource.AutomaticRoll;
        var navigationAutomatic = command.NavigationResolutionSource == ResolutionSource.AutomaticRoll;
        var encounterAutomatic = command.EncounterResolutionSource == ResolutionSource.AutomaticRoll;
        var anyAutomatic = travelAutomatic || navigationAutomatic || encounterAutomatic;

        if (!anyAutomatic)
        {
            if (command.GeneratedProcedureResolutionId.HasValue)
            {
                throw new InvalidOperationException(
                    "A generated procedure-resolution id may only be supplied when applying an AutomaticRoll component.");
            }
            return null;
        }

        var id = command.GeneratedProcedureResolutionId
            ?? throw new InvalidOperationException(
                "AutomaticRoll requires the server-generated procedure-resolution id returned by the helper.");

        var generated = (expedition.GeneratedProcedureResolutions ?? [])
            .SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException(
                "The supplied generated procedure resolution does not belong to this crawl session.");

        if (generated.SessionId != expedition.Id)
        {
            throw new InvalidOperationException(
                "The supplied generated procedure resolution does not belong to this crawl session.");
        }
        if (generated.Status != GeneratedProcedureResolutionStatus.Available)
        {
            throw new InvalidOperationException(
                $"Generated procedure resolution {generated.Id:D} is {generated.Status} and can not be applied.");
        }
        if (generated.GeneratedAtVersion != expedition.Version
            || generated.GeneratedAtVersion != command.ExpectedVersion)
        {
            throw new InvalidOperationException(
                "The generated procedure resolution is not valid for the current crawl-session version.");
        }

        var currentWatch = ProcedureResolutionHelperService.CurrentWatchNumber(state);
        if (generated.WatchNumber != currentWatch)
        {
            throw new InvalidOperationException(
                "The generated procedure resolution belongs to a different watch context.");
        }

        if (travelAutomatic)
        {
            var expected = generated.Travel
                ?? throw new InvalidOperationException("The selected helper generation did not produce a travel result.");
            RequireEqual(command.ExpectedDistance, expected.ExpectedDistance, "expected travel distance");
            RequireEqual(command.ActualDistance, expected.ActualDistance, "actual travel distance");
        }

        if (navigationAutomatic)
        {
            if (state.ActiveWatch is not null
                || !runtimeProfile.UsesNavigationChecks
                || command.SuppressesNavigationCheck
                || command.DeliberateDoubleBack)
            {
                throw new InvalidOperationException("Automatic navigation resolution is not applicable to this watch.");
            }

            var expected = generated.Navigation
                ?? throw new InvalidOperationException("The selected helper generation did not produce a navigation result.");
            if (command.NavigationOutcome != expected.Outcome
                || command.VeerSteps != expected.VeerSteps)
            {
                throw new InvalidOperationException(
                    "The submitted navigation values do not match the persisted generated result.");
            }
        }

        if (encounterAutomatic)
        {
            if (state.ActiveWatch is not null || runtimeProfile.EncounterCadence == EncounterCheckCadence.None)
            {
                throw new InvalidOperationException("Automatic encounter resolution is not applicable to this watch.");
            }

            var expected = generated.Encounter
                ?? throw new InvalidOperationException("The selected helper generation did not produce an encounter result.");
            if (command.EncounterOutcome != expected.Kind)
            {
                throw new InvalidOperationException(
                    "The submitted encounter outcome does not match the persisted generated result.");
            }
            RequireEqual(command.EncounterHour, expected.OccursAtHours, "encounter time");

            if (expected.LocationId.HasValue && command.LocationId != expected.LocationId)
            {
                throw new InvalidOperationException(
                    "The submitted keyed location does not match the persisted generated result.");
            }
            if (expected.Kind != EncounterOutcomeKind.KeyedLocationDiscovery
                && command.LocationId.HasValue != expected.LocationId.HasValue)
            {
                throw new InvalidOperationException(
                    "The submitted encounter location does not match the persisted generated result.");
            }
        }

        return generated;
    }

    private static void RequireEqual(double? actual, double? expected, string label)
    {
        if (actual.HasValue != expected.HasValue
            || (actual.HasValue && actual.Value != expected!.Value))
        {
            throw new InvalidOperationException(
                $"The submitted {label} does not match the persisted generated result.");
        }
    }

    private static (ExpeditionState State, IReadOnlyList<GeneratedProcedureResolution> Resolutions)
        FinalizeGeneratedResolutionUse(
            StoredExpedition expedition,
            ExpeditionState state,
            GeneratedProcedureResolution? consumed,
            long expectedVersion)
    {
        var resolutions = expedition.GeneratedProcedureResolutions ?? [];
        if (resolutions.Count == 0)
        {
            return (state, resolutions);
        }

        long? consumedSequence = null;
        if (consumed is not null)
        {
            consumedSequence = state.History.Count == 0 ? 1 : state.History[^1].Sequence + 1;
            var audit = new CrawlRuntimeEvent(
                consumedSequence.Value,
                consumed.WatchNumber,
                CrawlRuntimeEventKind.ProcedureResolutionHelperConsumed,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Generated procedure resolution {consumed.Id:D} consumed for watch {consumed.WatchNumber}.");
            state = state with { History = [.. state.History, audit] };
        }

        var nextVersion = checked(expectedVersion + 1);
        var updated = resolutions.Select(item =>
        {
            if (item.Status != GeneratedProcedureResolutionStatus.Available)
            {
                return item;
            }
            if (consumed is not null && item.Id == consumed.Id)
            {
                return item with
                {
                    Status = GeneratedProcedureResolutionStatus.Consumed,
                    ConsumedAtVersion = nextVersion,
                    ConsumedAuditSequence = consumedSequence
                };
            }
            return item with { Status = GeneratedProcedureResolutionStatus.Superseded };
        }).ToArray();

        return (state, updated);
    }

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
