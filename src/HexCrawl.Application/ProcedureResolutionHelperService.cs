using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public interface IProcedureResolutionRandomSource
{
    int NextInt32(int minInclusive, int maxExclusive);
}

public enum ProcedureResolutionComponent
{
    All,
    Travel,
    Navigation,
    Encounter
}

public sealed record TravelModifier(
    string Key,
    string Label,
    double Multiplier,
    string Source,
    string? Note = null);

public sealed record TravelResolutionContext(
    double BaseRate,
    DistanceUnit BaseRateUnit,
    double RateDurationHours,
    string TravelModeKey,
    string? TravelModeLabel,
    string PaceKey,
    double? PaceMultiplier = null,
    IReadOnlyList<TravelModifier>? Modifiers = null);

public sealed record TravelCalculationStep(
    string Key,
    string Label,
    double InputValue,
    double Multiplier,
    double OutputValue,
    string UnitSymbol,
    string? Source = null,
    string? Note = null);

public sealed record ProcedureResolutionHelperCommand
{
    public long ExpectedVersion { get; init; }
    public ProcedureResolutionComponent Component { get; init; } = ProcedureResolutionComponent.All;
    public TravelResolutionContext? TravelContext { get; init; }
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
    DistanceUnit Unit,
    double SegmentHours,
    IReadOnlyList<TravelCalculationStep> Calculation,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolvedNavigation(
    NavigationCheckOutcome Outcome,
    int? VeerSteps,
    bool ResultingIsLost,
    int ResultingVeerSteps,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolvedEncounter(
    EncounterOutcomeKind Kind,
    double? OccursAtHours,
    Guid? LocationId,
    string? Note,
    ResolutionProvenance Provenance);

public enum GeneratedProcedureResolutionStatus
{
    Available,
    Superseded,
    Consumed
}

public sealed record GeneratedProcedureResolution(
    Guid Id,
    Guid SessionId,
    long GeneratedFromVersion,
    long GeneratedAtVersion,
    long AuditSequence,
    int WatchNumber,
    IReadOnlyList<ProcedureResolutionRoll> Rolls,
    ProcedureResolvedTravel? Travel,
    ProcedureResolvedNavigation? Navigation,
    ProcedureResolvedEncounter? Encounter,
    GeneratedProcedureResolutionStatus Status,
    long? ConsumedAtVersion = null,
    long? ConsumedAuditSequence = null);

public sealed record ProcedureResolutionHelperResult(
    long ExpeditionVersion,
    Guid? GeneratedResolutionId,
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
            return new ProcedureResolutionHelperResult(expeditionVersion, null, null, null, null, null, rolls, notes);
        }

        var wantsTravel = command.Component is ProcedureResolutionComponent.All or ProcedureResolutionComponent.Travel;
        var wantsNavigation = command.Component is ProcedureResolutionComponent.All or ProcedureResolutionComponent.Navigation;
        var wantsEncounter = command.Component is ProcedureResolutionComponent.All or ProcedureResolutionComponent.Encounter;

        if (runtime is ExpeditionState spatial)
        {
            if (wantsTravel)
            {
                travel = ResolveTravel(profile, spatial, configured.Travel, command, rolls, notes);
            }
            if (wantsNavigation)
            {
                navigation = ResolveNavigation(profile, spatial, configured.Navigation, command, rolls, notes);
            }
        }
        else
        {
            if (wantsTravel && configured.Travel is not null)
            {
                notes.Add("Travel resolution was not generated because this session is non-spatial.");
            }
            if (wantsNavigation && configured.Navigation is not null)
            {
                notes.Add("Navigation resolution was not generated because this session is non-spatial.");
            }
        }

        if (wantsEncounter)
        {
            encounter = ResolveEncounter(profile, context, runtime, configured.Encounter, command, rolls, notes);
        }

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
        CrawlProcedureProfile profile,
        ExpeditionState state,
        TravelResolutionHelperProfile? helper,
        ProcedureResolutionHelperCommand command,
        List<ProcedureResolutionRoll> rolls,
        List<string> notes)
    {
        if (helper is null)
        {
            return null;
        }
        if (profile.TravelResolution != TravelResolutionMode.ContinuousDistance)
        {
            notes.Add("Automatic physical-distance arithmetic is unavailable for a hex-step travel procedure.");
            return null;
        }

        var unit = state.DistanceTraveled.Unit;
        var segment = state.ActiveWatch?.Remaining ?? profile.WatchLength;
        var calculation = new List<TravelCalculationStep>();

        double expected;
        if (helper.SupportsRateArithmetic)
        {
            var travelContext = command.TravelContext
                ?? throw new InvalidOperationException("The travel helper requires a DM-confirmed base travel rate and rate-duration basis.");
            if (!double.IsFinite(travelContext.BaseRate) || travelContext.BaseRate <= 0)
            {
                throw new InvalidOperationException("Base travel rate must be finite and positive.");
            }
            if (!double.IsFinite(travelContext.RateDurationHours) || travelContext.RateDurationHours <= 0)
            {
                throw new InvalidOperationException("Travel rate duration basis must be finite and positive.");
            }

            var baseRate = new DistanceMeasure(travelContext.BaseRate, travelContext.BaseRateUnit).ConvertTo(unit);
            var running = baseRate.Value;
            calculation.Add(new TravelCalculationStep(
                "base-rate",
                $"Base capability per {travelContext.RateDurationHours:0.###}h",
                running,
                1d,
                running,
                unit.Symbol,
                "dm-confirmed",
                string.IsNullOrWhiteSpace(travelContext.TravelModeLabel)
                    ? travelContext.TravelModeKey
                    : travelContext.TravelModeLabel.Trim()));

            var fraction = segment.TotalHours / travelContext.RateDurationHours;
            var afterSegment = running * fraction;
            calculation.Add(new TravelCalculationStep(
                "watch-fraction",
                $"Current segment {segment.TotalHours:0.###}h",
                running,
                fraction,
                afterSegment,
                unit.Symbol,
                "persisted-session"));
            running = afterSegment;

            var configuredPace = helper.PaceDefaults?.Resolve(travelContext.PaceKey);
            var pace = travelContext.PaceMultiplier ?? configuredPace
                ?? throw new InvalidOperationException(
                    $"Pace '{travelContext.PaceKey}' has no procedure-defined multiplier; provide an explicit DM-confirmed pace multiplier.");
            ValidateMultiplier(pace, "Travel pace multiplier");
            var afterPace = running * pace;
            calculation.Add(new TravelCalculationStep(
                "pace",
                $"Pace: {travelContext.PaceKey}",
                running,
                pace,
                afterPace,
                unit.Symbol,
                travelContext.PaceMultiplier.HasValue ? "dm-confirmed" : "procedure"));
            running = afterPace;

            foreach (var modifier in travelContext.Modifiers ?? [])
            {
                if (string.IsNullOrWhiteSpace(modifier.Key) || string.IsNullOrWhiteSpace(modifier.Label))
                {
                    throw new InvalidOperationException("Travel modifiers require a key and label.");
                }
                ValidateMultiplier(modifier.Multiplier, $"Travel modifier '{modifier.Label}'");
                var output = running * modifier.Multiplier;
                calculation.Add(new TravelCalculationStep(
                    $"modifier:{modifier.Key.Trim()}",
                    modifier.Label.Trim(),
                    running,
                    modifier.Multiplier,
                    output,
                    unit.Symbol,
                    string.IsNullOrWhiteSpace(modifier.Source) ? "dm-confirmed" : modifier.Source.Trim(),
                    string.IsNullOrWhiteSpace(modifier.Note) ? null : modifier.Note.Trim()));
                running = output;
            }

            expected = running;
        }
        else
        {
            expected = command.ExpectedDistance
                ?? throw new InvalidOperationException(
                    "This older travel-helper snapshot requires a DM-confirmed expected distance because it does not contain typed rate arithmetic.");
            if (!double.IsFinite(expected) || expected < 0)
            {
                throw new InvalidOperationException("Expected travel distance must be finite and non-negative.");
            }
            calculation.Add(new TravelCalculationStep(
                "legacy-expected",
                "DM-confirmed expected distance",
                expected,
                1d,
                expected,
                unit.Symbol,
                "dm-confirmed"));
        }

        if (!double.IsFinite(expected) || expected < 0)
        {
            throw new InvalidOperationException("The configured travel arithmetic produced an invalid expected distance.");
        }

        calculation.Add(new TravelCalculationStep(
            "expected",
            "Expected distance",
            expected,
            1d,
            expected,
            unit.Symbol,
            "calculated"));

        var actual = expected;
        ProcedureResolutionRoll? variance = null;
        if (helper.Roll is not null)
        {
            var factor = helper.DistanceFactorPerRollPoint
                ?? throw new InvalidOperationException("Travel variance roll is missing its configured factor.");
            variance = Roll("travel-distance", helper.Roll, rolls);
            var multiplier = variance.Total * factor;
            actual = expected * multiplier;
            calculation.Add(new TravelCalculationStep(
                "variance",
                $"Variance: {Describe(variance)}",
                expected,
                multiplier,
                actual,
                unit.Symbol,
                "procedure"));
        }
        else if (profile.ActualDistanceResolution == ActualDistanceResolutionMode.VariableResolved)
        {
            throw new InvalidOperationException("The active variable-distance procedure snapshot does not configure a variance roll.");
        }

        if (!double.IsFinite(actual) || actual < 0)
        {
            throw new InvalidOperationException("The configured travel helper produced an invalid actual distance.");
        }

        calculation.Add(new TravelCalculationStep(
            "actual",
            "Actual distance",
            actual,
            1d,
            actual,
            unit.Symbol,
            variance is null ? "calculated" : "procedure"));

        var note = string.Create(
            CultureInfo.InvariantCulture,
            $"Automatic travel helper: expected={expected:0.###} {unit.Symbol}; actual={actual:0.###} {unit.Symbol}; segment={segment.TotalHours:0.###}h.");
        return new ProcedureResolvedTravel(
            expected,
            actual,
            unit,
            segment.TotalHours,
            calculation,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
    }

    private static void ValidateMultiplier(double value, string label)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new InvalidOperationException($"{label} must be finite and positive.");
        }
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
        ProcedureResolutionRoll? veerRoll = null;
        if (outcome == NavigationCheckOutcome.Failed)
        {
            if (helper.FailureVeer is { } rule)
            {
                veerRoll = Roll("navigation-veer", rule.Roll, rolls);
                veer = ResolveFailureVeer(rule, state.Navigation, veerRoll.Total);
            }
            else
            {
                veer = command.FailureVeerSteps
                    ?? throw new InvalidOperationException(
                        "This older navigation-helper snapshot has no typed failure-veer rule; supply a DM-confirmed failure veer or use manual resolution.");
            }
        }

