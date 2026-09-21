using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
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
        var profile = CrawlProcedureCatalog.Resolve(command.ProcedureKey);
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
            profile,
            null,
            TimeSpan.Zero,
            world.OwnerUserId,
            1,
            now,
            now), cancellationToken);
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
        var provenance = new ResolutionProvenance(command.ResolutionSource, command.DmOverrideNote);
        var profile = expedition.Procedure;
        var travel = BuildTravel(profile, runtimeContext.HexCenterDistance.Unit, command, provenance);
        var navigation = BuildNavigation(profile, state, command, provenance);
        var encounter = BuildEncounter(profile, state, command, provenance);
        if (world is null && encounter?.Kind == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            throw new InvalidOperationException("Keyed-location discovery requires a world-bound crawl session.");
        }
        var boundaryDecision = command.RecognizedLost.HasValue || command.Reorient.HasValue
            ? new BoundaryNavigationDecision(command.RecognizedLost ?? false, command.Reorient ?? false, provenance)
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
            profile,
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
                return (ExpeditionWorldComposition.RuntimeContext(world.World), world);
            }
            case AbstractHexCrawlSessionContext abstractContext:
                abstractContext.Validate();
                return (abstractContext.HexContext, null);
            case NonSpatialCrawlSessionContext:
                throw new InvalidOperationException("This crawl session is non-spatial.");
            default:
                throw new InvalidOperationException("Unsupported crawl session context.");
        }
    }

    private static ResolvedTravelAmount BuildTravel(
        CrawlProcedureProfile profile,
        DistanceUnit unit,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance)
    {
        if (profile.TravelResolution == TravelResolutionMode.HexSteps)
        {
            return command.HexSteps.HasValue
                ? ResolvedTravelAmount.Steps(command.HexSteps.Value, provenance)
                : throw new InvalidOperationException("The selected procedure requires a resolved hex-step count.");
        }
        if (!command.ExpectedDistance.HasValue || !command.ActualDistance.HasValue)
        {
            throw new InvalidOperationException("The selected procedure requires expected and actual travel distance.");
        }
        return ResolvedTravelAmount.Distance(
            new DistanceMeasure(command.ExpectedDistance.Value, unit),
            new DistanceMeasure(command.ActualDistance.Value, unit),
            provenance);
    }

    private static ResolvedNavigation? BuildNavigation(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        AdvanceExpeditionCommand command,
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
        AdvanceExpeditionCommand command,
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


}
