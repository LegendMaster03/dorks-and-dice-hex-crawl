using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
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
        var context = expedition.Context as WorldBoundCrawlSessionContext
            ?? throw new InvalidOperationException("Generalized cell travel requires an authoritative world.");
        var world = await GetOverworldAsync(context.WorldId, ownerUserId, cancellationToken);
        var result = _runtime.AdvanceCellWatch(
            world.World.SpatialTiling, expedition.CampaignProcedure, state, plan, inputs);
        var updated = expedition with
        {
            Runtime = result.Expedition,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime
        };
        return await SaveExpeditionAsync(updated, expectedVersion, cancellationToken);
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
        var context = expedition.Context as WorldBoundCrawlSessionContext
            ?? throw new InvalidOperationException("Generalized cell travel requires an authoritative world.");
        var world = await GetOverworldAsync(context.WorldId, ownerUserId, cancellationToken);
        state.Validate(world.World.SpatialTiling);
        var result = _runtime.ResolveCellEncounter(state, occurrenceId, resultNote);
        return await SaveExpeditionAsync(expedition with
        {
            Runtime = result.Expedition,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime
        }, expectedVersion, cancellationToken);
    }
}