        var preliminary = new ResolvedNavigation(
            outcome,
            veer,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, null));
        var resulting = NavigationResolutionTransition.Apply(profile, state.Navigation, preliminary);

        var note = $"Automatic navigation helper: {Describe(roll)}; situational modifier={command.NavigationModifier}; total={resolvedTotal}; DC={difficultyClass}.";
        if (veerRoll is not null)
        {
            note += $" Veer {Describe(veerRoll)} => candidate {veer}.";
        }
        else if (veer.HasValue)
        {
            note += $" DM-confirmed legacy failure veer={veer.Value}.";
        }
        note += $" Resulting navigation: {(resulting.IsLost ? $"lost, veer={resulting.VeerSteps}" : "oriented")}.";

        return new ProcedureResolvedNavigation(
            outcome,
            veer,
            resulting.IsLost,
            resulting.VeerSteps,
            new ResolutionProvenance(ResolutionSource.AutomaticRoll, note));
    }

    private static int ResolveFailureVeer(
        FailureVeerRule rule,
        NavigationRuntimeState previous,
        int rollTotal)
    {
        return rule.Kind switch
        {
            FailureVeerRuleKind.AlexandrianHexD10 => ResolveAlexandrianHexVeer(previous, rollTotal),
            _ => throw new ArgumentOutOfRangeException(nameof(rule.Kind))
        };
    }

    private static int ResolveAlexandrianHexVeer(NavigationRuntimeState previous, int rollTotal)
    {
        var direction = rollTotal switch
        {
            >= 1 and <= 4 => -1,
            5 or 6 => 0,
            >= 7 and <= 10 => 1,
            _ => throw new InvalidOperationException("Alexandrian hex veer roll must total between 1 and 10.")
        };

        if (!previous.IsLost || previous.VeerSteps == 0)
        {
            return direction;
        }
        if (previous.VeerSteps < 0 && direction < 0)
        {
            return previous.VeerSteps - 1;
        }
        if (previous.VeerSteps > 0 && direction > 0)
        {
            return previous.VeerSteps + 1;
        }
        return previous.VeerSteps;
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
        var generatedResolutionId = Guid.NewGuid();
        var generatedAtVersion = checked(command.ExpectedVersion + 1);
        var watchNumber = CurrentWatchNumber(expedition.Runtime);
        var audited = AddAuditReference(generated, generatedResolutionId, sequence);
        var auditEvent = BuildAuditEvent(expedition.Runtime, sequence, audited);
        var runtime = AppendAuditEvent(expedition.Runtime, auditEvent);

        var retained = (expedition.GeneratedProcedureResolutions ?? [])
            .Select(item => item.Status == GeneratedProcedureResolutionStatus.Available
                ? item with { Status = GeneratedProcedureResolutionStatus.Superseded }
                : item)
            .ToList();
        retained.Add(new GeneratedProcedureResolution(
            generatedResolutionId,
            expedition.Id,
            command.ExpectedVersion,
            generatedAtVersion,
            sequence,
            watchNumber,
            audited.Rolls.ToArray(),
            audited.Travel,
            audited.Navigation,
            audited.Encounter,
            GeneratedProcedureResolutionStatus.Available));

        var save = await store.SaveExpeditionAsync(
            expedition with
            {
                Runtime = runtime,
                GeneratedProcedureResolutions = retained
            },
            command.ExpectedVersion,
            cancellationToken);

        var saved = save.Outcome switch
        {
            SaveOutcome.Saved => save.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The crawl session changed while procedure inputs were being recorded. Reload it before generating another helper result."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };

        if (saved.Version != generatedAtVersion)
        {
            throw new InvalidOperationException("Persisted helper-generation version did not match the expected aggregate version.");
        }

        return audited with { ExpeditionVersion = saved.Version };
    }

    private static ProcedureResolutionHelperResult AddAuditReference(
        ProcedureResolutionHelperResult generated,
        Guid generatedResolutionId,
        long sequence)
    {
        ResolutionProvenance Reference(ResolutionProvenance provenance)
        {
            var suffix = $"Generated by server resolution {generatedResolutionId:D} at procedure-resolution audit event #{sequence}.";
            var note = string.IsNullOrWhiteSpace(provenance.Note)
                ? suffix
                : $"{provenance.Note.Trim()} {suffix}";
            return provenance with { Note = note };
        }

        return generated with
        {
            GeneratedResolutionId = generatedResolutionId,
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
        HexCrawl.Domain.Spatial.HexCoordinate? hex =
            runtime is ExpeditionState spatialState ? spatialState.CurrentHex : null;

        var parts = new List<string>();
        if (result.Rolls.Count > 0)
        {
            parts.Add("rolls=" + string.Join(", ", result.Rolls.Select(DescribeRoll)));
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
            $"Procedure resolution helper attempt #{sequence} [{result.GeneratedResolutionId:D}]: {string.Join("; ", parts)}.");
    }

    internal static int CurrentWatchNumber(CrawlSessionRuntimeState runtime) => runtime switch
    {
        ExpeditionState spatial => Math.Max(1, spatial.ActiveWatch?.WatchNumber ?? spatial.CompletedWatches + 1),
        NonSpatialSessionState nonSpatial => Math.Max(1, nonSpatial.ActiveWatch?.WatchNumber ?? nonSpatial.CompletedWatches + 1),
        _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
    };

    private static string DescribeRoll(ProcedureResolutionRoll roll) =>
        $"{roll.Formula} [{string.Join(", ", roll.Dice)}] = {roll.Total}";

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
