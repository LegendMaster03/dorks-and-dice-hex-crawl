using System.Text.Json.Serialization;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class PostgresHexCrawlStore
{
    private enum RuntimeStateKind
    {
        Spatial,
        NonSpatial,
        CellSpatial
    }

    private sealed record RuntimeStateSnapshot
    {
        public RuntimeStateKind Kind { get; init; } = RuntimeStateKind.Spatial;
        public Guid Id { get; init; }
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
        public PendingEncounterSnapshot? PendingEncounter { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PeriodicCellTraversal? CellTraversal { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorldPoint? CellIntendedHeading { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CellIsLost { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? CellVeerDegrees { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellActiveWatchSnapshot? CellActiveWatch { get; init; }

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
                ActiveWatch = state.ActiveWatch is null ? null : ActiveWatchSnapshot.FromDomain(state.ActiveWatch),
                PendingEncounter = state.PendingEncounter is null ? null : PendingEncounterSnapshot.FromDomain(state.PendingEncounter)
            },
            CellExpeditionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.CellSpatial,
                Id = state.Id,
                CellTraversal = state.Traversal,
                CellIntendedHeading = state.IntendedHeading,
                CellIsLost = state.IsLost,
                CellVeerDegrees = state.ResolvedVeerDegrees,
                DistanceTraveled = state.DistanceTraveled,
                ElapsedTravelTicks = state.ElapsedTravelTime.Ticks,
                CompletedWatches = state.CompletedWatches,
                CellActiveWatch = state.ActiveWatch is null ? null : CellActiveWatchSnapshot.FromDomain(state.ActiveWatch),
                PendingEncounter = state.PendingEncounter is null ? null : PendingEncounterSnapshot.FromDomain(state.PendingEncounter)
            },
            NonSpatialSessionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.NonSpatial,
                Id = state.Id,
                ElapsedTravelTicks = state.ElapsedTime.Ticks,
                CompletedWatches = state.CompletedWatches,
                NonSpatialActiveWatch = state.ActiveWatch is null
                    ? null
                    : NonSpatialActiveWatchSnapshot.FromDomain(state.ActiveWatch),
                PendingEncounter = state.PendingEncounter is null ? null : PendingEncounterSnapshot.FromDomain(state.PendingEncounter)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(runtime))
        };

        public CrawlSessionRuntimeState ToDomain() => Kind switch
        {
            RuntimeStateKind.Spatial => ToSpatial(),
            RuntimeStateKind.CellSpatial => ToCellSpatial(),
            RuntimeStateKind.NonSpatial => new NonSpatialSessionState
            {
                Id = Id,
                ElapsedTime = TimeSpan.FromTicks(ElapsedTravelTicks),
                CompletedWatches = CompletedWatches,
                ActiveWatch = NonSpatialActiveWatch?.ToDomain(),
                PendingEncounter = PendingEncounter?.ToDomain(),
                History = []
            },
            _ => throw new InvalidDataException("Persisted crawl session runtime kind is not supported.")
        };

        private CellExpeditionState ToCellSpatial()
        {
            if (CurrentHex is not null || EntryDirection is not null || LastTravelDirection is not null
                || Progress is not null || IntendedDirection is not null || ActualDirection is not null)
                throw new InvalidDataException("Generalized cell snapshots must not include axial runtime authority.");
            var cursor = CellTraversal
                ?? throw new InvalidDataException("Persisted generalized expedition has no traversal cursor.");
            if (cursor.FormatVersion != PeriodicCellTraversal.CurrentFormatVersion)
                throw new InvalidDataException("Unsupported generalized traversal cursor format.");
            var distance = DistanceTraveled
                ?? throw new InvalidDataException("Persisted generalized expedition has no distance accounting.");
            var result = new CellExpeditionState
            {
                Id = Id,
                Traversal = cursor,
                IntendedHeading = CellIntendedHeading,
                IsLost = CellIsLost ?? false,
                ResolvedVeerDegrees = CellVeerDegrees,
                DistanceTraveled = distance,
                ElapsedTravelTime = TimeSpan.FromTicks(ElapsedTravelTicks),
                CompletedWatches = CompletedWatches,
                ActiveWatch = CellActiveWatch?.ToDomain(),
                PendingEncounter = PendingEncounter?.ToDomain(),
                History = []
            };
            if (result.ActiveWatch is not null && result.ActiveWatch.WatchNumber <= result.CompletedWatches)
                throw new InvalidDataException("Active generalized watch number must follow completed watches.");
            return result;
        }

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
                PendingEncounter = PendingEncounter?.ToDomain(),
                History = []
            };
        }
    }

    private sealed record CellActiveWatchSnapshot(
        int WatchNumber,
        long TotalDurationTicks,
        long ElapsedTicks,
        CellWatchTravelPlan Plan,
        ResolvedEncounterSnapshot Encounter,
        bool EncounterHandled,
        RuntimePauseReason? PendingDecision)
    {
        public static CellActiveWatchSnapshot FromDomain(CellActiveWatchState active) => new(
            active.WatchNumber, active.TotalDuration.Ticks, active.Elapsed.Ticks, active.Plan,
            ResolvedEncounterSnapshot.FromDomain(active.Encounter), active.EncounterHandled,
            active.PendingDecision);

        public CellActiveWatchState ToDomain()
        {
            var state = new CellActiveWatchState(
                WatchNumber, TimeSpan.FromTicks(TotalDurationTicks), TimeSpan.FromTicks(ElapsedTicks),
                Plan, Encounter.ToDomain(), EncounterHandled, PendingDecision);
            state.Validate();
            return state;
        }
    }

    private sealed record PendingEncounterSnapshot(
        Guid Id, long TriggerSequence, int WatchNumber, EncounterOutcomeKind Outcome,
        long ExpeditionElapsedTicks, HexCoordinate? Hex, Guid? LocationId, string? Note,
        ResolutionSource Source, string? ProvenanceNote)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorldCellId? Cell { get; init; }

        public static PendingEncounterSnapshot FromDomain(PendingEncounterOccurrence encounter) => new(
            encounter.Id, encounter.TriggerSequence, encounter.WatchNumber, encounter.Outcome,
            encounter.ExpeditionElapsedTime.Ticks, encounter.Hex, encounter.LocationId,
            encounter.Note, encounter.Provenance.Source, encounter.Provenance.Note)
            { Cell = encounter.Cell };

        public PendingEncounterOccurrence ToDomain() => new(
            Id, TriggerSequence, WatchNumber, Outcome, TimeSpan.FromTicks(ExpeditionElapsedTicks),
            Hex, LocationId, Note, new ResolutionProvenance(Source, ProvenanceNote))
            { Cell = Cell };
    }

    private sealed record NonSpatialActiveWatchSnapshot(
        int WatchNumber,
        long TotalDurationTicks,
        long ElapsedTicks,
        IReadOnlyList<ParticipantActivityAssignment> ActivityAssignments)
    {
        public static NonSpatialActiveWatchSnapshot FromDomain(NonSpatialActiveWatchState active) => new(
            active.WatchNumber,
            active.TotalDuration.Ticks,
            active.Elapsed.Ticks,
            active.ActivityAssignments.Select(value => value with { }).ToArray());

        public NonSpatialActiveWatchState ToDomain()
        {
            var state = new NonSpatialActiveWatchState(
                WatchNumber,
                TimeSpan.FromTicks(TotalDurationTicks),
                TimeSpan.FromTicks(ElapsedTicks))
            {
                ActivityAssignments = ActivityAssignments.Select(value => value with { }).ToArray()
            };
            state.Validate();
            return state;
        }
    }

    private sealed record CrawlSessionContextSnapshot(
        CrawlSessionContextKind Kind,
        Guid? OverworldId = null,
        string? Name = null,
        HexOrientation? Orientation = null,
        DistanceMeasure? HexCenterDistance = null,
        Guid? CampaignId = null)
    {
        public static CrawlSessionContextSnapshot FromDomain(
            CrawlSessionContext context,
            Guid? campaignId = null) => context switch
        {
            WorldBoundCrawlSessionContext world => new(
                context.Kind,
                world.WorldId,
                CampaignId: campaignId),
            AbstractHexCrawlSessionContext hex => new(
                context.Kind,
                Name: hex.DisplayName,
                Orientation: hex.Orientation,
                HexCenterDistance: hex.HexContext.HexCenterDistance,
                CampaignId: campaignId),
            NonSpatialCrawlSessionContext nonSpatial => new(
                context.Kind,
                Name: nonSpatial.DisplayName,
                CampaignId: campaignId),
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
        IReadOnlyList<ParticipantActivityAssignment> ActivityAssignments,
        string NavigationAidKey,
        bool SuppressesNavigationCheck,
        bool ResetsVeerAtBoundary,
        bool DeliberateDoubleBack,
        bool ContinueAcrossBoundaries)
    {
        public static WatchTravelPlanSnapshot FromDomain(WatchTravelPlan plan) => new(
            plan.IntendedDirection.Value,
            plan.Mode.PaceKey,
            plan.Mode.ActivityAssignments.Select(value => value with { }).ToArray(),
            plan.NavigationAid.Key,
            plan.NavigationAid.SuppressesNavigationCheck,
            plan.NavigationAid.ResetsVeerAtBoundary,
            plan.DeliberateDoubleBack,
            plan.ContinueAcrossBoundaries);

        public WatchTravelPlan ToDomain() => new(
            new HexDirection(IntendedDirection),
            new TravelModeSelection(PaceKey, ActivityAssignments.Select(value => value with { }).ToArray()),
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
