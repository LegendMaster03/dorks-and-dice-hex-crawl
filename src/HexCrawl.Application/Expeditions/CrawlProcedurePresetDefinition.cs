using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed record ProcedureOriginMetadata(
    string? PresetKey = null,
    string? PresetDisplayName = null,
    int? PresetRevision = null);

public sealed record CrawlProcedurePresetDefinition(
    string PresetKey,
    string DisplayName,
    string Description,
    int PresetRevision,
    GenericProcedurePresetRecipe Recipe,
    string? Attribution = null,
    string? Disclaimer = null)
{
    public ProcedureOriginMetadata Origin => new(PresetKey, DisplayName, PresetRevision);

    public MaterializedCampaignProcedure MaterializeGeneric(Guid? procedureId = null) =>
        CampaignProcedureMaterializer.Materialize(this, procedureId);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PresetKey)
            || string.IsNullOrWhiteSpace(DisplayName)
            || string.IsNullOrWhiteSpace(Description)
            || PresetRevision <= 0)
        {
            throw new InvalidOperationException("A crawl procedure preset requires a key, display name, description, and positive revision.");
        }
        ArgumentNullException.ThrowIfNull(Recipe);
        if (string.IsNullOrWhiteSpace(Recipe.DefaultProcedureKey)
            || string.IsNullOrWhiteSpace(Recipe.DefaultProcedureName)
            || Recipe.ModuleSelections.Count == 0)
        {
            throw new InvalidOperationException($"Crawl procedure preset '{PresetKey}' requires a complete generic recipe identity and at least one module selection.");
        }

        var materialized = CampaignProcedureMaterializer.Materialize(this);
        materialized.Procedure.Validate();
    }
}
