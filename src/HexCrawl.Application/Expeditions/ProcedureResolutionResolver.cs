using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed class ProcedureResolutionResolver(IProcedureResolutionRandomSource random)
{
    public CellProcedureResolutionHelperResult ResolveCells(
        GenericProcedureRuntime procedure,
        CrawlSessionContext context,
        CellExpeditionState state,
        long expeditionVersion,
        CellProcedureResolutionHelperCommand command)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        var rolls = new List<ProcedureResolutionRoll>();
        var notes = new List<string>();
        var helpers = procedure.ResolutionHelpers;
        if (helpers is null)
            return new(expeditionVersion, null, null, null, null, null, rolls,
                ["The pinned procedure does not configure automatic resolution helpers."]);

        ResolvedTravelAmount? travel = null;
        if (helpers.Travel is { } travelProfile)
        {
            if (procedure.Movement.TravelResolution == TravelResolutionMode.ContinuousDistance
                && procedure.Movement.ActualDistanceResolution == ActualDistanceResolutionMode.VariableResolved)
            {
                var expected = command.ExpectedDistance
                    ?? throw new InvalidOperationException(
                        "A cell travel helper requires an explicitly qualified expected physical distance.");
                if (!double.IsFinite(expected.Value) || expected.Value < 0)
                    throw new InvalidOperationException("Expected distance must be finite and nonnegative.");
                var roll = Roll("cell-travel-distance", travelProfile.Roll, rolls);
                var actual = expected.Value * roll.Total * travelProfile.DistanceFactorPerRollPoint;
                if (!double.IsFinite(actual) || actual < 0)
                    throw new InvalidOperationException("A generated cell travel distance is invalid.");
                var note = string.Create(CultureInfo.InvariantCulture,
                    $"Automatic cell travel: {Describe(roll)}; expected={expected.Value:0.###} {expected.Unit.Symbol}; actual={actual:0.###} {expected.Unit.Symbol}.");
                if (!string.IsNullOrWhiteSpace(command.ExpectedDistanceRulesNote))
                    note += " " + command.ExpectedDistanceRulesNote.Trim();
                travel = ResolvedTravelAmount.Distance(
                    expected, new DistanceMeasure(actual, expected.Unit),
                    new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
            }
            else
            {
                notes.Add("The pinned movement policy does not require a variable physical-distance helper.");
            }
        }

        ResolvedCellNavigation? navigation = null;
        var nextWatch = Math.Max(1, state.CompletedWatches + 1);
        var needsNavigation = helpers.Navigation is not null
            && procedure.Navigation.UsesNavigationChecks
            && state.ActiveWatch is null
            && !state.History.Any(item =>
                item.Kind == CrawlRuntimeEventKind.NavigationCheckResolved
                && item.WatchNumber == nextWatch)
            && !command.SuppressesNavigationCheck
            && !command.DeliberateDoubleBack;
        if (needsNavigation)
        {
            var dc = command.NavigationDifficultyClass
                ?? throw new InvalidOperationException(
                    "Automatic cell navigation requires an explicit DM difficulty class or a qualified provider result.");
            var check = Roll("cell-navigation-check", helpers.Navigation!.CheckRoll, rolls);
            var total = checked(check.Total + command.NavigationModifier);
            var result = total >= dc ? NavigationCheckOutcome.Succeeded : NavigationCheckOutcome.Failed;
            double? veer = null;
            if (result == NavigationCheckOutcome.Failed)
            {
                veer = command.FailureVeerDegrees
                    ?? throw new InvalidOperationException("A failed navigation check requires an explicit angular veer in degrees.");
                if (!double.IsFinite(veer.Value) || veer.Value == 0d || Math.Abs(veer.Value) > 180d)
                    throw new InvalidOperationException("Angular veer must be finite, nonzero, and at most 180 degrees.");
            }
            var note = $"Automatic cell navigation: {Describe(check)}; modifier={command.NavigationModifier}; total={total}; DC={dc}.";
            if (!string.IsNullOrWhiteSpace(command.NavigationDifficultyRulesNote))
                note += " " + command.NavigationDifficultyRulesNote.Trim();
            if (veer.HasValue) note += $" DM-confirmed angular veer={veer.Value} degrees.";
            navigation = new ResolvedCellNavigation(
                result, veer, new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
        }

        ResolvedEncounter? encounter = null;
        if (helpers.Encounter is { } encounterProfile
            && GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state))
        {
            // Encounter cadence and location semantics are shared across
            // geometries. Only traversal and navigation have separate models.
            encounter = ResolveEncounter(procedure, context, state, encounterProfile,
                command.KeyedLocationId, rolls, notes);
        }

        return new(expeditionVersion, null, null, travel, navigation, encounter, rolls, notes);
    }

    private ResolvedEncounter? ResolveEncounter(
        GenericProcedureRuntime procedure,
        CrawlSessionContext context,
        CellExpeditionState runtime,
        EncounterResolutionHelperProfile? helper,
        Guid? keyedLocationId,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null || !ExpeditionProcedureRequirements.IsEncounterCheckDue(procedure, runtime))
        {
            return null;
        }

        var check = Roll("encounter-check", helper.CheckRoll, rolls);
        var kind = helper.WanderingResults.Contains(check.Total)
            ? EncounterOutcomeKind.WanderingEncounter
            : helper.KeyedLocationResults.Contains(check.Total)
                ? EncounterOutcomeKind.KeyedLocationDiscovery
                : EncounterOutcomeKind.None;

        string? encounterNote = null;
        Guid? locationId = null;
        if (kind == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            if (context.Kind == CrawlSessionContextKind.WorldBound)
            {
                locationId = keyedLocationId;
                if (locationId is null)
                {
                    notes.Add("The encounter helper resolved a keyed-location discovery. Select the applicable keyed location before applying the result.");
                }
            }
            else
            {
                kind = EncounterOutcomeKind.ManualCustom;
                encounterNote = "The procedure resolved a keyed-location result, but this session has no world-bound location subject. Resolve the location manually.";
            }
        }

        double? occursAtHours = null;
        ProcedureResolutionRoll? timing = null;
        if (kind != EncounterOutcomeKind.None)
        {
            var slot = random.NextInt32(1, helper.TimingSlots + 1);
            timing = new ProcedureResolutionRoll(
                "encounter-time",
                $"1d{helper.TimingSlots}",
                [slot],
                0,
                slot);
            rolls.Add(timing);
            occursAtHours = procedure.Time.IntervalDuration.TotalHours * slot / helper.TimingSlots;
        }

        var provenanceNote = $"Automatic encounter helper: {Describe(check)}.";
        if (timing is not null)
        {
            provenanceNote += string.Create(
                CultureInfo.InvariantCulture,
                $" Timing {Describe(timing)} => {occursAtHours:0.###}h into the watch.");
        }
        if (encounterNote is not null)
        {
            provenanceNote += $" {encounterNote}";
        }

        return new ResolvedEncounter(
            kind,
            occursAtHours is { } hours ? TimeSpan.FromHours(hours) : null,
            locationId,
            encounterNote,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, provenanceNote));
    }

    private ProcedureResolutionRoll Roll(
        string purpose,
        DiceRollFormula formula,
        List<ProcedureResolutionRoll> rolls)
    {
        var dice = new int[formula.DiceCount];
        var total = formula.Modifier;
        for (var index = 0; index < dice.Length; index++)
        {
            var value = random.NextInt32(1, formula.DieSides + 1);
            dice[index] = value;
            total = checked(total + value);
        }

        var formulaText = formula.Modifier switch
        {
            > 0 => $"{formula.DiceCount}d{formula.DieSides}+{formula.Modifier}",
            < 0 => $"{formula.DiceCount}d{formula.DieSides}{formula.Modifier}",
            _ => $"{formula.DiceCount}d{formula.DieSides}"
        };
        var result = new ProcedureResolutionRoll(purpose, formulaText, dice, formula.Modifier, total);
        rolls.Add(result);
        return result;
    }

    private static string Describe(ProcedureResolutionRoll roll) =>
        $"{roll.Formula} [{string.Join(", ", roll.Dice)}] = {roll.Total}";
}
