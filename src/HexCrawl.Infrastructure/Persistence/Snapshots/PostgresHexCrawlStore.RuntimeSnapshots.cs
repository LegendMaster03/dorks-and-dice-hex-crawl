using System.Text.Json.Serialization;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class PostgresHexCrawlStore
{
    private enum RuntimeStateKind
    {
        Unspecified = 0,
        NonSpatial = 1,
        CellSpatial = 2
    }

    private sealed record RuntimeStateSnapshot
    {
        public RuntimeStateKind Kind { get; init; }
        public Guid Id { get; init; }
        public DistanceMeasure? DistanceTraveled { get; init; }
        public long ElapsedTravelTicks { get; init; }
        public int CompletedWatches { get; init; }
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
        public DistanceMeasure? CellPendingTravelBudgetCharge { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellActiveWatchSnapshot? CellActiveWatch { get; init; }

        public static RuntimeStateSnapshot FromDomain(CrawlSessionRuntimeState runtime) => runtime switch
        {
            CellExpeditionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.CellSpatial,
                Id = state.Id,
                CellTraversal = state.Traversal,
                CellIntendedHeading = state.IntendedHeading,
                CellIsLost = state.IsLost,
                CellVeerDegrees = state.ResolvedVeerDegrees,
                CellPendingTravelBudgetCharge = state.PendingTravelBudgetCharge,
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
            _ => throw new NotSupportedException("Only cell-based spatial or nonspatial session states are supported.")
        };

        public CrawlSessionRuntimeState ToDomain() => Kind switch
        {
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
            var cursor = CellTraversal
                ?? throw new InvalidDataException("Persisted generalized expedition has no traversal cursor.");
            if (cursor.FormatVersion != PeriodicCellTraversal.CurrentFormatVersion)
                throw new InvalidDataException("Unsupported generalized traversal cursor format.");
            var distance = DistanceTraveled;
            var result = new CellExpeditionState
            {
                Id = Id,
                Traversal = cursor,
                IntendedHeading = CellIntendedHeading,
                IsLost = CellIsLost ?? false,
                ResolvedVeerDegrees = CellVeerDegrees,
                PendingTravelBudgetCharge = CellPendingTravelBudgetCharge,
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorldPoint? PendingTurnFromHeading { get; init; }
        public bool TurnPreflightWasDeferred { get; init; }

        public static CellActiveWatchSnapshot FromDomain(CellActiveWatchState active) => new(
            active.WatchNumber, active.TotalDuration.Ticks, active.Elapsed.Ticks, active.Plan,
            ResolvedEncounterSnapshot.FromDomain(active.Encounter), active.EncounterHandled,
            active.PendingDecision)
        {
            PendingTurnFromHeading = active.PendingTurnFromHeading,
            TurnPreflightWasDeferred = active.TurnPreflightWasDeferred
        };

        public CellActiveWatchState ToDomain()
        {
            var state = new CellActiveWatchState(
                WatchNumber, TimeSpan.FromTicks(TotalDurationTicks), TimeSpan.FromTicks(ElapsedTicks),
                Plan, Encounter.ToDomain(), EncounterHandled, PendingDecision)
            {
                PendingTurnFromHeading = PendingTurnFromHeading,
                TurnPreflightWasDeferred = TurnPreflightWasDeferred
            };
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
