using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Modules.Procedures;

public sealed record SavedProcedureSummaryContract(
    Guid ProcedureId,
    int Revision,
    string Key,
    string Name,
    Guid? CampaignId,
    string? OriginPresetKey,
    string? OriginPresetDisplayName,
    bool IsExecutable,
    int ModuleCount,
    DateTimeOffset CreatedAt)
{
    public static SavedProcedureSummaryContract From(StoredCampaignProcedureRevision stored)
    {
        var executable = false;
        try
        {
            _ = GenericProcedureRuntime.Bind(stored.Procedure);
            executable = true;
        }
        catch (InvalidOperationException)
        {
            // Incomplete/declarative procedures remain listable and editable.
        }

        return new SavedProcedureSummaryContract(
            stored.ProcedureId,
            stored.Revision,
            stored.Procedure.Key,
            stored.Procedure.Name,
            stored.CampaignId,
            stored.ProcedureOrigin?.PresetKey,
            stored.ProcedureOrigin?.PresetDisplayName,
            executable,
            stored.Procedure.Modules.Count,
            stored.CreatedAt);
    }
}
