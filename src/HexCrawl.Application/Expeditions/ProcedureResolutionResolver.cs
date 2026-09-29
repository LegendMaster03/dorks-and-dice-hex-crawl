using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed class ProcedureResolutionResolver(IProcedureResolutionRandomSource random)
{
    public ProcedureResolutionHelperResult Resolve(
        GenericProcedureRuntime procedure,
        CrawlSessionContext context,
        CrawlSessionRuntimeState runtime,
        long expeditionVersion,
        ProcedureResolutionHelperCommand command)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(command);

        var configured = procedure.ResolutionHelpers;
        var rolls = new List<ProcedureResolutionRoll>();
        var notes = new List<string>();
        ProcedureResolvedTravel? travel = null;
        ProcedureResolvedNavigation? navigation = null;
        ProcedureResolvedEncounter? encounter = null;

        if (configured is null)
        {
            notes.Add("This procedure snapshot does not configure automatic resolution helpers.");
            return new ProcedureResolutionHelperResult(expeditionVersion, null, null, null, null, null, rolls, notes);
        }

        if (runtime is ExpeditionState spatial)
        {
            travel = ResolveTravel(procedure, configured.Travel, command, rolls, notes);
            navigation = ResolveNavigation(procedure, spatial, configured.Navigation, command, rolls, notes);
        }
        else
        {
            if (configured.Travel is not null)
            {
                notes.Add("Travel resolution was not generated because this session is non-spatial.");
            }
            if (configured.Navigation is not null)
            {
                notes.Add("Navigation resolution was not generated because this session is non-spatial.");
            }
        }

        encounter = ResolveEncounter(procedure, context, runtime, configured.Encounter, command, rolls, notes);

        return new ProcedureResolutionHelperResult(
            expeditionVersion,
            null,
            null,
            travel,
            navigation,
            encounter,
            rolls,
            notes);
    }

    private ProcedureResolvedTravel? ResolveTravel(
        GenericProcedureRuntime procedure,
        TravelResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null)
        {
            return null;
        }
        if (procedure.Movement.TravelResolution != TravelResolutionMode.ContinuousDistance
            || procedure.Movement.ActualDistanceResolution != ActualDistanceResolutionMode.VariableResolved)
        {
            notes.Add("The configured travel helper is not applicable to the active travel-resolution mode.");
            return null;
        }

        var expected = command.ExpectedDistance
            ?? throw new InvalidOperationException(
                "The travel helper requires an explicit DM expected distance or sufficient source-backed travel inputs.");
        if (!double.IsFinite(expected) || expected < 0)
        {
            throw new InvalidOperationException("Expected travel distance must be finite and non-negative.");
        }

        var roll = Roll("travel-distance", helper.Roll, rolls);
        var actual = expected * roll.Total * helper.DistanceFactorPerRollPoint;
        if (!double.IsFinite(actual) || actual < 0)
        {
            throw new InvalidOperationException("The configured travel helper produced an invalid distance.");
        }

        var note = string.Create(
            CultureInfo.InvariantCulture,
            $"Automatic travel helper: {Describe(roll)}; expected={expected:0.###}; factor={helper.DistanceFactorPerRollPoint:0.###}; actual={actual:0.###}.");
        if (!string.IsNullOrWhiteSpace(command.ExpectedDistanceRulesNote))
        {
            note += " " + command.ExpectedDistanceRulesNote.Trim();
        }
        return new ProcedureResolvedTravel(
            expected,
            actual,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
    }

    private ProcedureResolvedNavigation? ResolveNavigation(
        GenericProcedureRuntime procedure,
        ExpeditionState state,
        NavigationResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null
            || !ExpeditionProcedureRequirements.IsNavigationResolutionPotentiallyRequired(procedure, state)
            || command.SuppressesNavigationCheck
            || command.DeliberateDoubleBack)
        {
            return null;
        }

        var difficultyClass = command.NavigationDifficultyClass
            ?? throw new InvalidOperationException(
                "The navigation helper requires an explicit DM DC or sufficient source-backed navigation inputs.");
        var roll = Roll("navigation-check", helper.CheckRoll, rolls);
        var resolvedTotal = checked(roll.Total + command.NavigationModifier);
        var outcome = resolvedTotal >= difficultyClass
            ? NavigationCheckOutcome.Succeeded
            : NavigationCheckOutcome.Failed;
        int? veer = null;
        if (outcome == NavigationCheckOutcome.Failed)
        {
            veer = command.FailureVeerSteps
                ?? throw new InvalidOperationException("A failed navigation helper result requires a DM-confirmed non-zero failure veer.");
            if (veer == 0)
            {
                throw new InvalidOperationException("A failed navigation helper result requires a non-zero failure veer.");
            }
        }

        var note = $"Automatic navigation helper: {Describe(roll)}; situational modifier={command.NavigationModifier}; total={resolvedTotal}; DC={difficultyClass}.";
        if (!string.IsNullOrWhiteSpace(command.NavigationDifficultyRulesNote))
        {
            note += " " + command.NavigationDifficultyRulesNote.Trim();
        }
        if (veer.HasValue)
        {
            note += $" DM-confirmed failure veer={veer.Value}.";
        }
        return new ProcedureResolvedNavigation(
            outcome,
            veer,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
    }

    private ProcedureResolvedEncounter? ResolveEncounter(
        GenericProcedureRuntime procedure,
        CrawlSessionContext context,
        CrawlSessionRuntimeState runtime,
        EncounterResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
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
                locationId = command.KeyedLocationId;
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

        return new ProcedureResolvedEncounter(
            kind,
            occursAtHours,
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
