using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
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
        if (expedition.CampaignProcedure is { } procedure)
        {
            return GenericProcedureRuntime.Bind(procedure);
        }

        return GenericProcedureRuntime.FromLegacyProfile(RequireLegacyProfile(expedition));
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
            : engine.Advance(context, RequireLegacyProfile(expedition), state, plan, inputs);
    }

    private static CrawlProcedureProfile RequireLegacyProfile(StoredExpedition expedition) =>
        expedition.Procedure
        ?? throw new InvalidOperationException(
            "This expedition has no generic CampaignProcedure and no historical CrawlProcedureProfile compatibility snapshot to execute.");
}
