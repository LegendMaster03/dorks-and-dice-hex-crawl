using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed record ProcedureCanonicalValidationResult(
    bool IsValid,
    CampaignProcedure? Procedure,
    string? Error,
    long? LineNumber,
    long? BytePositionInLine);

public sealed class ProcedureCanonicalJsonService(
    CampaignProcedureService procedures,
    ProcedureComposerService composer)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<string> CreateDraftJsonAsync(
        string ownerUserId,
        string? presetKey,
        Guid? procedureId,
        int? revision,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        var draft = await composer.CreateDraftAsync(
            ownerUserId,
            presetKey,
            procedureId,
            revision,
            overrides,
            cancellationToken);
        return Serialize(draft.Procedure);
    }

    public ProcedureCanonicalValidationResult Validate(string canonicalJson)
    {
        if (string.IsNullOrWhiteSpace(canonicalJson))
        {
            return new ProcedureCanonicalValidationResult(
                false,
                null,
                "Canonical procedure JSON is required.",
                null,
                null);
        }

        try
        {
            var procedure = Deserialize(canonicalJson);
            procedure.Validate();
            return new ProcedureCanonicalValidationResult(true, procedure, null, null, null);
        }
        catch (JsonException exception)
        {
            return new ProcedureCanonicalValidationResult(
                false,
                null,
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Canonical procedure JSON is not valid JSON."
                    : exception.Message,
                exception.LineNumber,
                exception.BytePositionInLine);
        }
        catch (InvalidOperationException exception)
        {
            return new ProcedureCanonicalValidationResult(
                false,
                null,
                exception.Message,
                null,
                null);
        }
    }

    public async Task<StoredCampaignProcedureRevision> CreateAsync(
        string ownerUserId,
        string canonicalJson,
        string? presetKey,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default)
    {
        var procedure = RequireValid(canonicalJson);
        if (procedure.Revision != 1)
        {
            throw new InvalidOperationException("A new canonical procedure must begin at revision 1.");
        }

        ProcedureOriginMetadata? origin = null;
        if (!string.IsNullOrWhiteSpace(presetKey))
        {
            origin = CrawlProcedureCatalog.Resolve(presetKey).Origin;
        }

        return await procedures.CreateAsync(
            ownerUserId,
            procedure,
            origin,
            campaignId,
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> CreateRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
        string canonicalJson,
        CancellationToken cancellationToken = default)
    {
        var procedure = RequireValid(canonicalJson);
        return await procedures.CreateCanonicalRevisionAsync(
            ownerUserId,
            procedureId,
            expectedRevision,
            procedure,
            cancellationToken);
    }

    public string Serialize(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return JsonSerializer.Serialize(procedure, JsonOptions);
    }

    private static CampaignProcedure RequireValid(string canonicalJson)
    {
        if (string.IsNullOrWhiteSpace(canonicalJson))
        {
            throw new InvalidOperationException("Canonical procedure JSON is required.");
        }

        CampaignProcedure procedure;
        try
        {
            procedure = Deserialize(canonicalJson);
        }
        catch (JsonException exception)
        {
            var location = exception.LineNumber.HasValue
                ? $" at line {exception.LineNumber.Value + 1}, byte {exception.BytePositionInLine.GetValueOrDefault() + 1}"
                : string.Empty;
            throw new InvalidOperationException($"Canonical procedure JSON is invalid{location}: {exception.Message}", exception);
        }

        procedure.Validate();
        return procedure;
    }

    private static CampaignProcedure Deserialize(string canonicalJson) =>
        JsonSerializer.Deserialize<CampaignProcedure>(canonicalJson, JsonOptions)
        ?? throw new JsonException("Canonical procedure JSON was empty.");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
