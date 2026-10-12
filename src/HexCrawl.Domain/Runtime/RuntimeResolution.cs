using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum ResolutionSource
{
    ProcedureDefault,
    AutomaticRoll,
    ManualRoll,
    ExternalSystem,
    DmOverride
}

public sealed record ResolutionProvenance(ResolutionSource Source, string? Note = null)
{
    public static ResolutionProvenance ProcedureDefault { get; } = new(ResolutionSource.ProcedureDefault);
}

public sealed record TravelModeSelection(
    string PaceKey,
    IReadOnlyList<ParticipantActivityAssignment> ActivityAssignments)
{
    public static TravelModeSelection Normal { get; } = new("normal", []);
}

public sealed record NavigationAidSelection(
    string Key,
    bool SuppressesNavigationCheck = false,
    bool ResetsVeerAtBoundary = false)
{
    public static NavigationAidSelection None { get; } = new("none");
}

public sealed record ResolvedTravelAmount
{
    public DistanceMeasure? ExpectedDistance { get; init; }
    public DistanceMeasure? ActualDistance { get; init; }
    public int? CellSteps { get; init; }
    public required ResolutionProvenance Provenance { get; init; }

    public static ResolvedTravelAmount Distance(
        DistanceMeasure expected,
        DistanceMeasure actual,
        ResolutionProvenance provenance) => new()
    {
        ExpectedDistance = expected,
        ActualDistance = actual,
        Provenance = provenance
    };

    public static ResolvedTravelAmount CellTransitions(int steps, ResolutionProvenance provenance)
    {
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));
        return new ResolvedTravelAmount { CellSteps = steps, Provenance = provenance };
    }

}

public enum NavigationCheckOutcome
{
    NotRequired,
    Succeeded,
    Failed
}

public enum EncounterOutcomeKind
{
    None,
    WanderingEncounter,
    KeyedLocationDiscovery,
    ManualCustom
}

public sealed record ResolvedEncounter(
    EncounterOutcomeKind Kind,
    TimeSpan? OccursAt,
    Guid? LocationId,
    string? Note,
    ResolutionProvenance Provenance)
{
    public static ResolvedEncounter None { get; } = new(
        EncounterOutcomeKind.None,
        null,
        null,
        null,
        ResolutionProvenance.ProcedureDefault);
}

public sealed record BoundaryNavigationDecision(
    bool RecognizedLost,
    bool Reorient,
    ResolutionProvenance Provenance);

public enum RuntimePauseReason
{
    ConditionsReviewRequired,
    LostRecognitionRequired,
    EncounterTriggered,
    BacktrackBoundaryReached,
    CellCourseAdjudicationRequired
}
