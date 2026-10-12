using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Application.Rules;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed class ProcedureResolutionHelperService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    ProcedureResolutionResolver resolver,
    ProcedureResolutionProviderEnricher providerEnricher)
{
    public async Task<CellProcedureResolutionHelperResult> ResolveCellsAsync(
        Guid expeditionId,
        string ownerUserId,
        CellProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var expedition = await coreService.GetExpeditionAsync(
            expeditionId, ownerUserId, cancellationToken);
        if (command.ExpectedVersion != expedition.Version)
            throw new HexCrawlConcurrencyException(
                "Cell resolution was requested against a stale expedition version.");
        var state = expedition.Runtime as CellExpeditionState
            ?? throw new InvalidOperationException(
                "Cell procedure resolution requires a generalized spatial expedition.");
        var geometry = await coreService.ResolveCellGeometryAsync(
            expedition, ownerUserId, cancellationToken);
        var runtime = ExpeditionProcedureExecutionResolver.Resolve(expedition);
        if (runtime.Movement.MechanicVersion != 2)
            throw new InvalidOperationException("Cell resolution requires a pinned V2 movement procedure.");

        // The provider's legacy numeric carrier is used only for existing
        // provider dispatch. The qualified distance is independently retained.
        // A provider result without a proven physical unit is never accepted.
        var providerCommand = new ProcedureResolutionHelperCommand
        {
            ExpectedVersion = command.ExpectedVersion,
            ExpectedDistance = command.ExpectedDistance?.Value,
            ExpectedDistanceQualified = command.ExpectedDistance,
            SuppressesNavigationCheck = command.SuppressesNavigationCheck,
            DeliberateDoubleBack = command.DeliberateDoubleBack,
            NavigationDifficultyClass = command.NavigationDifficultyClass,
            NavigationModifier = command.NavigationModifier,
            KeyedLocationId = command.KeyedLocationId,
            TravelDistanceRule = command.TravelDistanceRule,
            BaseSpeedFeet = command.BaseSpeedFeet,
            Terrain = command.Terrain,
            Route = command.Route,
            NavigationRiskFactors = command.NavigationRiskFactors
        };
        var environment = await ApplyEnvironmentAsync(
            expedition, ownerUserId, providerCommand, cancellationToken);
        var prepared = await providerEnricher.PrepareAsync(
            expedition, environment.Command, environment.MovementInput,
            cancellationToken);
        var expected = command.ExpectedDistance ?? prepared.ExpectedDistanceQualified;
        if (expected is { } qualified && runtime.Movement.TravelResolution
                == TravelResolutionMode.ContinuousDistance)
        {
            var physical = geometry.Tiling.PhysicalDistancePerWorldUnit
                ?? throw new InvalidOperationException(
                    "Continuous cell travel requires authoritative physical calibration.");
            _ = qualified.ConvertTo(physical.Unit);
        }
        var generated = resolver.ResolveCells(runtime, expedition.Context, state,
            expedition.Version, command with
            {
                ExpectedDistance = expected,
                ExpectedDistanceRulesNote = prepared.ExpectedDistanceRulesNote,
                NavigationDifficultyClass = prepared.NavigationDifficultyClass,
                NavigationDifficultyRulesNote = prepared.NavigationDifficultyRulesNote
            });
        if (generated.Rolls.Count == 0
            && generated.Travel is null && generated.Navigation is null
            && generated.Encounter is null)
            return generated;

        var sequence = state.History.Count == 0 ? 1 : state.History[^1].Sequence + 1;
        var id = Guid.NewGuid();
        var generatedAtVersion = checked(expedition.Version + 1);
        var audited = AddCellAuditReference(generated, id, sequence);
        var rollDescription = string.Join(", ", audited.Rolls.Select(DescribeRoll));
        var eventRecord = new CrawlRuntimeEvent(
            sequence,
            CurrentWatchNumber(state),
            CrawlRuntimeEventKind.ProcedureResolutionHelperGenerated,
            state.ElapsedTravelTime,
            null,
            $"Geometric procedure resolution #{sequence} [{id:D}]: rolls={rollDescription}; "
            + $"travel={audited.Travel?.ActualDistance?.Value.ToString(CultureInfo.InvariantCulture) ?? "none"} "
            + $"{audited.Travel?.ActualDistance?.Unit.Symbol ?? ""}; "
            + $"navigation={audited.Navigation?.Outcome.ToString() ?? "none"}; "
            + $"veerDegrees={audited.Navigation?.VeerDegreesOnFailure?.ToString(CultureInfo.InvariantCulture) ?? "none"}; "
            + $"encounter={audited.Encounter?.Kind.ToString() ?? "none"}.")
        { Cell = state.Traversal.CurrentCell };
        var retained = expedition.GeneratedProcedureResolutions
            .Select(item => item.Status == GeneratedProcedureResolutionStatus.Available
                ? item with { Status = GeneratedProcedureResolutionStatus.Superseded }
                : item).ToList();
        retained.Add(new GeneratedProcedureResolution(
            id, expedition.Id, expedition.Version, generatedAtVersion, sequence,
            CurrentWatchNumber(state), audited.Rolls.ToArray(),
            null, null, null, GeneratedProcedureResolutionStatus.Available)
        {
            CellTravel = audited.Travel,
            CellNavigation = audited.Navigation,
            CellEncounter = audited.Encounter,
            CellSuppressesNavigationCheck = command.SuppressesNavigationCheck,
            CellDeliberateDoubleBack = command.DeliberateDoubleBack
        });
        var save = await store.SaveExpeditionAsync(
            expedition with
            {
                Runtime = state with { History = [.. state.History, eventRecord] },
                GeneratedProcedureResolutions = retained
            }, expedition.Version, cancellationToken);
        var saved = save.Outcome switch
        {
            SaveOutcome.Saved => save.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The expedition changed while the cell resolution was generated."),
            _ => throw new HexCrawlNotFoundException("Expedition was not found.")
        };
        if (saved.Version != generatedAtVersion)
            throw new InvalidOperationException("Cell helper generation version was not persisted as expected.");
        return audited with { ExpeditionVersion = saved.Version };
    }

    private static CellProcedureResolutionHelperResult AddCellAuditReference(
        CellProcedureResolutionHelperResult result, Guid id, long sequence)
    {
        ResolutionProvenance Ref(ResolutionProvenance provenance)
        {
            var marker = $"Server-generated cell resolution {id:D}, audit event #{sequence}.";
            var note = string.IsNullOrWhiteSpace(provenance.Note)
                ? marker : provenance.Note.Trim() + " " + marker;
            return provenance with { Note = note };
        }

        return result with
        {
            GeneratedResolutionId = id,
            AuditSequence = sequence,
            Travel = result.Travel is { } travel
                ? travel with { Provenance = Ref(travel.Provenance) } : null,
            Navigation = result.Navigation is { } nav
                ? nav with { Provenance = Ref(nav.Provenance) } : null,
            Encounter = result.Encounter is { } enc
                ? enc with { Provenance = Ref(enc.Provenance) } : null
        };
    }

    private async Task<(ProcedureResolutionHelperCommand Command, MovementCompositionInput MovementInput)> ApplyEnvironmentAsync(
        StoredExpedition expedition,
        string ownerUserId,
        ProcedureResolutionHelperCommand command,
        CancellationToken cancellationToken)
    {
        HexCrawl.Domain.World.OverworldDefinition? world = null;
        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            world = (await coreService.GetOverworldAsync(
                worldContext.WorldId, ownerUserId, cancellationToken)).World;
        }

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);
        var composition = MovementCapabilityComposer.Compose(expedition, evaluation.MovementInput);
        var needsAutomaticTravelDistance = command.ExpectedDistance is null
            && !string.IsNullOrWhiteSpace(command.TravelDistanceRule);

        if (needsAutomaticTravelDistance
            && evaluation.Status != EnvironmentProcedureEvaluationStatus.Resolved)
        {
            var detail = evaluation.Diagnostics
                .Concat(evaluation.UnsupportedSemantics)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var suffix = detail.Length == 0 ? "" : " " + string.Join(" ", detail);
            throw new InvalidOperationException(
                "Effective environment must be resolved or explicitly adjudicated before expected travel distance can be generated automatically."
                + suffix);
        }

        var prepared = command;
        if (prepared.ExpectedDistance is null
            && !string.IsNullOrWhiteSpace(prepared.TravelDistanceRule)
            && composition.Status == MovementCompositionStatus.Resolved
            && composition.SuggestedExpectedDistance is { } suggested)
        {
            var notes = evaluation.Provenance
                .Concat(composition.Provenance)
                .Concat(composition.Diagnostics)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            prepared = prepared with
            {
                ExpectedDistance = suggested.Value,
                ExpectedDistanceQualified = suggested,
                ExpectedDistanceRulesNote = notes.Length == 0
                    ? "Effective environment and movement capability composition supplied the expected distance."
                    : string.Join(" ", notes)
            };
        }

        return (prepared, evaluation.MovementInput);
    }

    internal static int CurrentWatchNumber(CellExpeditionState state) =>
        Math.Max(1, state.ActiveWatch?.WatchNumber ?? state.CompletedWatches + 1);

    private static string DescribeRoll(ProcedureResolutionRoll roll) =>
        $"{roll.Formula} [{string.Join(", ", roll.Dice)}] = {roll.Total}";

}
