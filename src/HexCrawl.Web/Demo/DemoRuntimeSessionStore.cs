using HexCrawl.Application;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Demo;

public sealed class DemoRuntimeSessionStore
{
    private readonly object _gate = new();
    private readonly CrawlRuntimeEngine _engine = new();
    private DemoRuntimeSession _session;

    public DemoRuntimeSessionStore()
    {
        _session = CreateSession(DemoRuntimeProfiles.Resolve(null), new DemoRuntimeStartRequest(null));
    }

    public DemoRuntimeResponse Snapshot()
    {
        lock (_gate)
        {
            return ToResponse(_session);
        }
    }

    public DemoRuntimeResponse Reset(DemoRuntimeStartRequest request)
    {
        var profile = DemoRuntimeProfiles.Resolve(request.ProfileKey);
        lock (_gate)
        {
            _session = CreateSession(profile, request);
            return ToResponse(_session);
        }
    }

    public DemoRuntimeResponse Advance(DemoRuntimeAdvanceRequest request)
    {
        lock (_gate)
        {
            var world = _session.World;
            var profile = _session.Profile;
            var provenance = new ResolutionProvenance(ParseResolutionSource(request.ResolutionSource), request.DmOverrideNote);
            var travel = BuildTravel(profile, world.Grid.NeighborCenterDistance.Unit, request, provenance);
            var navigation = BuildNavigation(profile, _session.Expedition, request, provenance);
            var encounter = BuildEncounter(profile, _session.Expedition, request, provenance);
            var boundaryDecision = request.RecognizedLost.HasValue || request.Reorient.HasValue
                ? new BoundaryNavigationDecision(
                    request.RecognizedLost ?? false,
                    request.Reorient ?? false,
                    provenance)
                : null;
            var plan = new WatchTravelPlan(
                new HexDirection(request.IntendedDirection),
                new TravelModeSelection(
                    string.IsNullOrWhiteSpace(request.PaceKey) ? "normal" : request.PaceKey.Trim(),
                    request.Activities ?? []),
                new NavigationAidSelection(
                    string.IsNullOrWhiteSpace(request.NavigationAidKey) ? "none" : request.NavigationAidKey.Trim(),
                    request.SuppressesNavigationCheck,
                    request.ResetsVeerAtBoundary),
                request.DeliberateDoubleBack,
                request.ContinueAcrossBoundaries);
            var result = _engine.Advance(
                new CrawlRuntimeContext(world.Grid.NeighborCenterDistance),
                profile,
                _session.Expedition,
                plan,
                new WatchAdvanceInputs(
                    travel,
                    navigation,
                    encounter,
                    boundaryDecision,
                    request.DmOverrideNote));
            var projection = ExpeditionWorldComposition.Apply(
                world,
                result,
                _session.Knowledge,
                applyAutomaticKnowledge: true);

            _session = _session with
            {
                Expedition = projection.State,
                Knowledge = projection.Knowledge,
                PauseReason = result.PauseReason,
                RemainingWatchTime = result.RemainingWatchTime
            };
            return ToResponse(_session);
        }
    }

    public DemoRuntimeResponse Discover(DemoDiscoveryRequest request)
    {
        var subjectType = ParseSubjectType(request.SubjectType);
        lock (_gate)
        {
            var result = CrawlRuntimeActions.Discover(
                _session.World,
                _session.Expedition,
                _session.Knowledge,
                request.SubjectId,
                subjectType,
                string.IsNullOrWhiteSpace(request.Source) ? "dm:manual-discovery" : request.Source.Trim());
            _session = _session with
            {
                Expedition = result.Expedition,
                Knowledge = result.Knowledge
            };
            return ToResponse(_session);
        }
    }

