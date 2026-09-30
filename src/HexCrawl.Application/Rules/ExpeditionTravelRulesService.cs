using HexCrawl.Application.Persistence;

namespace HexCrawl.Application.Rules;

public sealed record ExpeditionRulesScopeView(Guid? CampaignId, long Version);

public sealed class ExpeditionTravelRulesService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    TravelEnvironmentProviderRegistry providers)
{
    public async Task<TravelEnvironmentProviderCatalogResult> GetCatalogAsync(
        Guid expeditionId,
        string ownerUserId,
        CancellationToken cancellationToken = default,
        string? providerKey = null)
    {
        var expedition = await coreService.GetExpeditionAsync(
            expeditionId,
            ownerUserId,
            cancellationToken);
        var selection = providers.Select(providerKey);
        if (selection.Provider is null)
        {
            return new TravelEnvironmentProviderCatalogResult(
                null,
                TravelEnvironmentProviderAvailabilityStates.Unavailable,
                null,
                selection.Detail);
        }

        return await selection.Provider.GetCatalogAsync(expedition.CampaignId, cancellationToken);
    }

    public async Task<TravelEnvironmentProviderResolutionResult> ResolveAsync(
        Guid expeditionId,
        string ownerUserId,
        string mechanicKey,
        TravelEnvironmentResolutionRequest request,
        CancellationToken cancellationToken = default,
        string? providerKey = null)
    {
        var expedition = await coreService.GetExpeditionAsync(
            expeditionId,
            ownerUserId,
            cancellationToken);
        var selection = providers.Select(providerKey);
        if (selection.Provider is null)
        {
            return new TravelEnvironmentProviderResolutionResult(
                null,
                mechanicKey,
                TravelEnvironmentProviderResolutionStates.Unavailable,
                null,
                [],
                selection.Detail);
        }

        return await selection.Provider.ResolveAsync(
            expedition.CampaignId,
            mechanicKey,
            request,
            cancellationToken);
    }

    public async Task<ExpeditionRulesScopeView> UpdateCampaignScopeAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        Guid? campaignId,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(
            expeditionId,
            ownerUserId,
            cancellationToken);
        if (expedition.Version != expectedVersion)
        {
            throw new HexCrawlConcurrencyException(
                "The crawl session changed before its campaign rules scope was updated. Reload it and try again.");
        }

        if (expedition.CampaignId == campaignId)
        {
            return new ExpeditionRulesScopeView(campaignId, expedition.Version);
        }

        var save = await store.SaveExpeditionAsync(
            expedition with { CampaignId = campaignId },
            expectedVersion,
            cancellationToken);
        var saved = save.Outcome switch
        {
            SaveOutcome.Saved => save.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The crawl session changed while its campaign rules scope was being updated. Reload it and try again."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };

        return new ExpeditionRulesScopeView(saved.CampaignId, saved.Version);
    }
}
