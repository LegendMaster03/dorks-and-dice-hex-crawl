using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record ResolvedCrawlSessionContext(
    CrawlRuntimeContext? RuntimeContext,
    StoredOverworld? World)
{
    public bool IsSpatial => RuntimeContext is not null;
    public bool IsWorldBound => World is not null;
}

public sealed class CrawlSessionContextResolver(HexCrawlService coreService)
{
    public async Task<ResolvedCrawlSessionContext> ResolveAsync(
        StoredExpedition session,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return session.Context switch
        {
            WorldBoundCrawlSessionContext worldContext => await ResolveWorldAsync(
                worldContext, ownerUserId, cancellationToken),
            AbstractHexCrawlSessionContext abstractContext => ResolveAbstract(abstractContext),
            NonSpatialCrawlSessionContext nonSpatialContext => ResolveNonSpatial(nonSpatialContext),
            _ => throw new InvalidOperationException("Unsupported crawl session context.")
        };
    }

    private async Task<ResolvedCrawlSessionContext> ResolveWorldAsync(
        WorldBoundCrawlSessionContext context,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var world = await coreService.GetOverworldAsync(context.WorldId, ownerUserId, cancellationToken);
        return new ResolvedCrawlSessionContext(
            ExpeditionWorldComposition.RuntimeContext(world.World),
            world);
    }

    private static ResolvedCrawlSessionContext ResolveAbstract(AbstractHexCrawlSessionContext context)
    {
        context.Validate();
        return new ResolvedCrawlSessionContext(context.HexContext, null);
    }

    private static ResolvedCrawlSessionContext ResolveNonSpatial(NonSpatialCrawlSessionContext context)
    {
        context.Validate();
        return new ResolvedCrawlSessionContext(null, null);
    }
}