    private static DemoRuntimeSession CreateSession(
        CrawlProcedureProfile profile,
        DemoRuntimeStartRequest request)
    {
        var orientation = string.Equals(request.Orientation, "flat", StringComparison.OrdinalIgnoreCase)
            ? HexOrientation.FlatTop
            : HexOrientation.PointyTop;
        var scale = request.Scale is > 0 and <= 10000 ? request.Scale.Value : 12d;
        var unit = string.Equals(request.Unit, "km", StringComparison.OrdinalIgnoreCase)
            ? DistanceUnit.Kilometers
            : DistanceUnit.Miles;
        var world = DemoWorldFactory.Create(orientation, scale, unit);
        var startHex = new HexCoordinate(0, 0);
        var expedition = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            OverworldId = world.Id,
            Position = HexGeometry.HexToWorld(world.Grid, startHex),
            PositionPrecision = WorldPositionPrecision.HexAnchor,
            Traversal = HexTraversalState.StartingIn(startHex, world.Grid.NeighborCenterDistance.Unit),
            Navigation = new NavigationRuntimeState(false, 0),
            DistanceTraveled = new DistanceMeasure(0, world.Grid.NeighborCenterDistance.Unit)
        };
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.Id
        };
        return new DemoRuntimeSession(world, profile, expedition, knowledge, null, TimeSpan.Zero);
    }

    private static ResolvedTravelAmount BuildTravel(
        CrawlProcedureProfile profile,
        DistanceUnit unit,
        DemoRuntimeAdvanceRequest request,
        ResolutionProvenance provenance)
    {
        if (profile.TravelResolution == TravelResolutionMode.HexSteps)
        {
            if (request.HexSteps is null)
            {
                throw new InvalidOperationException("The selected procedure requires a resolved hex-step count.");
            }
            return ResolvedTravelAmount.Steps(request.HexSteps.Value, provenance);
        }

        if (request.ExpectedDistance is null || request.ActualDistance is null)
        {
            throw new InvalidOperationException("The selected procedure requires expected and actual travel distance.");
        }
        return ResolvedTravelAmount.Distance(
            new DistanceMeasure(request.ExpectedDistance.Value, unit),
            new DistanceMeasure(request.ActualDistance.Value, unit),
            provenance);
    }

    private static ResolvedNavigation? BuildNavigation(
        CrawlProcedureProfile profile,
        ExpeditionState expedition,
        DemoRuntimeAdvanceRequest request,
        ResolutionProvenance provenance)
    {
        if (expedition.ActiveWatch is not null || !profile.UsesNavigationChecks || request.SuppressesNavigationCheck || request.DeliberateDoubleBack)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.NavigationOutcome))
        {
            throw new InvalidOperationException("This watch requires an explicit navigation outcome.");
        }
        var outcome = request.NavigationOutcome.Trim().ToLowerInvariant() switch
        {
            "success" or "succeeded" => NavigationCheckOutcome.Succeeded,
            "failure" or "failed" => NavigationCheckOutcome.Failed,
            _ => throw new InvalidOperationException("Navigation outcome must be success or failure.")
        };
        return new ResolvedNavigation(
            outcome,
            outcome == NavigationCheckOutcome.Failed ? request.VeerSteps : null,
            provenance);
    }

    private static ResolvedEncounter? BuildEncounter(
        CrawlProcedureProfile profile,
        ExpeditionState expedition,
        DemoRuntimeAdvanceRequest request,
        ResolutionProvenance provenance)
    {
        if (expedition.ActiveWatch is not null || profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return null;
        }

        var kind = string.IsNullOrWhiteSpace(request.EncounterOutcome)
            ? EncounterOutcomeKind.None
            : request.EncounterOutcome.Trim().ToLowerInvariant() switch
            {
                "none" => EncounterOutcomeKind.None,
                "wandering" or "wanderingencounter" => EncounterOutcomeKind.WanderingEncounter,
                "location" or "keyedlocation" or "keyedlocationdiscovery" => EncounterOutcomeKind.KeyedLocationDiscovery,
                "manual" or "custom" => EncounterOutcomeKind.ManualCustom,
                _ => throw new InvalidOperationException("Unknown encounter outcome.")
            };
        return new ResolvedEncounter(
            kind,
            kind == EncounterOutcomeKind.None
                ? null
                : TimeSpan.FromHours(request.EncounterHour ?? throw new InvalidOperationException("A triggered encounter requires an encounter hour.")),
            request.LocationId,
            request.EncounterNote,
            provenance);
    }

    private static ResolutionSource ParseResolutionSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ResolutionSource.ManualRoll;
        }
        return Enum.TryParse<ResolutionSource>(source.Trim(), true, out var parsed)
            ? parsed
            : throw new InvalidOperationException("Unknown resolution source.");
    }

    private static KnowledgeSubjectType ParseSubjectType(string subjectType) =>
        subjectType.Trim().ToLowerInvariant() switch
        {
            "location" => KnowledgeSubjectType.Location,
            "feature" => KnowledgeSubjectType.Feature,
            _ => throw new InvalidOperationException("Discovery subject type must be location or feature.")
        };

    private static DemoRuntimeResponse ToResponse(DemoRuntimeSession session) =>
        DemoRuntimeResponse.From(
            session.Profile,
            session.World.Grid.NeighborCenterDistance,
            session.Expedition,
            session.Knowledge,
            session.PauseReason,
            session.RemainingWatchTime);

    private sealed record DemoRuntimeSession(
        OverworldDefinition World,
        CrawlProcedureProfile Profile,
        ExpeditionState Expedition,
        PlayerKnowledgeState Knowledge,
        RuntimePauseReason? PauseReason,
        TimeSpan RemainingWatchTime);
}
