using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

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
        var knowledge = expedition.RequireKnowledge();
        var source = string.IsNullOrWhiteSpace(command.Source)
            ? "dm:manual-discovery" : command.Source.Trim();
        if (expedition.Runtime is not CellExpeditionState cells)
            throw new InvalidOperationException(
                "World-bound discovery requires an authoritative cell expedition.");
        var result = CrawlRuntimeActions.DiscoverCell(
            world.World, cells, knowledge, command.SubjectId, command.SubjectType, source);
        var updated = expedition with
        {
            Runtime = result.Expedition,
            Knowledge = result.Knowledge
        };
        return await SaveExpeditionAsync(updated, command.ExpectedVersion, cancellationToken);
    }

}
