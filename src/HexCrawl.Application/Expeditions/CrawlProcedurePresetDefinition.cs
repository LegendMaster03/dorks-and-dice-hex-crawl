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
/// Creation-time catalog recipe. The executable template is materialized into campaign-owned procedure state.
/// </summary>
public sealed record CrawlProcedurePresetDefinition(
    string PresetKey,
    string DisplayName,
    string Description,
    int PresetRevision,
    CrawlProcedureProfile ExecutableProcedureTemplate,
    string? Attribution = null,
    string? Disclaimer = null)
{
    public ProcedureOriginMetadata Origin => new(PresetKey, DisplayName, PresetRevision);

    public CrawlProcedureProfile Materialize(CrawlProcedureProfile? customizedProcedure = null)
    {
        var materialized = customizedProcedure ?? ExecutableProcedureTemplate with { };
        materialized.Validate();
        return materialized;
    }

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

        ExecutableProcedureTemplate.Validate();
    }
}
