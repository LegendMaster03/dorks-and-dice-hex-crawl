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
}

public sealed record EncounterCadenceAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public EncounterOutcomeKind Outcome { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
}

public sealed record ResolveEncounterCommand(
    long ExpectedVersion,
    Guid OccurrenceId,
    ResolutionSource ResolutionSource = ResolutionSource.DmOverride,
    string? ResolutionNote = null,
    string? ResultNote = null);

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
        EnsureNoPendingEncounter(expedition);
        JourneyRuntimeIntegration.EnsureRelevantTravelAllowed(expedition);

        var stateBefore = expedition.Runtime as ExpeditionState
            ?? throw new InvalidOperationException("Travel/watch bookkeeping requires a spatial crawl session.");
        var resolvedContext = await contextResolver.ResolveAsync(expedition, ownerUserId, cancellationToken);
        var context = resolvedContext.RuntimeContext
            ?? throw new InvalidOperationException("Travel/watch bookkeeping requires a spatial crawl session.");
        var unit = context.HexCenterDistance.Unit;
        var provenance = ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote);
        var procedure = ExpeditionProcedureExecutionResolver.Resolve(expedition);

        ResolvedTravelAmount travel;
        if (procedure.Movement.TravelResolution == TravelResolutionMode.HexSteps)
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
            procedure,
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

        var forcedTravel = ForcedTravelAccounting.AccountTravelMutation(
            expedition,
            stateBefore,
            state,
            command.ExpectedVersion,
            "focused-spatial-travel",
            new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "focused-spatial-travel",
                Note: command.Note));
        var journey = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition,
            stateBefore,
            state,
            expedition.Journey);

        return await SaveAsync(
            expedition with { Runtime = state, Survival = forcedTravel.Survival, Journey = journey },
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
        EnsureNoPendingEncounter(expedition);
        JourneyRuntimeIntegration.EnsureRelevantTravelAllowed(expedition);

        if (expedition.Context is not NonSpatialCrawlSessionContext)
        {
            throw new InvalidOperationException("Non-spatial watch bookkeeping requires a non-spatial crawl session.");
        }

        var stateBefore = expedition.Runtime as NonSpatialSessionState
            ?? throw new InvalidOperationException("Non-spatial watch bookkeeping requires non-spatial session state.");
        var intervalDuration = ExpeditionProcedureRequirements.ResolveIntervalDuration(expedition.CampaignProcedure);

        var state = CrawlAssistantActions.RecordWatch(
            intervalDuration,
            stateBefore,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(command.ElapsedHours),
                ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote),
                command.Note),
            ParticipantActivityPolicyResolver.SnapshotAssignments(expedition.Party));

        var forcedTravel = ForcedTravelAccounting.AccountTravelMutation(
            expedition,
            stateBefore,
            state,
            command.ExpectedVersion,
            "focused-nonspatial-travel",
            new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "focused-nonspatial-travel",
                Note: command.Note));
        var journey = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition,
            stateBefore,
            state,
            expedition.Journey);

        return await SaveAsync(
            expedition with
            {
                Runtime = state,
                RemainingWatchTime = state.ActiveWatch?.Remaining ?? TimeSpan.Zero,
                Survival = forcedTravel.Survival,
                Journey = journey
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
        var state = CrawlAssistantActions.RecordNavigation(
            stateBefore,
            new NavigationAssistantInput(
                command.IsLost,
                command.VeerSteps,
                command.IntendedDirection.HasValue ? new HexDirection(command.IntendedDirection.Value) : null,
                ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote),
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
            ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote),
            command.Note);

        CrawlSessionRuntimeState runtime = expedition.Runtime switch
        {
            ExpeditionState spatial => CrawlAssistantActions.RecordEncounterCadence(spatial, input),
            NonSpatialSessionState nonSpatial => CrawlAssistantActions.RecordEncounterCadence(nonSpatial, input),
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };

        var pending = runtime.PendingEncounter;
        return await SaveAsync(
            expedition with
            {
                Runtime = runtime,
                PauseReason = pending is null ? expedition.PauseReason : RuntimePauseReason.EncounterTriggered,
                RemainingWatchTime = pending is null ? expedition.RemainingWatchTime : runtime switch
                {
                    ExpeditionState spatial => spatial.ActiveWatch?.Remaining ?? TimeSpan.Zero,
                    NonSpatialSessionState nonSpatial => nonSpatial.ActiveWatch?.Remaining ?? TimeSpan.Zero,
                    _ => TimeSpan.Zero
                }
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> ResolveEncounterAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveEncounterCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var pending = expedition.Runtime.PendingEncounter
            ?? throw new InvalidOperationException("No encounter is pending for this expedition.");
        if (pending.Id != command.OccurrenceId)
        {
            throw new InvalidOperationException("The requested encounter occurrence is not the encounter currently pending for this expedition.");
        }
        if (expedition.PauseReason != RuntimePauseReason.EncounterTriggered)
        {
            throw new InvalidOperationException("Pending encounter state is inconsistent with the expedition interruption.");
        }

        var provenance = ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote);
        var history = expedition.Runtime.History.ToList();
        var sequence = history.Count == 0 ? 1 : history[^1].Sequence + 1;
        var elapsed = expedition.Runtime switch
        {
            ExpeditionState spatial => spatial.ElapsedTravelTime,
            NonSpatialSessionState nonSpatial => nonSpatial.ElapsedTime,
            _ => pending.ExpeditionElapsedTime
        };
        history.Add(new CrawlRuntimeEvent(
            sequence,
            pending.WatchNumber,
            CrawlRuntimeEventKind.EncounterResolved,
            elapsed,
            pending.Hex,
            string.IsNullOrWhiteSpace(command.ResultNote)
                ? $"{pending.Outcome} resolved."
                : $"{pending.Outcome} resolved: {command.ResultNote.Trim()}",
            EncounterOutcome: pending.Outcome,
            EncounterNote: command.ResultNote,
            EncounterProvenance: provenance,
            EncounterOccurrenceId: pending.Id));

        CrawlSessionRuntimeState runtime = expedition.Runtime switch
        {
            ExpeditionState spatial => spatial with
            {
                ActiveWatch = spatial.ActiveWatch?.PendingDecision == RuntimePauseReason.EncounterTriggered
                    ? spatial.ActiveWatch with { PendingDecision = null }
                    : spatial.ActiveWatch,
                PendingEncounter = null,
                History = history
            },
            NonSpatialSessionState nonSpatial => nonSpatial with
            {
                PendingEncounter = null,
                History = history
            },
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };
        var remaining = runtime switch
        {
            ExpeditionState spatial => spatial.ActiveWatch?.Remaining ?? TimeSpan.Zero,
            NonSpatialSessionState nonSpatial => nonSpatial.ActiveWatch?.Remaining ?? TimeSpan.Zero,
            _ => TimeSpan.Zero
        };
        return await SaveAsync(
            expedition with { Runtime = runtime, PauseReason = null, RemainingWatchTime = remaining },
            command.ExpectedVersion,
            cancellationToken);
    }

    private static void EnsureNoPendingEncounter(StoredExpedition expedition)
    {
        if (expedition.Runtime.PendingEncounter is not null)
        {
            throw new InvalidOperationException("Resolve the pending encounter before continuing travel.");
        }
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