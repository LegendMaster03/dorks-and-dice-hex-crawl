using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using Microsoft.Data.Sqlite;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class SqliteHexCrawlStore
{
    private enum RuntimeStateKind
    {
        Spatial,
        NonSpatial
    }

    private sealed record RuntimeStateSnapshot
    {
        public RuntimeStateKind Kind { get; init; } = RuntimeStateKind.Spatial;
        public Guid Id { get; init; }

        // Legacy v1 snapshots included OverworldId here. It is ignored after
        // migration because CrawlSessionContext is now authoritative.
        public Guid? OverworldId { get; init; }

        public WorldPoint? Position { get; init; }
        public WorldPositionPrecision? PositionPrecision { get; init; }
        public HexCoordinate? CurrentHex { get; init; }
        public int? EntryDirection { get; init; }
        public int? LastTravelDirection { get; init; }
        public DistanceMeasure? Progress { get; init; }
        public DistanceMeasure? CurrentExitRequirement { get; init; }
        public int? IntendedDirection { get; init; }
        public int? ActualDirection { get; init; }
        public bool IsLost { get; init; }
        public int VeerSteps { get; init; }
        public DistanceMeasure? DistanceTraveled { get; init; }
        public long ElapsedTravelTicks { get; init; }
        public int CompletedWatches { get; init; }
        public ActiveWatchSnapshot? ActiveWatch { get; init; }
        public NonSpatialActiveWatchSnapshot? NonSpatialActiveWatch { get; init; }

        public static RuntimeStateSnapshot FromDomain(CrawlSessionRuntimeState runtime) => runtime switch
        {
            ExpeditionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.Spatial,
                Id = state.Id,
                Position = state.Position,
                PositionPrecision = state.PositionPrecision,
                CurrentHex = state.Traversal.CurrentHex,
                EntryDirection = state.Traversal.EntryDirection?.Value,
                LastTravelDirection = state.Traversal.LastTravelDirection?.Value,
                Progress = state.Traversal.Progress,
                CurrentExitRequirement = state.Traversal.CurrentExitRequirement,
                IntendedDirection = state.IntendedDirection?.Value,
                ActualDirection = state.ActualDirection?.Value,
                IsLost = state.Navigation.IsLost,
                VeerSteps = state.Navigation.VeerSteps,
                DistanceTraveled = state.DistanceTraveled,
                ElapsedTravelTicks = state.ElapsedTravelTime.Ticks,
                CompletedWatches = state.CompletedWatches,
                ActiveWatch = state.ActiveWatch is null ? null : ActiveWatchSnapshot.FromDomain(state.ActiveWatch)
            },
            NonSpatialSessionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.NonSpatial,
                Id = state.Id,
                ElapsedTravelTicks = state.ElapsedTime.Ticks,
                CompletedWatches = state.CompletedWatches,
                NonSpatialActiveWatch = state.ActiveWatch is null
                    ? null
                    : NonSpatialActiveWatchSnapshot.FromDomain(state.ActiveWatch)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(runtime))
        };

        public CrawlSessionRuntimeState ToDomain() => Kind switch
        {
            RuntimeStateKind.Spatial => ToSpatial(),
            RuntimeStateKind.NonSpatial => new NonSpatialSessionState
            {
                Id = Id,
                ElapsedTime = TimeSpan.FromTicks(ElapsedTravelTicks),
                CompletedWatches = CompletedWatches,
                ActiveWatch = NonSpatialActiveWatch?.ToDomain(),
                History = []
            },
            _ => throw new InvalidDataException("Persisted crawl session runtime kind is not supported.")
        };

        private ExpeditionState ToSpatial()
        {
            var currentHex = CurrentHex
                ?? throw new InvalidDataException("Persisted spatial crawl state has no current hex.");
            var progress = Progress
                ?? throw new InvalidDataException("Persisted spatial crawl state has no traversal progress.");
            var distance = DistanceTraveled
                ?? throw new InvalidDataException("Persisted spatial crawl state has no total distance.");
            return new ExpeditionState
            {
                Id = Id,
                Position = Position,
                PositionPrecision = PositionPrecision,
                Traversal = new HexTraversalState
                {
                    CurrentHex = currentHex,
                    EntryDirection = EntryDirection.HasValue ? new HexDirection(EntryDirection.Value) : null,
                    LastTravelDirection = LastTravelDirection.HasValue ? new HexDirection(LastTravelDirection.Value) : null,
                    Progress = progress,
                    CurrentExitRequirement = CurrentExitRequirement
                },
                IntendedDirection = IntendedDirection.HasValue ? new HexDirection(IntendedDirection.Value) : null,
                ActualDirection = ActualDirection.HasValue ? new HexDirection(ActualDirection.Value) : null,
                Navigation = new NavigationRuntimeState(IsLost, VeerSteps),
                DistanceTraveled = distance,
                ElapsedTravelTime = TimeSpan.FromTicks(ElapsedTravelTicks),
                CompletedWatches = CompletedWatches,
                ActiveWatch = ActiveWatch?.ToDomain(),
                History = []
            };
        }
    }

    private sealed record NonSpatialActiveWatchSnapshot(
        int WatchNumber,
        long TotalDurationTicks,
        long ElapsedTicks)
    {
        public static NonSpatialActiveWatchSnapshot FromDomain(NonSpatialActiveWatchState active) => new(
            active.WatchNumber,
            active.TotalDuration.Ticks,
            active.Elapsed.Ticks);

        public NonSpatialActiveWatchState ToDomain()
        {
            var state = new NonSpatialActiveWatchState(
                WatchNumber,
                TimeSpan.FromTicks(TotalDurationTicks),
                TimeSpan.FromTicks(ElapsedTicks));
            state.Validate();
            return state;
        }
    }

    private sealed record CrawlSessionContextSnapshot(
        CrawlSessionContextKind Kind,
        Guid? OverworldId = null,
        string? Name = null,
        HexOrientation? Orientation = null,
        DistanceMeasure? HexCenterDistance = null)
    {
        public static CrawlSessionContextSnapshot FromDomain(CrawlSessionContext context) => context switch
        {
            WorldBoundCrawlSessionContext world => new(context.Kind, world.WorldId),
            AbstractHexCrawlSessionContext hex => new(
                context.Kind,
                Name: hex.DisplayName,
                Orientation: hex.Orientation,
                HexCenterDistance: hex.HexContext.HexCenterDistance),
            NonSpatialCrawlSessionContext nonSpatial => new(
                context.Kind,
                Name: nonSpatial.DisplayName),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };

        public CrawlSessionContext ToDomain() => Kind switch
        {
            CrawlSessionContextKind.WorldBound => new WorldBoundCrawlSessionContext(
                OverworldId ?? throw new InvalidDataException("Persisted world-bound context has no overworld id.")),
            CrawlSessionContextKind.AbstractHex => new AbstractHexCrawlSessionContext(
                Name ?? "Abstract hex crawl",
                Orientation ?? HexOrientation.PointyTop,
                new CrawlRuntimeContext(
                    HexCenterDistance ?? throw new InvalidDataException("Persisted abstract-hex context has no hex-center distance."))),
            CrawlSessionContextKind.NonSpatial => new NonSpatialCrawlSessionContext(Name ?? "Non-spatial session"),
            _ => throw new InvalidDataException("Persisted crawl session context kind is not supported.")
        };
    }

    private sealed record ActiveWatchSnapshot(
        int WatchNumber,
        long TotalDurationTicks,
        long ElapsedTicks,
        WatchTravelPlanSnapshot Plan,
        ResolvedEncounterSnapshot Encounter,
        bool EncounterHandled,
        RuntimePauseReason? PendingDecision)
    {
        public static ActiveWatchSnapshot FromDomain(ActiveWatchState active) => new(
            active.WatchNumber,
            active.TotalDuration.Ticks,
            active.Elapsed.Ticks,
            WatchTravelPlanSnapshot.FromDomain(active.Plan),
            ResolvedEncounterSnapshot.FromDomain(active.Encounter),
            active.EncounterHandled,
            active.PendingDecision);

        public ActiveWatchState ToDomain() => new(
            WatchNumber,
            TimeSpan.FromTicks(TotalDurationTicks),
            TimeSpan.FromTicks(ElapsedTicks),
            Plan.ToDomain(),
            Encounter.ToDomain(),
            EncounterHandled,
            PendingDecision);
    }

    private sealed record WatchTravelPlanSnapshot(
        int IntendedDirection,
        string PaceKey,
        IReadOnlyList<string> Activities,
        string NavigationAidKey,
        bool SuppressesNavigationCheck,
        bool ResetsVeerAtBoundary,
        bool DeliberateDoubleBack,
        bool ContinueAcrossBoundaries)
    {
        public static WatchTravelPlanSnapshot FromDomain(WatchTravelPlan plan) => new(
            plan.IntendedDirection.Value,
            plan.Mode.PaceKey,
            plan.Mode.Activities.ToArray(),
            plan.NavigationAid.Key,
            plan.NavigationAid.SuppressesNavigationCheck,
            plan.NavigationAid.ResetsVeerAtBoundary,
            plan.DeliberateDoubleBack,
            plan.ContinueAcrossBoundaries);

        public WatchTravelPlan ToDomain() => new(
            new HexDirection(IntendedDirection),
            new TravelModeSelection(PaceKey, Activities.ToArray()),
            new NavigationAidSelection(NavigationAidKey, SuppressesNavigationCheck, ResetsVeerAtBoundary),
            DeliberateDoubleBack,
            ContinueAcrossBoundaries);
    }

    private sealed record ResolvedEncounterSnapshot(
        EncounterOutcomeKind Kind,
        long? OccursAtTicks,
        Guid? LocationId,
        string? Note,
        ResolutionSource Source,
        string? ProvenanceNote)
    {
        public static ResolvedEncounterSnapshot FromDomain(ResolvedEncounter encounter) => new(
            encounter.Kind,
            encounter.OccursAt?.Ticks,
            encounter.LocationId,
            encounter.Note,
            encounter.Provenance.Source,
            encounter.Provenance.Note);

        public ResolvedEncounter ToDomain() => new(
            Kind,
            OccursAtTicks.HasValue ? TimeSpan.FromTicks(OccursAtTicks.Value) : null,
            LocationId,
            Note,
            new ResolutionProvenance(Source, ProvenanceNote));
    }


}
