using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// Deliberate compatibility boundary between native generic execution and historical profile-only data.
/// New expeditions execute their pinned CampaignProcedure. CrawlProcedureProfile is consulted only
/// when an old persisted expedition has no generic snapshot.
/// </summary>
public static class ExpeditionProcedureExecutionResolver
{
    public static GenericProcedureRuntime Resolve(StoredExpedition expedition)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        return expedition.CampaignProcedure is { } procedure
            ? GenericProcedureRuntime.Bind(procedure)
            : GenericProcedureRuntime.FromLegacyProfile(expedition.Procedure);
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
        return expedition.CampaignProcedure is { } procedure
            ? engine.Advance(context, procedure, state, plan, inputs)
            : engine.Advance(context, expedition.Procedure, state, plan, inputs);
    }
}
