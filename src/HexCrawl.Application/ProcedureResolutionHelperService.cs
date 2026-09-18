using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public interface IProcedureResolutionRandomSource
{
    int NextInt32(int minInclusive, int maxExclusive);
}

public sealed record ProcedureResolutionHelperCommand
{
    public long ExpectedVersion { get; init; }
    public double? ExpectedDistance { get; init; }
    public bool SuppressesNavigationCheck { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public int? NavigationDifficultyClass { get; init; }
    public int NavigationModifier { get; init; }
    public int? FailureVeerSteps { get; init; }
    public Guid? KeyedLocationId { get; init; }
}

public sealed record ProcedureResolutionRoll(
    string Purpose,
    string Formula,
    IReadOnlyList<int> Dice,
    int Modifier,
    int Total);

public sealed record ProcedureResolvedTravel(
    double ExpectedDistance,
    double ActualDistance,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolvedNavigation(
    NavigationCheckOutcome Outcome,
    int? VeerSteps,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolvedEncounter(
    EncounterOutcomeKind Kind,
    double? OccursAtHours,
    Guid? LocationId,
    string? Note,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolutionHelperResult(
    long ExpeditionVersion,
    long? AuditSequence,
    ProcedureResolvedTravel? Travel,
    ProcedureResolvedNavigation? Navigation,
    ProcedureResolvedEncounter? Encounter,
    IReadOnlyList<ProcedureResolutionRoll> Rolls,
    IReadOnlyList<string> Notes);

public sealed class ProcedureResolutionResolver(IProcedureResolutionRandomSource random)
{
    public ProcedureResolutionHelperResult Resolve(
        CrawlProcedureProfile profile,
        CrawlSessionContext context,
        CrawlSessionRuntimeState runtime,
        long expeditionVersion,
        ProcedureResolutionHelperCommand command)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(command);

        profile.Validate();
        var configured = profile.ResolutionHelpers;
        var rolls = new List<ProcedureResolutionRoll>();
        var notes = new List<string>();
        ProcedureResolvedTravel? travel = null;
        ProcedureResolvedNavigation? navigation = null;
        ProcedureResolvedEncounter? encounter = null;

        if (configured is null)
        {
            notes.Add("This procedure snapshot does not configure automatic resolution helpers.");
            return new ProcedureResolutionHelperResult(expeditionVersion, null, null, null, null, rolls, notes);
        }

        if (runtime is ExpeditionState spatial)
        {
            travel = ResolveTravel(profile, configured.Travel, command, rolls, notes);
            navigation = ResolveNavigation(profile, spatial, configured.Navigation, command, rolls, notes);
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

        encounter = ResolveEncounter(profile, context, runtime, configured.Encounter, command, rolls, notes);

        return new ProcedureResolutionHelperResult(
            expeditionVersion,
            null,
            travel,
            navigation,
            encounter,
            rolls,
            notes);
    }

    private ProcedureResolvedTravel? ResolveTravel(
        CrawlProcedureProfile profile,
        TravelResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null)
        {
            return null;
        }
        if (profile.TravelResolution != TravelResolutionMode.ContinuousDistance
            || profile.ActualDistanceResolution != ActualDistanceResolutionMode.VariableResolved)
        {
            notes.Add("The configured travel helper is not applicable to the active travel-resolution mode.");
            return null;
        }

        var expected = command.ExpectedDistance
            ?? throw new InvalidOperationException("The travel helper requires the DM-confirmed expected distance for this watch segment.");
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
        return new ProcedureResolvedTravel(
            expected,
            actual,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
    }

    private ProcedureResolvedNavigation? ResolveNavigation(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        NavigationResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null
            || !ExpeditionProcedureRequirements.IsNavigationResolutionPotentiallyRequired(profile, state)
            || command.SuppressesNavigationCheck
            || command.DeliberateDoubleBack)
        {
            return null;
        }

        var difficultyClass = command.NavigationDifficultyClass
            ?? throw new InvalidOperationException("The navigation helper requires a DM-confirmed navigation difficulty class.");
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
        CrawlProcedureProfile profile,
        CrawlSessionContext context,
        CrawlSessionRuntimeState runtime,
        EncounterResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null || !ExpeditionProcedureRequirements.IsEncounterCheckDue(profile, runtime))
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
            occursAtHours = profile.WatchLength.TotalHours * slot / helper.TimingSlots;
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

public sealed class ProcedureResolutionHelperService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    ProcedureResolutionResolver resolver)
{
    public async Task<ProcedureResolutionHelperResult> ResolveAsync(
        Guid expeditionId,
        string ownerUserId,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        if (command.ExpectedVersion != expedition.Version)
        {
            throw new HexCrawlConcurrencyException(
                "The crawl session changed before procedure inputs were resolved. Reload it before generating another helper result.");
        }

        var generated = resolver.Resolve(
            expedition.Procedure,
            expedition.Context,
            expedition.Runtime,
            expedition.Version,
            command);

        // A no-op helper request does not manufacture an audit mutation. Any
        // consequential automatic generation is persisted immediately so it
        // can not be silently discarded and rerolled.
        if (generated.Rolls.Count == 0
            && generated.Travel is null
            && generated.Navigation is null
            && generated.Encounter is null)
        {
            return generated;
        }

        var sequence = expedition.Runtime.History.Count == 0
            ? 1
            : expedition.Runtime.History[^1].Sequence + 1;
        var audited = AddAuditReference(generated, sequence);
        var auditEvent = BuildAuditEvent(expedition.Runtime, sequence, audited);
        var runtime = AppendAuditEvent(expedition.Runtime, auditEvent);
        var save = await store.SaveExpeditionAsync(
            expedition with { Runtime = runtime },
            command.ExpectedVersion,
            cancellationToken);

        var saved = save.Outcome switch
        {
            SaveOutcome.Saved => save.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The crawl session changed while procedure inputs were being recorded. Reload it before generating another helper result."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };

        return audited with { ExpeditionVersion = saved.Version };
    }

    private static ProcedureResolutionHelperResult AddAuditReference(
        ProcedureResolutionHelperResult generated,
        long sequence)
    {
        ResolutionProvenance Reference(ResolutionProvenance provenance)
        {
            var suffix = $"Generated by procedure-resolution audit event #{sequence}.";
            var note = string.IsNullOrWhiteSpace(provenance.Note)
                ? suffix
                : $"{provenance.Note.Trim()} {suffix}";
            return provenance with { Note = note };
        }

        return generated with
        {
            AuditSequence = sequence,
            Travel = generated.Travel is { } travel
                ? travel with { Provenance = Reference(travel.Provenance) }
                : null,
            Navigation = generated.Navigation is { } navigation
                ? navigation with { Provenance = Reference(navigation.Provenance) }
                : null,
            Encounter = generated.Encounter is { } encounter
                ? encounter with { Provenance = Reference(encounter.Provenance) }
                : null
        };
    }

    private static CrawlRuntimeEvent BuildAuditEvent(
        CrawlSessionRuntimeState runtime,
        long sequence,
        ProcedureResolutionHelperResult result)
    {
        var watchNumber = runtime switch
        {
            ExpeditionState spatial => spatial.ActiveWatch?.WatchNumber ?? spatial.CompletedWatches + 1,
            NonSpatialSessionState nonSpatial => nonSpatial.ActiveWatch?.WatchNumber ?? nonSpatial.CompletedWatches + 1,
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };
        var elapsed = runtime switch
        {
            ExpeditionState spatial => spatial.ElapsedTravelTime,
            NonSpatialSessionState nonSpatial => nonSpatial.ElapsedTime,
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };
        var hex = runtime is ExpeditionState spatialState ? spatialState.CurrentHex : null;

        var parts = new List<string>();
        if (result.Rolls.Count > 0)
        {
            parts.Add("rolls=" + string.Join(", ", result.Rolls.Select(Describe)));
        }
        if (result.Travel is { } travel)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"travel expected={travel.ExpectedDistance:0.###}, actual={travel.ActualDistance:0.###}"));
        }
        if (result.Navigation is { } navigation)
        {
            parts.Add($"navigation={navigation.Outcome}"
                + (navigation.VeerSteps.HasValue ? $", veer={navigation.VeerSteps.Value}" : ""));
        }
        if (result.Encounter is { } encounter)
        {
            var encounterDescription = $"encounter={encounter.Kind}";
            if (encounter.OccursAtHours.HasValue)
            {
                encounterDescription += string.Create(
                    CultureInfo.InvariantCulture,
                    $", at={encounter.OccursAtHours.Value:0.###}h");
            }
            if (encounter.LocationId.HasValue)
            {
                encounterDescription += $", location={encounter.LocationId.Value:D}";
            }
            parts.Add(encounterDescription);
        }
        if (result.Notes.Count > 0)
        {
            parts.Add("notes=" + string.Join(" | ", result.Notes));
        }

        return new CrawlRuntimeEvent(
            sequence,
            Math.Max(1, watchNumber),
            CrawlRuntimeEventKind.ProcedureResolutionHelperGenerated,
            elapsed,
            hex,
            $"Procedure resolution helper attempt #{sequence}: {string.Join("; ", parts)}.");
    }

    private static CrawlSessionRuntimeState AppendAuditEvent(
        CrawlSessionRuntimeState runtime,
        CrawlRuntimeEvent auditEvent)
    {
        var history = runtime.History.Concat([auditEvent]).ToArray();
        return runtime switch
        {
            ExpeditionState spatial => spatial with { History = history },
            NonSpatialSessionState nonSpatial => nonSpatial with { History = history },
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };
    }
}
