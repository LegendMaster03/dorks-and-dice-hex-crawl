namespace HexCrawl.Web.Modules.Procedures;

public sealed record ProcedureCanonicalDraftRequest(
    string? PresetKey,
    Guid? ProcedureId,
    int? Revision,
    IReadOnlyList<ProcedureComposerOverrideRequest>? Overrides);

public sealed record ProcedureCanonicalValidationRequest(string CanonicalJson);

public sealed record ProcedureCanonicalCreateRequest(
    string CanonicalJson,
    string? PresetKey,
    Guid? CampaignId);

public sealed record ProcedureCanonicalRevisionRequest(
    int ExpectedRevision,
    string CanonicalJson);

public sealed record ProcedureCanonicalJsonContract(
    Guid ProcedureId,
    int Revision,
    string CanonicalJson);

public sealed record ProcedureCanonicalValidationContract(
    bool IsValid,
    Guid? ProcedureId,
    int? Revision,
    string? Error,
    long? LineNumber,
    long? BytePositionInLine);
