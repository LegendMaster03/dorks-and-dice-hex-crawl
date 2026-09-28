using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

/// <summary>
/// Informational provenance describing the creation-time preset from which a procedure was materialized.
/// Runtime execution must not depend on this metadata.
/// </summary>
public sealed record ProcedureOriginMetadata(
    string? PresetKey = null,
    string? PresetDisplayName = null,
    int? PresetRevision = null);

/// <summary>
/// Creation-time catalog recipe. Named identity remains catalog metadata; the recipe contains only generic module/mechanic selections.
/// </summary>
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

    // Compatibility surface retained for existing callers while generic procedures become the creation-time authority.
    public CrawlProcedureProfile ExecutableProcedureTemplate => Materialize();

    public MaterializedCampaignProcedure MaterializeGeneric(
        CrawlProcedureProfile? customizedProcedure = null,
        Guid? procedureId = null) =>
        CampaignProcedureMaterializer.Materialize(this, customizedProcedure, procedureId);

    public CrawlProcedureProfile Materialize(CrawlProcedureProfile? customizedProcedure = null) =>
        MaterializeGeneric(customizedProcedure).CompatibilityProfile;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PresetKey))
        {
            throw new InvalidOperationException("A crawl procedure preset requires a key.");
        }
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException("A crawl procedure preset requires a display name.");
        }
        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new InvalidOperationException("A crawl procedure preset requires a description.");
        }
        if (PresetRevision <= 0)
        {
            throw new InvalidOperationException("A crawl procedure preset revision must be positive.");
        }
        if (string.IsNullOrWhiteSpace(Recipe.DefaultProcedureKey) || string.IsNullOrWhiteSpace(Recipe.DefaultProcedureName))
        {
            throw new InvalidOperationException("A crawl procedure preset recipe requires default campaign-owned procedure identity.");
        }
        if (Recipe.ModuleSelections.Count == 0)
        {
            throw new InvalidOperationException("A crawl procedure preset recipe requires generic module selections.");
        }
        var materialized = CampaignProcedureMaterializer.Materialize(this);
        materialized.Procedure.Validate();
        materialized.CompatibilityProfile.Validate();
    }
}
