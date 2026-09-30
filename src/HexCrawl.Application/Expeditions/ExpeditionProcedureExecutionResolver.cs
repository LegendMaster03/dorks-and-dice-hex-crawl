using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public static class ExpeditionProcedureExecutionResolver
{
    public static GenericProcedureRuntime Resolve(StoredExpedition expedition)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        return GenericProcedureRuntime.Bind(expedition.CampaignProcedure);
    }

    public static WatchAdvanceResult Advance(
        CrawlRuntimeEngine engine,
        StoredExpedition expedition,
        CrawlRuntimeContext context,
        ExpeditionState state,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(expedition);
        return engine.Advance(context, expedition.CampaignProcedure, state, plan, inputs);
    }
}
