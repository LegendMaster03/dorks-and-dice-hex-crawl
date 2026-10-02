using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Application.Rules;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed class ProcedureResolutionHelperService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    ProcedureResolutionResolver resolver,
    ProcedureResolutionProviderEnricher providerEnricher)
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

        command = await ApplyEnvironmentAsync(expedition, ownerUserId, command, cancellationToken);
        command = await providerEnricher.PrepareAsync(expedition, command, cancellationToken);
        var generated = resolver.Resolve(
            ExpeditionProcedureExecutionResolver.Resolve(expedition),
            expedition.Context,
            expedition.Runtime,
            expedition.Version,
            command);

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

        var retained = expedition.GeneratedProcedureResolutions
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

    private async Task<ProcedureResolutionHelperCommand> ApplyEnvironmentAsync(
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
                ExpectedDistanceRulesNote = notes.Length == 0
                    ? "Effective environment and movement capability composition supplied the expected distance."
                    : string.Join(" ", notes)
            };
        }

        if (string.IsNullOrWhiteSpace(prepared.Terrain)
            && string.IsNullOrWhiteSpace(prepared.Route)
            && !string.IsNullOrWhiteSpace(evaluation.TerrainKey)
            && !string.IsNullOrWhiteSpace(evaluation.RouteKey))
        {
            prepared = prepared with
            {
                Terrain = evaluation.TerrainKey,
                Route = evaluation.RouteKey
            };
        }

        return prepared;
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
        var watchNumber = CurrentWatchNumber(runtime);
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
            var description = $"encounter={encounter.Kind}";
            if (encounter.OccursAtHours.HasValue)
            {
                description += string.Create(
                    CultureInfo.InvariantCulture,
                    $", at={encounter.OccursAtHours.Value:0.###}h");
            }
            if (encounter.LocationId.HasValue)
            {
                description += $", location={encounter.LocationId.Value:D}";
            }
            parts.Add(description);
        }
        if (result.Notes.Count > 0)
        {
            parts.Add("notes=" + string.Join(" | ", result.Notes));
        }

        return new CrawlRuntimeEvent(
            sequence,
            watchNumber,
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
