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

public sealed record TravelModeSelection(string PaceKey, IReadOnlyList<string> Activities)
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

public sealed record WatchTravelPlan(
    HexDirection IntendedDirection,
    TravelModeSelection Mode,
    NavigationAidSelection NavigationAid,
    bool DeliberateDoubleBack = false,
    bool ContinueAcrossBoundaries = false);

public sealed record ResolvedTravelAmount
{
    public DistanceMeasure? ExpectedDistance { get; init; }
    public DistanceMeasure? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
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

    public static ResolvedTravelAmount Steps(int steps, ResolutionProvenance provenance)
    {
        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }

        return new ResolvedTravelAmount
        {
            HexSteps = steps,
            Provenance = provenance
        };
    }
}

public enum NavigationCheckOutcome
{
    NotRequired,
    Succeeded,
    Failed
}

public sealed record ResolvedNavigation(
    NavigationCheckOutcome Outcome,
    int? VeerStepsOnFailure,
    ResolutionProvenance Provenance)
{
    public static ResolvedNavigation NotRequired { get; } = new(
        NavigationCheckOutcome.NotRequired,
        null,
        ResolutionProvenance.ProcedureDefault);
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

public sealed record WatchAdvanceInputs(
    ResolvedTravelAmount Travel,
    ResolvedNavigation? Navigation = null,
    ResolvedEncounter? Encounter = null,
    BoundaryNavigationDecision? BoundaryDecision = null,
    string? DmOverrideNote = null);

public enum RuntimePauseReason
{
    ConditionsReviewRequired,
    LostRecognitionRequired,
    EncounterTriggered,
    BacktrackBoundaryReached
}

public sealed record ActiveWatchState(
    int WatchNumber,
    TimeSpan TotalDuration,
    TimeSpan Elapsed,
    ResolvedEncounter Encounter,
    bool EncounterHandled,
    RuntimePauseReason? PendingDecision)
{
    public TimeSpan Remaining => TotalDuration > Elapsed ? TotalDuration - Elapsed : TimeSpan.Zero;
}
