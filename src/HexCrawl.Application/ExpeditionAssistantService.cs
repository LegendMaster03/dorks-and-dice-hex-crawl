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
}

public sealed record EncounterCadenceAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public EncounterOutcomeKind Outcome { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
}

public sealed class ExpeditionAssistantService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    CrawlSessionContextResolver contextResolver)
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
        var provenance = new ResolutionProvenance(command.ResolutionSource, command.ResolutionNote);

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

        return await SaveAsync(
            expedition with { Runtime = state },
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
        var state = CrawlAssistantActions.RecordNavigation(
            stateBefore,
            new NavigationAssistantInput(
                command.IsLost,
                command.VeerSteps,
                command.IntendedDirection.HasValue ? new HexDirection(command.IntendedDirection.Value) : null,
                new ResolutionProvenance(command.ResolutionSource, command.ResolutionNote),
                command.Note));

        return await SaveAsync(
            expedition with { Runtime = state },
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

        var input = new EncounterCadenceAssistantInput(
            command.Outcome,
            new ResolutionProvenance(command.ResolutionSource, command.ResolutionNote),
            command.Note);

        CrawlSessionRuntimeState runtime = expedition.Runtime switch
        {
            ExpeditionState spatial => CrawlAssistantActions.RecordEncounterCadence(spatial, input),
            NonSpatialSessionState nonSpatial => CrawlAssistantActions.RecordEncounterCadence(nonSpatial, input),
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };

        return await SaveAsync(
            expedition with { Runtime = runtime },
            command.ExpectedVersion,
            cancellationToken);
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
