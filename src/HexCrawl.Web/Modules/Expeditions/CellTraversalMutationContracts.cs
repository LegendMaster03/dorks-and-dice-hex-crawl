using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Api;

/// <summary>
/// Explicit versioned API surface for existing cell expeditions. These
/// contracts are intentionally separate from the legacy six-direction route.
/// No generalized expedition creation or browser workbench is enabled here.
/// </summary>
public sealed record AdvanceCellWatchRequest(
    long ExpectedVersion,
    CellWatchTravelPlan Plan,
    CellWatchAdvanceInputs Inputs)
{
    public void Validate()
    {
        if (ExpectedVersion <= 0)
            throw new InvalidOperationException("An expected expedition version is required.");
        ArgumentNullException.ThrowIfNull(Plan);
        ArgumentNullException.ThrowIfNull(Inputs);
        ArgumentNullException.ThrowIfNull(Inputs.Travel);
        ValidateClientProvenance(Inputs.Travel.Provenance);
        if (Inputs.Navigation is { } nav) ValidateClientProvenance(nav.Provenance);
        if (Inputs.Encounter is { } encounter) ValidateClientProvenance(encounter.Provenance);
        if (Inputs.BoundaryDecision is { } boundary) ValidateClientProvenance(boundary.Provenance);
    }

    internal static void ValidateClientProvenance(ResolutionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        if (provenance.Source == ResolutionSource.AutomaticRoll)
            throw new InvalidOperationException(
                "AutomaticRoll is reserved for server-verified resolutions; clients must submit explicit manual provenance.");
        if (!Enum.IsDefined(provenance.Source))
            throw new InvalidOperationException("Unknown resolution provenance source.");
    }
}

public sealed record ResolveCellCourseRequest(
    long ExpectedVersion,
    CellCourseDecision Decision)
{
    public void Validate()
    {
        if (ExpectedVersion <= 0)
            throw new InvalidOperationException("An expected expedition version is required.");
        ArgumentNullException.ThrowIfNull(Decision);
        AdvanceCellWatchRequest.ValidateClientProvenance(Decision.Provenance);
    }
}

public sealed record ResolveCellEncounterRequest(
    long ExpectedVersion,
    string? ResultNote = null)
{
    public void Validate()
    {
        if (ExpectedVersion <= 0)
            throw new InvalidOperationException("An expected expedition version is required.");
        if (ResultNote is { Length: > 2000 })
            throw new InvalidOperationException("Encounter resolution note exceeds 2000 characters.");
    }
}
