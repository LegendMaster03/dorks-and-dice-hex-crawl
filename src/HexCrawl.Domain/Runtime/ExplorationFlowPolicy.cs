namespace HexCrawl.Domain.Runtime;

/// <summary>
/// A reusable exploration process independent of map shape and source system.
/// Stage keys are ordered labels. Actions are resolved by observed location
/// state; the caller supplies visited/rest-site facts from authoritative data.
/// This policy does not create encounters, alter supplies or silently complete
/// manual actions.
/// </summary>
public enum ExplorationDiscoveryTrigger { Manual, OnFirstVisit, OnExplore }
public enum ExplorationEncounterTrigger { Manual, EveryTravel, OnExploreOrOptionalTravel }
public enum ExplorationEncounterRequirement { None, Required, OptionalOrRiskBased }

public sealed record ExplorationFlowPolicy(
    IReadOnlyList<string> StageKeys,
    ExplorationDiscoveryTrigger DiscoveryTrigger,
    ExplorationEncounterTrigger EncounterTrigger,
    string? FirstVisitAction = null,
    string? RepeatVisitAction = null,
    string? RestSiteAction = null,
    string? ActionSelectionStageKey = null,
    string? ActionResolutionStageKey = null)
{
    public void Validate()
    {
        if (StageKeys.Count == 0 || StageKeys.Any(string.IsNullOrWhiteSpace)
            || StageKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != StageKeys.Count)
            throw new InvalidOperationException(
                "An exploration flow requires distinct, nonempty ordered stage keys.");
        if (!Enum.IsDefined(DiscoveryTrigger) || !Enum.IsDefined(EncounterTrigger))
            throw new InvalidOperationException("Unknown exploration flow trigger.");
        foreach (var key in new[] { FirstVisitAction, RepeatVisitAction, RestSiteAction })
            if (key is not null && string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Exploration action keys cannot be blank.");
        if ((ActionSelectionStageKey is null) != (ActionResolutionStageKey is null))
            throw new InvalidOperationException("Action-driven exploration requires both selection and resolution stages.");
        if (ActionSelectionStageKey is not null)
        {
            var selection = Array.FindIndex(StageKeys.ToArray(), value => value == ActionSelectionStageKey);
            var resolution = Array.FindIndex(StageKeys.ToArray(), value => value == ActionResolutionStageKey);
            if (selection < 0 || resolution != selection + 1
                || FirstVisitAction is null || RepeatVisitAction is null)
                throw new InvalidOperationException(
                    "Action-driven exploration requires adjacent configured selection/resolution stages and first/repeat action keys.");
        }
    }

    public IReadOnlyList<string> AvailableActions(bool previouslyVisited, bool restSite)
    {
        var primary = previouslyVisited ? RepeatVisitAction : FirstVisitAction;
        return new[] { primary, restSite ? RestSiteAction : null }
            .Where(key => key is not null)
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool ShouldGenerateLocation(bool previouslyVisited, string? chosenAction)
        => DiscoveryTrigger switch
        {
            ExplorationDiscoveryTrigger.Manual => false,
            ExplorationDiscoveryTrigger.OnFirstVisit => !previouslyVisited,
            ExplorationDiscoveryTrigger.OnExplore =>
                !previouslyVisited && string.Equals(chosenAction, FirstVisitAction,
                    StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException("Unsupported location discovery policy.")
        };

    /// <summary>
    /// Returns true only for mandatory encounters. Optional/risk-based
    /// encounters require an explicit external result, not a fabricated roll.
    /// </summary>
    public bool RequiresEncounterAfterTravel() =>
        EncounterTrigger == ExplorationEncounterTrigger.EveryTravel;

    /// <summary>
    /// Reports what the pinned rule requires before an encounter can be resolved.
    /// No roll, encounter content, or combat state is generated here.
    /// </summary>
    public ExplorationEncounterRequirement EncounterForAction(
        bool previouslyVisited, string? chosenAction)
    {
        if (chosenAction is not null && RestSiteAction is not null
            && string.Equals(chosenAction, RestSiteAction, StringComparison.OrdinalIgnoreCase))
            return ExplorationEncounterRequirement.None;
        return EncounterTrigger switch
        {
            ExplorationEncounterTrigger.Manual => ExplorationEncounterRequirement.None,
            ExplorationEncounterTrigger.EveryTravel => ExplorationEncounterRequirement.Required,
            ExplorationEncounterTrigger.OnExploreOrOptionalTravel
                when !previouslyVisited && FirstVisitAction is not null
                    && string.Equals(chosenAction, FirstVisitAction, StringComparison.OrdinalIgnoreCase)
                => ExplorationEncounterRequirement.Required,
            ExplorationEncounterTrigger.OnExploreOrOptionalTravel
                when previouslyVisited && RepeatVisitAction is not null
                    && string.Equals(chosenAction, RepeatVisitAction, StringComparison.OrdinalIgnoreCase)
                => ExplorationEncounterRequirement.OptionalOrRiskBased,
            _ => ExplorationEncounterRequirement.None
        };
    }
}
