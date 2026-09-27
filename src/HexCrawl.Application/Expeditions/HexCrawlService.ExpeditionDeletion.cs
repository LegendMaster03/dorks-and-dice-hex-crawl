using HexCrawl.Application.Persistence;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
    public async Task<StoredExpedition> DeleteExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        var outcome = await _store.DeleteExpeditionAsync(
            expeditionId,
            current.OwnerUserId,
            expectedVersion,
            cancellationToken);
        return outcome switch
        {
            DeleteExpeditionOutcome.Deleted => current,
            DeleteExpeditionOutcome.NotFound => throw new HexCrawlNotFoundException("Crawl session was not found."),
            DeleteExpeditionOutcome.Conflict => throw new HexCrawlConcurrencyException("The running sheet was changed by another request. Reload it before deleting it."),
            _ => throw new InvalidOperationException("The running-sheet delete operation returned an unknown result.")
        };
    }
}
