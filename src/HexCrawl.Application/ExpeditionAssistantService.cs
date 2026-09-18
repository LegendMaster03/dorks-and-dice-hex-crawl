using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed record TravelWatchAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public double ElapsedHours { get; init; }
    public double? Distance { get; init; }
    public int? HexSteps { get; init; }
    public HexCoordinate ResultingHex { get; init; }
    public double? HexProgress { get; init; }
    public int? IntendedDirection { get; init; }
    public int? ActualDirection { get; init; }
    public bool CompleteWatch { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
    public Guid? GeneratedProcedureResolutionId { get; init; }
}

public sealed record NonSpatialWatchAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public double ElapsedHours { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ProcedureDefault;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
}

public sealed record NavigationAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public bool IsLost { get; init; }
    public int VeerSteps { get; init; }
    public int? IntendedDirection { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
    public Guid? GeneratedProcedureResolutionId { get; init; }
}

public sealed record EncounterCadenceAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public EncounterOutcomeKind Outcome { get; init; }
    public double? EncounterHour { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
    public Guid? GeneratedProcedureResolutionId { get; init; }
}

public sealed class ExpeditionAssistantService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    CrawlSessionContextResolver contextResolver,
    GeneratedProcedureResolutionVerifier generatedResolutionVerifier)
{
    public async Task<StoredExpedition> RecordTravelWatchAsync(
        Guid expeditionId,
        string ownerUserId,
        TravelWatchAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);

        var stateBefore = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("Travel/watch bookkeeping requires a spatial crawl session.");
        var resolvedContext = await contextResolver.ResolveAsync(expedition, ownerUserId, cancellationToken);
        var context = resolvedContext.RuntimeContext
            ?? throw new InvalidOperationException("Travel/watch bookkeeping requires a spatial crawl session.");
        var unit = context.HexCenterDistance.Unit;
        var generated = generatedResolutionVerifier.VerifyTravelAssistant(expedition, command);
        var provenance = generatedResolutionVerifier.Provenance(
            command.ResolutionSource,
            command.ResolutionNote,
            generated,
            item => item.Travel?.Provenance);

        ResolvedTravelAmount travel;
        if (expedition.Procedure.TravelResolution == TravelResolutionMode.HexSteps)
        {
            travel = command.HexSteps.HasValue
                ? ResolvedTravelAmount.Steps(command.HexSteps.Value, provenance)
                : throw new InvalidOperationException("The selected procedure requires a resolved hex-step count.");
        }
        else
        {
            var distance = command.Distance
                ?? throw new InvalidOperationException("The selected procedure requires a resolved travel distance.");
            var resolved = new DistanceMeasure(distance, unit);
            travel = ResolvedTravelAmount.Distance(resolved, resolved, provenance);
        }

        DistanceMeasure? progress = command.HexProgress.HasValue
            ? new DistanceMeasure(command.HexProgress.Value, unit)
            : null;

        var state = CrawlAssistantActions.RecordTravelWatch(
            context,
            expedition.Procedure,
            stateBefore,
            new TravelWatchAssistantInput(
                TimeSpan.FromHours(command.ElapsedHours),
                travel,
                command.ResultingHex,
                progress,
                command.IntendedDirection.HasValue ? new HexDirection(command.IntendedDirection.Value) : null,
                command.ActualDirection.HasValue ? new HexDirection(command.ActualDirection.Value) : null,
                command.CompleteWatch,
                command.Note));

        if (resolvedContext.World is { } world)
        {
            state = state with
            {
                Position = HexGeometry.HexToWorld(world.World.Grid, state.CurrentHex),
                PositionPrecision = WorldPositionPrecision.HexAnchor
            };
        }

        var finalized = generatedResolutionVerifier.FinalizeUse(
            expedition,
            state,
            generated,
            command.ExpectedVersion);
        return await SaveAsync(
            expedition with
            {
                Runtime = finalized.Runtime,
                GeneratedProcedureResolutions = finalized.Resolutions
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> RecordNonSpatialWatchAsync(
        Guid expeditionId,
        string ownerUserId,
        NonSpatialWatchAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);

        if (expedition.Context is not NonSpatialCrawlSessionContext)
        {
            throw new InvalidOperationException("Non-spatial watch bookkeeping requires a non-spatial crawl session.");
        }

        var stateBefore = expedition.Runtime as NonSpatialSessionState
            ?? throw new InvalidOperationException("Non-spatial watch bookkeeping requires non-spatial session state.");

        var state = CrawlAssistantActions.RecordWatch(
            expedition.Procedure,
            stateBefore,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(command.ElapsedHours),
                ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote),
                command.Note));

        var finalized = generatedResolutionVerifier.FinalizeUse(
            expedition,
            state,
            null,
            command.ExpectedVersion);
        var finalizedState = (NonSpatialSessionState)finalized.Runtime;
        return await SaveAsync(
            expedition with
            {
                Runtime = finalizedState,
                RemainingWatchTime = finalizedState.ActiveWatch?.Remaining ?? TimeSpan.Zero,
                GeneratedProcedureResolutions = finalized.Resolutions
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> RecordNavigationAsync(
        Guid expeditionId,
        string ownerUserId,
        NavigationAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);

        if (expedition.Context is NonSpatialCrawlSessionContext)
        {
            throw new InvalidOperationException("Navigation bookkeeping requires a spatial crawl session.");
        }

        var stateBefore = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("Navigation bookkeeping requires spatial expedition state.");
        var generated = generatedResolutionVerifier.VerifyNavigationAssistant(expedition, command);
        var provenance = generatedResolutionVerifier.Provenance(
            command.ResolutionSource,
            command.ResolutionNote,
            generated,
            item => item.Navigation?.Provenance);
        var state = CrawlAssistantActions.RecordNavigation(
            stateBefore,
            new NavigationAssistantInput(
                command.IsLost,
                command.VeerSteps,
                command.IntendedDirection.HasValue ? new HexDirection(command.IntendedDirection.Value) : null,
                provenance,
                command.Note));

        var finalized = generatedResolutionVerifier.FinalizeUse(
            expedition,
            state,
            generated,
            command.ExpectedVersion);
        return await SaveAsync(
            expedition with
            {
                Runtime = finalized.Runtime,
                GeneratedProcedureResolutions = finalized.Resolutions
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> RecordEncounterCadenceAsync(
        Guid expeditionId,
        string ownerUserId,
        EncounterCadenceAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);

        var generated = generatedResolutionVerifier.VerifyEncounterAssistant(expedition, command);
        var provenance = generatedResolutionVerifier.Provenance(
            command.ResolutionSource,
            command.ResolutionNote,
            generated,
            item => item.Encounter?.Provenance);
        var input = new EncounterCadenceAssistantInput(
            command.Outcome,
            provenance,
            command.Note,
            OccursAtHours: command.EncounterHour);

        CrawlSessionRuntimeState runtime = expedition.Runtime switch
        {
            ExpeditionState spatial => CrawlAssistantActions.RecordEncounterCadence(spatial, input),
            NonSpatialSessionState nonSpatial => CrawlAssistantActions.RecordEncounterCadence(nonSpatial, input),
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };

        var finalized = generatedResolutionVerifier.FinalizeUse(
            expedition,
            runtime,
            generated,
            command.ExpectedVersion);
        return await SaveAsync(
            expedition with
            {
                Runtime = finalized.Runtime,
                GeneratedProcedureResolutions = finalized.Resolutions
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    private static ResolutionProvenance ClientSuppliedProvenance(
        ResolutionSource source,
        string? note)
    {
        if (source == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException(
                "AutomaticRoll is reserved for server-verified procedure-helper results and can not be supplied to a focused manual assistant.");
        }
        return new ResolutionProvenance(source, note);
    }

    private async Task<StoredExpedition> SaveAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The expedition was changed by another request. Reload it before saving focused-assistant bookkeeping."),
            _ => throw new HexCrawlNotFoundException("Expedition was not found.")
        };
    }

    private static void RequireVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new HexCrawlConcurrencyException("The resource version is stale. Reload it before saving again.");
        }
    }
}
