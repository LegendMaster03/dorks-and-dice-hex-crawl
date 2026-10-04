using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record UpdateExpeditionPartyCommand(
    long ExpectedVersion,
    CrawlPartySheet Party,
    bool ReplaceMovementContributors = false);

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

        var party = command.ReplaceMovementContributors
            ? command.Party
            : command.Party with { MovementContributors = expedition.Party.MovementContributors };
        party.Validate();
        var policy = ParticipantActivityPolicyResolver.Resolve(expedition.CampaignProcedure);
        ParticipantActivityPolicyResolver.ValidateAssignments(party, policy);

        // Effects, resources, survival counters, and pending consequence targets are one aggregate.
        // Reject participant/mount/vehicle edits that would leave any Phase 10/11 reference dangling.
        expedition.Effects.Validate(party);
        expedition.Resources.Validate(party);
        expedition.Survival.Validate(party);

        var updated = expedition with { Party = party };
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
