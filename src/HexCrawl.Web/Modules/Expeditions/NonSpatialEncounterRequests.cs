using HexCrawl.Application;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Api;

/// <summary>
/// Nonspatial encounter operations share event/audit services but never accept
/// V1 spatial runtime state or fabricate coordinates. Cell encounters use the
/// geometry-authoritative /cells/encounters route instead.
/// </summary>
public sealed record RecordNonSpatialEncounterRequest(
    long ExpectedVersion,
    EncounterOutcomeKind Outcome,
    ResolutionSource ResolutionSource = ResolutionSource.ManualRoll,
    string? ResolutionNote = null,
    string? Note = null)
{
    public void Validate()
    {
        if (ExpectedVersion <= 0)
            throw new InvalidOperationException("Expected expedition version is required.");
        if (ResolutionNote is { Length: > 2000 } || Note is { Length: > 2000 })
            throw new InvalidOperationException("Encounter notes exceed 2000 characters.");
    }

    public EncounterCadenceAssistantCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        Outcome = Outcome,
        ResolutionSource = ResolutionSource,
        ResolutionNote = ResolutionNote,
        Note = Note
    };
}

public sealed record ResolveNonSpatialEncounterRequest(
    long ExpectedVersion,
    ResolutionSource ResolutionSource = ResolutionSource.DmOverride,
    string? ResolutionNote = null,
    string? ResultNote = null)
{
    public void Validate()
    {
        if (ExpectedVersion <= 0)
            throw new InvalidOperationException("Expected expedition version is required.");
        if (ResolutionNote is { Length: > 2000 } || ResultNote is { Length: > 2000 })
            throw new InvalidOperationException("Encounter notes exceed 2000 characters.");
    }

    public ResolveEncounterCommand ToCommand(Guid occurrenceId) => new(
        ExpectedVersion, occurrenceId, ResolutionSource, ResolutionNote, ResultNote);
}
