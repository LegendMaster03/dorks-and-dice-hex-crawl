using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
    private readonly CrawlRuntimeEngine _runtime = new();

    public Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        _store.ListExpeditionsAsync(RequireUser(ownerUserId), cancellationToken);

    public async Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var world = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        return await _store.ListExpeditionsAsync(overworldId, world.OwnerUserId, cancellationToken);
    }

    public async Task<StoredExpedition> GetExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireUser(ownerUserId);
        return await _store.GetExpeditionAsync(expeditionId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Crawl session was not found.");
    }

    public async Task<StoredExpedition> StartExpeditionAsync(
        Guid overworldId,
        string ownerUserId,
        StartExpeditionCommand command,
        CancellationToken cancellationToken = default)
    {
        var world = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        if (!world.World.HasLegacyHexGrid)
            throw new NotSupportedException(
                "Generalized world traversal is gated until Phase 18.");
        var materialized = CrawlProcedureCatalog.Resolve(command.ProcedureKey).MaterializeGeneric();
        if (materialized.Procedure.TilingDsSymbol != LegacyHexTilingCompatibility.HexQuotient)
            throw new NotSupportedException(
                "The selected procedure is not compatible with hex-only movement.");
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
            OverworldId = world.World.Id
        };
        var now = DateTimeOffset.UtcNow;
        return await _store.CreateExpeditionAsync(new StoredExpedition(
            RequiredText(command.Name, "Expedition name"),
            state,
            new WorldBoundCrawlSessionContext(world.World.Id),
            knowledge,
            materialized.Procedure,
            null,
            TimeSpan.Zero,
            world.OwnerUserId,
            1,
            now,
            now)
        {
            ProcedureOrigin = materialized.Origin
        }, cancellationToken);
    }

    public async Task<StoredExpedition> AdvanceExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        AdvanceExpeditionCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var state = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("Spatial expedition advancement requires a spatial crawl session.");
        var (runtimeContext, world) = await ResolveSpatialContextAsync(expedition, ownerUserId, cancellationToken);
        if (command.ResolutionSource == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException(
                "AutomaticRoll is reserved for server-verified procedure-helper results and can not be supplied to the manual advance path.");
        }
        var provenance = new ResolutionProvenance(command.ResolutionSource, command.DmOverrideNote);
        var procedure = ExpeditionProcedureExecutionResolver.Resolve(expedition);
        var travel = BuildTravel(procedure, runtimeContext.HexCenterDistance.Unit, command, provenance);
        var navigation = BuildNavigation(procedure, state, command, provenance);
        var encounter = BuildEncounter(
            ExpeditionProcedureRequirements.IsEncounterCheckDue(procedure, state),
            state,
            command,
            provenance);
        if (world is null && encounter?.Kind == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            throw new InvalidOperationException("Keyed-location discovery requires a world-bound crawl session.");
        }
        var boundaryDecision = command.RecognizedLost.HasValue || command.Reorient.HasValue
            ? new BoundaryNavigationDecision(command.RecognizedLost ?? false, command.Reorient ?? false, provenance)
            : null;
        var activityAssignments = state.ActiveWatch?.Plan.Mode.ActivityAssignments
            ?? ParticipantActivityPolicyResolver.SnapshotAssignments(expedition.Party);
        var plan = new WatchTravelPlan(
            new HexDirection(command.IntendedDirection),
            new TravelModeSelection(RequiredText(command.PaceKey, "Pace key"), activityAssignments),
            new NavigationAidSelection(
                string.IsNullOrWhiteSpace(command.NavigationAidKey) ? "none" : command.NavigationAidKey.Trim(),
                command.SuppressesNavigationCheck,
                command.ResetsVeerAtBoundary),
            command.DeliberateDoubleBack,
            command.ContinueAcrossBoundaries);
        var result = ExpeditionProcedureExecutionResolver.Advance(
            _runtime,
            expedition,
            runtimeContext,
            state,
            plan,
            new WatchAdvanceInputs(travel, navigation, encounter, boundaryDecision, command.DmOverrideNote));
        var projectedState = result.Expedition;
        var projectedKnowledge = expedition.Knowledge;
        if (world is not null)
        {
            var projection = ExpeditionWorldComposition.Apply(
                world.World,
                result,
                expedition.RequireKnowledge(),
                applyAutomaticKnowledge: true);
            projectedState = projection.State;
            projectedKnowledge = projection.Knowledge;
        }
        var updated = expedition with
        {
            Runtime = projectedState,
            Knowledge = projectedKnowledge,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime
        };
        return await SaveExpeditionAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredExpedition> SetExpeditionCourseIntentAsync(
        Guid expeditionId,
        string ownerUserId,
        SetExpeditionCourseIntentCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var state = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("Course intent requires a spatial crawl session.");
        var intendedDirection = command.IntendedDirection.HasValue
            ? new HexDirection(command.IntendedDirection.Value)
            : (HexDirection?)null;
        var updatedState = CrawlRuntimeActions.SetIntendedCourse(state, intendedDirection);

        return await SaveExpeditionAsync(
            expedition with
            {
                Runtime = updatedState,
                GeneratedProcedureResolutions = []
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> RepositionExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        RepositionExpeditionCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var state = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("Party repositioning requires a spatial crawl session.");

        var (_, world) = await ResolveSpatialContextAsync(expedition, ownerUserId, cancellationToken);
        var repositioned = CrawlRuntimeActions.Reposition(state, command.TargetHex, command.Note);
        PlayerKnowledgeState? knowledge = expedition.Knowledge;
        if (world is not null)
        {
            repositioned = repositioned with
            {
                Position = HexGeometry.HexToWorld(world.World.Grid, command.TargetHex),
                PositionPrecision = WorldPositionPrecision.HexAnchor
            };
            if (knowledge?.PresentationPolicy is { } presentation)
            {
                knowledge = PresentationKnowledgeProjection.ApplyEnteredHexes(
                    presentation,
                    knowledge,
                    [command.TargetHex]);
            }
        }
        else
        {
            repositioned = repositioned with
            {
                Position = null,
                PositionPrecision = null
            };
        }

        var updated = expedition with
        {
            Runtime = repositioned,
            Knowledge = knowledge,
            PauseReason = null,
            RemainingWatchTime = TimeSpan.Zero,
            GeneratedProcedureResolutions = []
        };
        return await SaveExpeditionAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredExpedition> DiscoverAsync(
        Guid expeditionId,
        string ownerUserId,
        DiscoverSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        if (expedition.Context is not WorldBoundCrawlSessionContext worldContext)
        {
            throw new InvalidOperationException("Semantic discovery requires a world-bound crawl session.");
        }
        var world = await GetOverworldAsync(worldContext.WorldId, ownerUserId, cancellationToken);
        var state = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("World-bound discovery requires spatial expedition state.");
        var result = CrawlRuntimeActions.Discover(
            world.World,
            state,
            expedition.RequireKnowledge(),
            command.SubjectId,
            command.SubjectType,
            string.IsNullOrWhiteSpace(command.Source) ? "dm:manual-discovery" : command.Source.Trim());
        var updated = expedition with { Runtime = result.Expedition, Knowledge = result.Knowledge };
        return await SaveExpeditionAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    private async Task<(CrawlRuntimeContext RuntimeContext, StoredOverworld? World)> ResolveSpatialContextAsync(
        StoredExpedition expedition,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        switch (expedition.Context)
        {
            case WorldBoundCrawlSessionContext worldContext:
            {
                var world = await GetOverworldAsync(worldContext.WorldId, ownerUserId, cancellationToken);
                if (!world.World.HasLegacyHexGrid
                    || expedition.CampaignProcedure.TilingDsSymbol != LegacyHexTilingCompatibility.HexQuotient)
                    throw new NotSupportedException(
                        "Spatial traversal requires a matching hex tiling until Phase 18.");
                return (ExpeditionWorldComposition.RuntimeContext(world.World), world);
            }
            case AbstractHexCrawlSessionContext abstractContext:
                if (expedition.CampaignProcedure.TilingDsSymbol != LegacyHexTilingCompatibility.HexQuotient)
                    throw new NotSupportedException(
                        "Abstract hex traversal requires a hex procedure until Phase 18.");
                abstractContext.Validate();
                return (abstractContext.HexContext, null);
            case NonSpatialCrawlSessionContext:
                throw new InvalidOperationException("This crawl session is non-spatial.");
            default:
                throw new InvalidOperationException("Unsupported crawl session context.");
        }
    }

    private static ResolvedTravelAmount BuildTravel(
        GenericProcedureRuntime procedure,
        DistanceUnit unit,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance) =>
        ProcedureTravelInputPolicy.Build(
            procedure,
            unit,
            null,
            command.ExpectedDistance,
            command.ActualDistance,
            command.HexSteps,
            provenance);

    private static ResolvedNavigation? BuildNavigation(
        GenericProcedureRuntime procedure,
        ExpeditionState state,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance)
    {
        if (state.ActiveWatch is not null
            || !procedure.Navigation.UsesNavigationChecks
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
        bool encounterDue,
        ExpeditionState state,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance)
    {
        if (state.ActiveWatch is not null || !encounterDue)
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
}
