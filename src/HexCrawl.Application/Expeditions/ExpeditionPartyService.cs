using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record UpdateExpeditionPartyCommand(
    long ExpectedVersion,
    CrawlPartySheet Party);

public sealed class ExpeditionPartyService(
    IHexCrawlStore store,
    HexCrawlService coreService)
{
    public async Task<StoredExpedition> UpdateAsync(
        Guid expeditionId,
        string ownerUserId,
        UpdateExpeditionPartyCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(
            expeditionId,
            ownerUserId,
            cancellationToken);

        if (command.ExpectedVersion != expedition.Version)
        {
            throw new HexCrawlConcurrencyException(
                "The resource version is stale. Reload the running sheet before saving party information.");
        }

        command.Party.Validate();
        var updated = expedition with { Party = command.Party };
        var result = await store.SaveExpeditionAsync(
            updated,
            command.ExpectedVersion,
            cancellationToken);

        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The running sheet was changed by another request. Reload it before saving party information."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };
    }
}
