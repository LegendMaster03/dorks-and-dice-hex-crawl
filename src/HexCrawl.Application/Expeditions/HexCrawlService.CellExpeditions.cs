using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
    // The HTTP contract is not an authority boundary: application callers must
    // independently reject server-owned AutomaticRoll and procedure defaults.
    private static void RequireClientCellProvenance(ResolutionProvenance? provenance)
    {
        if (provenance is null || provenance.Source is not (
            ResolutionSource.ManualRoll or ResolutionSource.ExternalSystem or ResolutionSource.DmOverride))
            throw new InvalidOperationException(
                "Server-owned procedure resolution provenance requires a verified persisted V2 helper generation, which is not yet supported.");
        if (provenance.Note is { Length: > 2000 })
            throw new InvalidOperationException("Cell resolution provenance note exceeds 2000 characters.");
    }

    private static void RequireClientCellInputs(CellWatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Travel);
        RequireClientCellProvenance(inputs.Travel.Provenance);
        if (inputs.Navigation is { } navigation
            && !(navigation.Outcome == NavigationCheckOutcome.NotRequired
                && navigation.Provenance?.Source == ResolutionSource.ProcedureDefault))
            RequireClientCellProvenance(navigation.Provenance);
        if (inputs.Encounter is { } encounter
            && !(encounter.Kind == EncounterOutcomeKind.None
                && encounter.Provenance?.Source == ResolutionSource.ProcedureDefault))
            RequireClientCellProvenance(encounter.Provenance);
        if (inputs.BoundaryDecision is { } boundary)
            RequireClientCellProvenance(boundary.Provenance);
        if (inputs.DmOverrideNote is { Length: > 2000 })
            throw new InvalidOperationException("DM override note exceeds 2000 characters.");
    }

    // Record which resolution source the client submitted. This is an audit
    // assertion, NOT proof of a generated/server result or proof that the
    // amount was consumed (a watch can pause before consuming its travel).
    private static CellExpeditionState RecordSubmittedCellProvenance(
        CellExpeditionState state, CellWatchAdvanceInputs inputs)
    {
        var added = new List<CrawlRuntimeEvent>();
        var sequence = state.History.Count == 0 ? 1 : state.History[^1].Sequence + 1;
        var watch = state.ActiveWatch?.WatchNumber ?? Math.Max(1, state.CompletedWatches);
        void Add(string component, ResolutionProvenance source)
        {
            var note = string.IsNullOrWhiteSpace(source.Note) ? "" : " Note: " + source.Note.Trim();
            added.Add(new CrawlRuntimeEvent(
                sequence++, watch, CrawlRuntimeEventKind.ResolutionProvenanceRecorded,
                state.ElapsedTravelTime, null,
                $"{component} source submitted as {source.Source}.{note}")
            { Cell = state.Traversal.CurrentCell });
        }
        Add("Cell travel", inputs.Travel.Provenance);
        if (inputs.Navigation is { } navigation
            && navigation.Provenance?.Source != ResolutionSource.ProcedureDefault)
            Add("Cell navigation", navigation.Provenance);
        if (inputs.Encounter is { } encounter
            && encounter.Provenance?.Source != ResolutionSource.ProcedureDefault)
            Add("Cell encounter", encounter.Provenance);
        if (inputs.BoundaryDecision is { } boundary)
            Add("Cell boundary decision", boundary.Provenance);
        return state with { History = [.. state.History, .. added] };
    }

    private static IReadOnlyList<GeneratedProcedureResolution> SupersedeCellResolutionGrants(
        StoredExpedition expedition) =>
        expedition.GeneratedProcedureResolutions.Select(item =>
            item.Status == GeneratedProcedureResolutionStatus.Available
                ? item with { Status = GeneratedProcedureResolutionStatus.Superseded }
                : item).ToArray();

    /// <summary>
    /// Obtain the one authoritative geometry for a previously owner-loaded V2
    /// expedition. Abstract sessions reconstruct exactly the same cells from
    /// their persisted session id and context, without a database world row.
    /// Callers must first load the expedition through the owner-scoped store.
    /// </summary>
    public async Task<(PeriodicWorldTiling Tiling, OverworldDefinition? World)> ResolveCellGeometryAsync(
        StoredExpedition expedition,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (expedition.Runtime is not CellExpeditionState state)
            throw new InvalidOperationException("This expedition has no generalized cell traversal state.");
        if (!string.Equals(expedition.OwnerUserId, ownerUserId, StringComparison.Ordinal))
            throw new HexCrawlNotFoundException("Expedition was not found.");
        switch (expedition.Context)
        {
            case WorldBoundCrawlSessionContext bound:
            {
                var savedWorld = await GetOverworldAsync(bound.WorldId, ownerUserId, cancellationToken);
                var tiling = savedWorld.World.SpatialTiling;
                state.Validate(tiling);
                return (tiling, savedWorld.World);
            }
            case AbstractHexCrawlSessionContext mapless:
            {
                var tiling = AbstractCellSessionGeometry.CreateHexWorld(expedition.Id, mapless);
                state.Validate(tiling);
                return (tiling, null);
            }
            default:
                throw new InvalidOperationException("This expedition does not have spatial geometry.");
        }
    }

    /// <summary>
    /// Execute a generalized world watch through the same owner-scoped,
    /// optimistic-concurrency persistence boundary used by hex expeditions.
    /// The pinned procedure and stored world are reloaded for each mutation.
    /// </summary>
    public async Task<StoredExpedition> AdvanceCellExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CellWatchTravelPlan plan,
        CellWatchAdvanceInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, expedition.Version);
        var state = expedition.Runtime as CellExpeditionState
            ?? throw new InvalidOperationException("This expedition has no generalized cell traversal state.");
        RequireClientCellInputs(inputs);
        JourneyRuntimeIntegration.EnsureRelevantTravelAllowed(expedition);
        ParticipantActivityPolicyResolver.ValidateAssignments(
            expedition.Party,
            ParticipantActivityPolicyResolver.Resolve(expedition.CampaignProcedure));
        ArgumentNullException.ThrowIfNull(plan.Mode);
        var assignments = state.ActiveWatch?.Plan.Mode.ActivityAssignments
            ?? ParticipantActivityPolicyResolver.SnapshotAssignments(expedition.Party);
        if (plan.Mode.ActivityAssignments is null
            || !plan.Mode.ActivityAssignments.SequenceEqual(assignments))
            throw new InvalidOperationException(
                "Watch activity assignments must match the authoritative party snapshot.");
        var geometry = await ResolveCellGeometryAsync(expedition, ownerUserId, cancellationToken);
        if (geometry.World is null && inputs.Encounter?.Kind == EncounterOutcomeKind.KeyedLocationDiscovery)
            throw new InvalidOperationException("A keyed location encounter requires an authored world.");
        var result = _runtime.AdvanceCellWatch(
            geometry.Tiling, expedition.CampaignProcedure, state, plan, inputs);
        // World knowledge is only projected for authored worlds. Mapless
        // sessions retain their real cell event history without invented maps.
        var updatedState = result.Expedition;
        var knowledge = expedition.Knowledge;
        if (geometry.World is { } world)
        {
            var projected = ExpeditionCellWorldComposition.Apply(
                world, result, expedition.RequireKnowledge(), applyAutomaticKnowledge: true);
            updatedState = projected.State;
            knowledge = projected.Knowledge;
        }
        updatedState = RecordSubmittedCellProvenance(updatedState, inputs);
        var forcedTravel = ForcedTravelAccounting.AccountTravelMutation(
            expedition, state, updatedState, expectedVersion,
            "cell-watch-travel",
            new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "cell-watch-travel",
                Note: inputs.DmOverrideNote ?? inputs.Travel.Provenance.Note));
        var journey = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition, state, updatedState, expedition.Journey);
        var updated = expedition with
        {
            Runtime = updatedState,
            Knowledge = knowledge,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime,
            Survival = forcedTravel.Survival,
            Journey = journey,
            GeneratedProcedureResolutions = SupersedeCellResolutionGrants(expedition)
        };
        return await SaveExpeditionAsync(updated, expectedVersion, cancellationToken);
    }

    public async Task<StoredExpedition> ResolveCellExpeditionCourseAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CellCourseDecision decision,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, expedition.Version);
        var state = expedition.Runtime as CellExpeditionState
            ?? throw new InvalidOperationException("This expedition has no generalized cell traversal state.");
        ArgumentNullException.ThrowIfNull(decision);
        RequireClientCellProvenance(decision.Provenance);
        var geometry = await ResolveCellGeometryAsync(expedition, ownerUserId, cancellationToken);
        var result = _runtime.ResolveCellCourse(geometry.Tiling, state, decision);
        return await SaveExpeditionAsync(expedition with
        {
            Runtime = result.Expedition,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime,
            GeneratedProcedureResolutions = SupersedeCellResolutionGrants(expedition)
        }, expectedVersion, cancellationToken);
    }

    /// <summary>
    /// Explicit exact-position correction, distinct from travel and available
    /// to any V2 spatial session. Paused watches are not silently abandoned.
    /// </summary>
    public async Task<StoredExpedition> RepositionCellExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        WorldCellId destination,
        WorldPoint position,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, expedition.Version);
        var state = expedition.Runtime as CellExpeditionState
            ?? throw new InvalidOperationException("Cell repositioning requires a generalized spatial expedition.");
        if (expedition.PauseReason is not null || expedition.RemainingWatchTime != TimeSpan.Zero)
            throw new InvalidOperationException(
                "Resolve the suspended watch or pending decision before repositioning this expedition.");
        var geometry = await ResolveCellGeometryAsync(expedition, ownerUserId, cancellationToken);
        var corrected = CrawlRuntimeActions.RepositionCell(
            geometry.Tiling, state, destination, position, note);
        var knowledge = expedition.Knowledge;
        if (geometry.World is not null && knowledge?.PresentationPolicy is { } policy)
            knowledge = PresentationKnowledgeProjection.ApplyEnteredCells(
                policy, knowledge, [destination]);
        return await SaveExpeditionAsync(expedition with
        {
            Runtime = corrected,
            Knowledge = knowledge,
            PauseReason = null,
            RemainingWatchTime = TimeSpan.Zero,
            // Invalidate outstanding helper grants without deleting their audit
            // snapshots or already consumed/superseded provenance.
            GeneratedProcedureResolutions = expedition.GeneratedProcedureResolutions
                .Select(item => item.Status == GeneratedProcedureResolutionStatus.Available
                    ? item with { Status = GeneratedProcedureResolutionStatus.Superseded }
                    : item).ToArray()
        }, expectedVersion, cancellationToken);
    }

    public async Task<StoredExpedition> ResolveCellExpeditionEncounterAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        Guid occurrenceId,
        string? resultNote = null,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, expedition.Version);
        var state = expedition.Runtime as CellExpeditionState
            ?? throw new InvalidOperationException("This expedition has no generalized cell traversal state.");
        var geometry = await ResolveCellGeometryAsync(expedition, ownerUserId, cancellationToken);
        state.Validate(geometry.Tiling);
        var result = _runtime.ResolveCellEncounter(state, occurrenceId, resultNote);
        return await SaveExpeditionAsync(expedition with
        {
            Runtime = result.Expedition,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime,
            GeneratedProcedureResolutions = SupersedeCellResolutionGrants(expedition)
        }, expectedVersion, cancellationToken);
    }
}
