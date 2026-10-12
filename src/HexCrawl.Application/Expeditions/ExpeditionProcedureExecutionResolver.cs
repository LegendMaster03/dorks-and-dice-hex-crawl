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

}
