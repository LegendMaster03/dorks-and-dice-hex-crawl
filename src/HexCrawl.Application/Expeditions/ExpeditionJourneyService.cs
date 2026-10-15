using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record StartJourneyProcessCommand
{
    public required long ExpectedVersion { get; init; }
    public required Guid ProcessId { get; init; }
    public JourneyProcessDefinition? Definition { get; init; }
    public string? ProcessKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? DestinationReference { get; init; }
    public string? RouteReference { get; init; }
    public string? LocationReference { get; init; }
    public required ExpeditionConsequenceProvenance Provenance { get; init; }
}

public sealed record StartExplorationFlowCommand(
    long ExpectedVersion,
    Guid ProcessId,
    ExpeditionConsequenceProvenance Provenance);

public sealed record RecordExplorationCellCommand(
    long ExpectedVersion,
    Guid OperationId,
    string? ActionKey,
    bool MarkVisited,
    bool MarkRestSite,
    string? LocationName,
    string? DmNote,
    bool? EncounterOccurs,
    ExpeditionConsequenceProvenance Provenance,
    Guid? ProcessId = null,
    IReadOnlyList<ExpeditionConsequence>? Consequences = null,
    string? RegionKey = null,
    string? RegionName = null,
    string? RegionDmNote = null,
    IReadOnlyList<ExplorationTableResult>? LocationTableResults = null,
    IReadOnlyList<ExplorationTableResult>? RegionTableResults = null,
    IReadOnlyList<ExplorationTableRollRequest>? TableRolls = null);

public sealed record ResolveJourneyProcessCommand(
    long ExpectedVersion,
    JourneyProcessResolutionInput Resolution,
    bool CaptureCurrentEnvironment = true);

public sealed record CreateJourneyEventOpportunityCommand(
    long ExpectedVersion,
    JourneyEventOpportunityInput Opportunity,
    bool CaptureCurrentEnvironment = true);

public sealed record ResolveJourneyEventCommand(
    long ExpectedVersion,
    JourneyEventResolutionInput Resolution,
    bool CaptureCurrentEnvironment = true);

public sealed record CloseJourneyProcessCommand(
    long ExpectedVersion,
    JourneyProcessStatus Status,
    string Reason,
    ExpeditionConsequenceProvenance Provenance);

public sealed record JourneyOperationResult(
    StoredExpedition Expedition,
    string Detail,
    bool StateChanged,
    Guid? ProcessId = null,
    Guid? EventOccurrenceId = null);

public sealed class ExpeditionJourneyService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    ExplorationTableRoller tableRoller)
{
    public async Task<JourneyOperationResult> StartProcessAsync(
        Guid expeditionId,
        string ownerUserId,
        StartJourneyProcessCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = JourneyProcedurePolicyResolver.ResolveProcess(expedition.CampaignProcedure);
        RequireSupportedProcess(policy);
        ValidateRoleDrivenPolicy(expedition, policy);
        var definition = command.Definition ?? DefinitionFromPinnedPolicy(policy, command);
        definition.Validate();
        ValidateDefinitionAgainstPinnedPolicy(policy, definition);
        ValidateDefinitionRoleKeys(expedition, definition);
        var transition = JourneyProcessEngine.Start(
            expedition.Journey,
            command.ProcessId,
            definition,
            policy.ToExecutionSnapshot(),
            JourneyClockReference.From(expedition.Runtime),
            expedition.Party,
            command.Provenance);
        var updated = expedition with { Journey = transition.State };
        var saved = await SaveAsync(updated, command.ExpectedVersion, cancellationToken);
        return new(saved, $"Started journey process '{definition.DisplayName}'.", true, command.ProcessId);
    }

    /// <summary>
    /// Start the ordered exploration flow stored in the pinned campaign procedure.
    /// It uses the same persistent journey process engine as all other staged
    /// procedures. No named-game dispatch, second stage state, or inferred
    /// action outcomes are introduced.
    /// </summary>
    public async Task<JourneyOperationResult> StartExplorationFlowAsync(
        Guid expeditionId,
        string ownerUserId,
        StartExplorationFlowCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(
            expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var projection = ExplorationFlowProcessSnapshot.From(expedition.CampaignProcedure);
        if (expedition.Journey.ActiveProcesses.Any(process =>
                process.Execution.ExecutionHandler ==
                    GenericProcedureExecutionHandlers.ExplorationFlowPolicy))
            throw new InvalidOperationException(
                "Resolve or close the active exploration flow before starting another.");

 
        var transition = JourneyProcessEngine.Start(
            expedition.Journey, command.ProcessId,
            projection.Definition, projection.Execution,
            JourneyClockReference.From(expedition.Runtime), expedition.Party,
            command.Provenance);
        var saved = await SaveAsync(
            expedition with { Journey = transition.State },
            command.ExpectedVersion, cancellationToken);
        return new(saved, "Started the pinned exploration flow.", true, command.ProcessId);
    }

    /// <summary>
    /// Persists DM-confirmed location, region, and table outcomes. When a
    /// configured action stage is linked, its eligible progress and explicitly
    /// supplied consequences are committed atomically with the cell record.
    /// No random table result is fabricated or player knowledge revealed.
    /// </summary>
    public async Task<JourneyOperationResult> RecordExplorationCellAsync(
        Guid expeditionId,
        string ownerUserId,
        RecordExplorationCellCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(
            expeditionId, ownerUserId, cancellationToken);
        if (command.OperationId == Guid.Empty)
            throw new InvalidOperationException("Exploration operation id is required.");
        var prior = expedition.Journey.ExplorationOperations.SingleOrDefault(value =>
            value.OperationId == command.OperationId);
        if (prior is not null)
        {
            // A retry never changes state, but the same token may not be used
            // for another action, even if the original cell is no longer current.
            var supplied = ExplorationSubmission(command, prior.Cell);
            if (prior != supplied)
                throw new InvalidOperationException(
                    "Exploration operation id already belongs to a different submitted action.");
            return new(expedition, "Exploration operation was already recorded.", false,
                ProcessId: prior.ProcessId);
        }
        if (expedition.Journey.ConsumedExplorationOperationIds.Contains(command.OperationId))
            throw new InvalidOperationException(
                "Exploration operation was previously consumed without a verifiable submission.");
        if (expedition.Version != command.ExpectedVersion)
            throw new HexCrawlConcurrencyException(
                "The expedition changed. Reload exploration state before applying this operation.");
        command.Provenance.Validate();
        if (expedition.Runtime is not CellExpeditionState spatial)
            throw new InvalidOperationException("Exploration cell recording requires a spatial expedition.");
        var flow = GenericProcedureRuntime.Bind(expedition.CampaignProcedure).ExplorationFlow
            ?? throw new InvalidOperationException("The exact pinned procedure has no exploration flow.");
        var (tiling, _) = await coreService.ResolveCellGeometryAsync(
            expedition, ownerUserId, cancellationToken);
        var cell = spatial.Traversal.CurrentCell;
        tiling.Resolve(cell.Address);
        var existing = expedition.Journey.ExplorationCells.SingleOrDefault(value => value.Cell == cell);
        JourneyProcessInstance? linkedProcess = null;
        if (command.ProcessId is { } processId)
        {
            if (flow.ActionResolutionStageKey is null)
                throw new InvalidOperationException("The pinned exploration policy does not link location actions to stages.");
            linkedProcess = expedition.Journey.ActiveProcesses.SingleOrDefault(value =>
                value.Id == processId &&
                value.Execution.ExecutionHandler == GenericProcedureExecutionHandlers.ExplorationFlowPolicy)
                ?? throw new InvalidOperationException("The linked exploration process is not active.");
            if (linkedProcess.CurrentStageKey != flow.ActionResolutionStageKey)
                throw new InvalidOperationException("Resolve the selection stage before recording its chosen action.");
            if (expedition.Journey.ExplorationStageActions.Any(action => action.ProcessId == processId))
                throw new InvalidOperationException("An action has already been recorded for this exploration process.");
            var selection = expedition.Journey.Resolutions.LastOrDefault(value =>
                value.ProcessId == processId && value.StageKey == flow.ActionSelectionStageKey
                && value.TransitionToStageKey == flow.ActionResolutionStageKey);
            if (selection is null || !string.Equals(selection.OutcomeKey, command.ActionKey,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Recorded action must match the selected action persisted in this process.");
            if (!command.MarkVisited)
                throw new InvalidOperationException("Linked exploration actions require confirmed resolution.");
        }
        else if (command.MarkVisited && flow.ActionResolutionStageKey is not null
            && expedition.Journey.ActiveProcesses.Any(value =>
                value.Execution.ExecutionHandler == GenericProcedureExecutionHandlers.ExplorationFlowPolicy
                && value.CurrentStageKey == flow.ActionResolutionStageKey))
            throw new InvalidOperationException("Record this action against the active exploration process.");
        // Rest-site eligibility is prior authoritative state, not a flag that
        // a request can grant to authorize its own rest action.
        var restSite = existing?.RestSite == true;
        var allowed = flow.AvailableActions(existing?.Visited == true, restSite);
        if (string.IsNullOrWhiteSpace(command.ActionKey))
        {
            if (command.MarkVisited && allowed.Count > 0)
                throw new InvalidOperationException("Choose an eligible exploration action for this location.");
        }
        else if (!allowed.Contains(command.ActionKey.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected exploration action is not available at this location.");
        if (command.LocationName is { Length: > 300 } || command.DmNote is { Length: > 4000 })
            throw new InvalidOperationException("Exploration location details exceed their limits.");
        if (!command.MarkVisited && !command.MarkRestSite
            && command.LocationName is null && command.DmNote is null
            && command.RegionKey is null && command.RegionName is null
            && command.RegionDmNote is null && command.LocationTableResults is null
            && command.RegionTableResults is null
            && (command.TableRolls is null || command.TableRolls.Count == 0))
            throw new InvalidOperationException("No exploration information was supplied.");
        if (command.RegionKey is not null)
            ExplorationTableResult.RequireKey(command.RegionKey, "Exploration region key", 120);
        if (command.RegionName is not null)
            ExpeditionConsequenceProvenance.ValidateOptional(command.RegionName, 300, "Region name");
        if (command.RegionDmNote is not null)
            ExpeditionConsequenceProvenance.ValidateOptional(command.RegionDmNote, 4000, "Region DM note");
        if (command.LocationTableResults is not null)
            ExplorationTableResult.ValidateCollection(command.LocationTableResults);
        if (command.RegionTableResults is not null)
            ExplorationTableResult.ValidateCollection(command.RegionTableResults);
        var rollRequests = command.TableRolls ?? [];
        ExplorationTableRollRequest.ValidateCollection(rollRequests);
        var assignedRegion = command.RegionKey ?? existing?.RegionKey;
        if (assignedRegion is null && rollRequests.Any(value => value.Scope == ExplorationTableScope.Region))
            throw new InvalidOperationException("A region table roll requires an assigned region key.");
        if (command.MarkVisited && flow.ShouldGenerateLocation(existing?.Visited == true, command.ActionKey)
            && flow.RequireRegionOnDiscovery && assignedRegion is null)
            throw new InvalidOperationException("Resolve the discovered region before completing this action.");
        // Evaluate the supplied tables once, only on a fresh, version-valid
        // operation. Replay is checked above, before any random source is used.
        var rolled = rollRequests.Count == 0
            ? [] : tableRoller.Roll(rollRequests);
        var locationResults = OverlayResults(
            command.LocationTableResults ?? existing?.TableResults ?? [],
            rolled.Where(value => value.Scope == ExplorationTableScope.Location)
                .Select(value => value.Result).ToArray());
        var priorRegion = assignedRegion is null ? null :
            expedition.Journey.ExplorationRegions.SingleOrDefault(value => value.Key == assignedRegion);
        var regionResults = OverlayResults(
            command.RegionTableResults ?? priorRegion?.TableResults ?? [],
            rolled.Where(value => value.Scope == ExplorationTableScope.Region)
                .Select(value => value.Result).ToArray());
        if (command.MarkVisited && flow.ShouldGenerateLocation(existing?.Visited == true, command.ActionKey))
        {
            if (flow.RequireRegionOnDiscovery && assignedRegion is null)
                throw new InvalidOperationException("Resolve the discovered region before completing this action.");
            var locationIsNamed = !string.IsNullOrWhiteSpace(command.LocationName ?? existing?.LocationName);
            var locationTable = flow.LocationResultTableKey ?? "location";
            var locationIsResolved = locationResults
                .Any(result => string.Equals(result.TableKey, locationTable, StringComparison.OrdinalIgnoreCase));
            if (flow.RequireResolvedLocationOnDiscovery && !locationIsNamed && !locationIsResolved)
                throw new InvalidOperationException(
                    "Resolve the discovered location or its configured table outcome before completing this action.");
        }
        if (assignedRegion is null
            && (command.RegionName is not null || command.RegionDmNote is not null
                || command.RegionTableResults is not null))
            throw new InvalidOperationException("A region key is required before recording region results.");
        var actionConsequences = command.Consequences ?? [];
        if (actionConsequences.Count > 0 && !command.MarkVisited)
            throw new InvalidOperationException(
                "An exploration action consequence requires a confirmed action.");
        ValidateNewConsequenceIds(expedition, actionConsequences);
        var obligation = flow.EncounterForAction(existing?.Visited == true, command.ActionKey);
        if (command.MarkVisited && obligation == ExplorationEncounterRequirement.OptionalOrRiskBased
            && !command.EncounterOccurs.HasValue)
            throw new InvalidOperationException(
                "An optional or risk-dependent exploration encounter needs an explicit DM result.");
        if (command.MarkVisited && obligation == ExplorationEncounterRequirement.Required
            && command.EncounterOccurs == false)
            throw new InvalidOperationException("The pinned procedure requires an encounter for this action.");
        if (command.EncounterOccurs.HasValue && !command.MarkVisited)
            throw new InvalidOperationException(
                "An exploration encounter result requires a confirmed action or visit.");
        if (command.EncounterOccurs == true && obligation == ExplorationEncounterRequirement.None)
            throw new InvalidOperationException("This exploration action does not authorize an encounter.");
        var updated = new ExplorationCellRecord
        {
            Cell = cell,
            Visited = existing?.Visited == true || command.MarkVisited,
            RestSite = restSite || command.MarkRestSite,
            RegionKey = assignedRegion,
            TableResults = locationResults,
            LocationName = command.LocationName is null ? existing?.LocationName : command.LocationName.Trim(),
            DmNote = command.DmNote is null ? existing?.DmNote : command.DmNote.Trim(),
            LastOperationId = command.OperationId,
            Provenance = command.Provenance
        };
        updated.Validate();
        var regions = expedition.Journey.ExplorationRegions;
        if (assignedRegion is not null
            && (priorRegion is null || command.RegionName is not null
                || command.RegionDmNote is not null || command.RegionTableResults is not null
                || rolled.Any(value => value.Scope == ExplorationTableScope.Region)))
        {
            var region = new ExplorationRegionRecord
            {
                Key = assignedRegion,
                Name = command.RegionName is null ? priorRegion?.Name : command.RegionName.Trim(),
                DmNote = command.RegionDmNote is null ? priorRegion?.DmNote : command.RegionDmNote.Trim(),
                TableResults = regionResults,
                LastOperationId = command.OperationId,
                Provenance = command.Provenance
            };
            region.Validate();
            regions = [.. regions.Where(value => value.Key != assignedRegion), region];
        }
        var next = expedition.Journey with
        {
            ExplorationCells = [.. expedition.Journey.ExplorationCells.Where(value => value.Cell != cell), updated],
            ExplorationRegions = regions,
            ConsumedExplorationOperationIds =
                [.. expedition.Journey.ConsumedExplorationOperationIds, command.OperationId],
            ExplorationOperations =
                [.. expedition.Journey.ExplorationOperations, ExplorationSubmission(command, cell)]
        };
        Guid? encounterId = null;
        if (command.MarkVisited && obligation != ExplorationEncounterRequirement.None
            && (obligation == ExplorationEncounterRequirement.Required || command.EncounterOccurs == true))
        {
            var events = JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure);
            if (events.Support == JourneyPolicySupport.Supported
                && events.Supports(JourneyEventTriggerKind.External)
                && events.LinkMode is JourneyEventLinkMode.Standalone or JourneyEventLinkMode.Both)
            {
                encounterId = JourneyProcessEngine.DeterministicId(
                    command.OperationId, "exploration-encounter");
                var created = JourneyEventEngine.CreateOpportunity(
                    next, events, new JourneyEventOpportunityInput
                    {
                        OccurrenceId = encounterId.Value,
                        Trigger = JourneyEventTriggerKind.External,
                        TriggerReference = command.OperationId.ToString("D"),
                        TargetKind = JourneyEventTargetKind.Unresolved,
                        Provenance = command.Provenance,
                        Note = "An exploration encounter requires explicit resolution; no table result or opposition was inferred."
                    }, JourneyClockReference.From(expedition.Runtime), expedition.Party);
                next = created.State;
            }
        }
        if (linkedProcess is not null)
        {
            next = next with
            {
                ExplorationStageActions =
                [
                    .. next.ExplorationStageActions,
                    new ExplorationStageActionRecord(
                        command.OperationId, linkedProcess.Id, linkedProcess.CurrentStageKey,
                        command.ActionKey!, cell, encounterId)
                ]
            };
        }
        var working = expedition with { Journey = next };
        foreach (var consequence in actionConsequences)
        {
            var sourced = WithSourceReference(
                consequence, $"exploration-action:{command.OperationId:D}");
            working = ExpeditionConsequenceAggregateTransition.Apply(
                working, sourced, command.Provenance).Expedition;
        }
        if (linkedProcess is not null && !encounterId.HasValue)
            working = working with
            {
                Journey = CompleteRecordedExplorationAction(
                    working.Journey, linkedProcess.Id, command.OperationId,
                    command.Provenance, working)
            };
        var saved = await SaveAsync(
            working, command.ExpectedVersion, cancellationToken);
        return new(saved, encounterId.HasValue
                ? "Exploration action recorded; encounter resolution is pending."
                : "Exploration action and eligible stage progress recorded.", true,
            linkedProcess?.Id, encounterId);
    }

    public async Task<JourneyOperationResult> ResolveProcessAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveJourneyProcessCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        ValidateRoleKey(expedition, command.Resolution.RoleKey);
        var selectedProcess = expedition.Journey.ActiveProcesses.SingleOrDefault(
            value => value.Id == command.Resolution.ProcessId);
        if (selectedProcess is { Execution.ExecutionHandler:
                GenericProcedureExecutionHandlers.ExplorationFlowPolicy })
        {
            var flow = GenericProcedureRuntime.Bind(expedition.CampaignProcedure).ExplorationFlow!;
            if (selectedProcess.CurrentStageKey == flow.ActionResolutionStageKey)
                throw new InvalidOperationException(
                    "This exploration action stage advances when the recorded action and its encounter are resolved.");
            if (selectedProcess.CurrentStageKey == flow.ActionSelectionStageKey)
            {
                if (!command.Resolution.CompleteStage
                    || command.Resolution.FailProcess || command.Resolution.CompleteProcess
                    || command.Resolution.Consequences.Count > 0)
                    throw new InvalidOperationException(
                        "Select and complete the exploration choice without applying action consequences.");
                if (expedition.Runtime is not CellExpeditionState spatial)
                    throw new InvalidOperationException("Action selection requires an authoritative spatial cell.");
                var entry = expedition.Journey.ExplorationCells.SingleOrDefault(
                    value => value.Cell == spatial.Traversal.CurrentCell);
                var actions = flow.AvailableActions(entry?.Visited == true, entry?.RestSite == true);
                if (!actions.Contains(command.Resolution.OutcomeKey, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected action is not eligible for the current location.");
            }
        }
        var resolutionClock = JourneyClockReference.From(expedition.Runtime);
        var transition = JourneyProcessEngine.Resolve(
            expedition.Journey,
            command.Resolution,
            resolutionClock,
            expedition.Party);
        if (!transition.StateChanged)
        {
            return new(expedition, "Journey resolution was already consumed; no state was changed.", false, command.Resolution.ProcessId);
        }
        ValidateNewConsequenceIds(expedition, command.Resolution.Consequences);

        var working = expedition with { Journey = transition.State };
        foreach (var consequence in command.Resolution.Consequences)
        {
            var sourced = WithSourceReference(
                consequence,
                $"journey-process:{command.Resolution.ProcessId:D}:resolution:{command.Resolution.ResolutionId:D}");
            var applied = ExpeditionConsequenceAggregateTransition.Apply(working, sourced, command.Resolution.Provenance);
            working = applied.Expedition;
        }

        var eventIds = new List<Guid>();
        var eventPolicy = JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure);
        if (eventPolicy.Support == JourneyPolicySupport.Supported
            && eventPolicy.LinkMode is JourneyEventLinkMode.ProcessLinked or JourneyEventLinkMode.Both)
        {
            var environment = command.CaptureCurrentEnvironment
                ? await SnapshotEnvironmentAsync(working, ownerUserId, cancellationToken)
                : [];
            if (transition.ProgressChanged && eventPolicy.Supports(JourneyEventTriggerKind.ProcessProgress))
            {
                var eventId = JourneyProcessEngine.DeterministicId(command.Resolution.ResolutionId, "process-progress-event");
                var created = JourneyEventEngine.CreateOpportunity(
                    working.Journey,
                    eventPolicy,
                    new JourneyEventOpportunityInput
                    {
                        OccurrenceId = eventId,
                        ProcessId = command.Resolution.ProcessId,
                        StageKey = command.Resolution.StageKey,
                        Trigger = JourneyEventTriggerKind.ProcessProgress,
                        TriggerReference = command.Resolution.ResolutionId.ToString("D"),
                        TargetKind = JourneyEventTargetKind.Unresolved,
                        Environment = environment,
                        Provenance = new ExpeditionConsequenceProvenance(
                            ExpeditionConsequenceSourceKind.Procedure,
                            "journey-process-progress-event-opportunity",
                            SourceReference: command.Resolution.ResolutionId.ToString("D")),
                        Note = "Resolved process progress created an event evaluation opportunity. Event content, target selection, and consequence quantities remain unresolved."
                    },
                    resolutionClock,
                    working.Party);
                working = working with { Journey = created.State };
                eventIds.Add(eventId);
            }
            if (transition.StageChanged && eventPolicy.Supports(JourneyEventTriggerKind.StageTransition))
            {
                var eventId = JourneyProcessEngine.DeterministicId(command.Resolution.ResolutionId, "stage-transition-event");
                var created = JourneyEventEngine.CreateOpportunity(
                    working.Journey,
                    eventPolicy,
                    new JourneyEventOpportunityInput
                    {
                        OccurrenceId = eventId,
                        ProcessId = command.Resolution.ProcessId,
                        StageKey = transition.Process.CurrentStageKey,
                        Trigger = JourneyEventTriggerKind.StageTransition,
                        TriggerReference = command.Resolution.ResolutionId.ToString("D"),
                        TargetKind = JourneyEventTargetKind.Unresolved,
                        Environment = environment,
                        Provenance = new ExpeditionConsequenceProvenance(
                            ExpeditionConsequenceSourceKind.Procedure,
                            "journey-stage-transition-event-opportunity",
                            SourceReference: command.Resolution.ResolutionId.ToString("D")),
                        Note = "A resolved stage transition created an event evaluation opportunity."
                    },
                    resolutionClock,
                    working.Party);
                working = working with { Journey = created.State };
                eventIds.Add(eventId);
            }
        }

        if (eventIds.Count > 0)
        {
            working = working with
            {
                Journey = working.Journey with
                {
                    Resolutions = working.Journey.Resolutions.Select(value =>
                        value.ResolutionId == command.Resolution.ResolutionId
                            ? value with { EventOccurrenceIds = eventIds }
                            : value).ToArray()
                }
            };
        }
        ValidateJourneyAggregate(working);
        var saved = await SaveAsync(working, command.ExpectedVersion, cancellationToken);
        return new(saved, "Journey process resolution applied atomically.", true,
            command.Resolution.ProcessId, eventIds.FirstOrDefault() == Guid.Empty ? null : eventIds.First());
    }

    public async Task<JourneyOperationResult> CreateEventOpportunityAsync(
        Guid expeditionId,
        string ownerUserId,
        CreateJourneyEventOpportunityCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure);
        var opportunity = command.Opportunity;
        if (opportunity.TargetRoleKey is not null) ValidateRoleKey(expedition, opportunity.TargetRoleKey);
        if (command.CaptureCurrentEnvironment && opportunity.Environment.Count == 0)
        {
            opportunity = opportunity with { Environment = await SnapshotEnvironmentAsync(expedition, ownerUserId, cancellationToken) };
        }
        var transition = JourneyEventEngine.CreateOpportunity(
            expedition.Journey,
            policy,
            opportunity,
            JourneyClockReference.From(expedition.Runtime),
            expedition.Party);
        if (!transition.StateChanged)
        {
            return new(expedition, "Journey event opportunity already exists; no state was changed.", false,
                transition.Occurrence.ProcessId, transition.Occurrence.Id);
        }
        var saved = await SaveAsync(expedition with { Journey = transition.State }, command.ExpectedVersion, cancellationToken);
        return new(saved, "Journey event opportunity created.", true, transition.Occurrence.ProcessId, transition.Occurrence.Id);
    }

    public async Task<JourneyOperationResult> ResolveEventAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveJourneyEventCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure);
        var resolution = command.Resolution;
        if (command.CaptureCurrentEnvironment && resolution.Environment is null)
        {
            resolution = resolution with { Environment = await SnapshotEnvironmentAsync(expedition, ownerUserId, cancellationToken) };
        }
        if (resolution.TargetRoleKey is not null) ValidateRoleKey(expedition, resolution.TargetRoleKey);
        var transition = JourneyEventEngine.Resolve(
            expedition.Journey,
            policy,
            resolution,
            JourneyClockReference.From(expedition.Runtime),
            expedition.Party);
        if (!transition.StateChanged)
        {
            return new(expedition, "Journey event occurrence was already resolved; no state was changed.", false,
                transition.Occurrence.ProcessId, transition.Occurrence.Id);
        }
        ValidateNewConsequenceIds(expedition, resolution.Consequences);
        var linkedAction = expedition.Journey.ExplorationStageActions.SingleOrDefault(
            value => value.EncounterOccurrenceId == resolution.OccurrenceId);
        if (linkedAction is not null && resolution.Status != JourneyEventStatus.Resolved)
            throw new InvalidOperationException(
                "A generated exploration encounter requires explicit resolution before its action can complete.");

        var working = expedition with { Journey = transition.State };
        foreach (var consequence in resolution.Consequences)
        {
            var sourced = WithSourceReference(consequence, $"journey-event:{resolution.OccurrenceId:D}");
            var applied = ExpeditionConsequenceAggregateTransition.Apply(working, sourced, resolution.Provenance);
            working = applied.Expedition;
        }
        if (linkedAction is not null)
            working = working with
            {
                Journey = CompleteRecordedExplorationAction(
                    working.Journey, linkedAction.ProcessId,
                    linkedAction.OperationId, resolution.Provenance, working)
            };
        ValidateJourneyAggregate(working);
        var saved = await SaveAsync(working, command.ExpectedVersion, cancellationToken);
        return new(saved, "Journey event resolution, consequences, and eligible stage progress were committed atomically.", true,
            linkedAction?.ProcessId ?? transition.Occurrence.ProcessId, transition.Occurrence.Id);
    }

    public Task<JourneyOperationResult> CompleteProcessAsync(
        Guid expeditionId,
        Guid processId,
        string ownerUserId,
        CloseJourneyProcessCommand command,
        CancellationToken cancellationToken = default) =>
        CloseAsync(expeditionId, processId, ownerUserId, command with { Status = JourneyProcessStatus.Completed }, cancellationToken);

    public Task<JourneyOperationResult> FailProcessAsync(
        Guid expeditionId,
        Guid processId,
        string ownerUserId,
        CloseJourneyProcessCommand command,
        CancellationToken cancellationToken = default) =>
        CloseAsync(expeditionId, processId, ownerUserId, command with { Status = JourneyProcessStatus.Failed }, cancellationToken);

    public Task<JourneyOperationResult> AbandonProcessAsync(
        Guid expeditionId,
        Guid processId,
        string ownerUserId,
        CloseJourneyProcessCommand command,
        CancellationToken cancellationToken = default) =>
        CloseAsync(expeditionId, processId, ownerUserId, command with { Status = JourneyProcessStatus.Abandoned }, cancellationToken);

    private async Task<JourneyOperationResult> CloseAsync(
        Guid expeditionId,
        Guid processId,
        string ownerUserId,
        CloseJourneyProcessCommand command,
        CancellationToken cancellationToken)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var already = expedition.Journey.ClosedProcesses.SingleOrDefault(value => value.Id == processId);
        if (already is not null)
        {
            if (already.Status != command.Status)
            {
                throw new InvalidOperationException("Journey process is already closed with a different terminal status.");
            }
            return new(expedition, "Journey process was already closed; no state was changed.", false, processId);
        }
        var journey = JourneyProcessEngine.Close(
            expedition.Journey,
            processId,
            command.Status,
            command.Reason,
            JourneyClockReference.From(expedition.Runtime),
            expedition.Party,
            command.Provenance);
        var saved = await SaveAsync(expedition with { Journey = journey }, command.ExpectedVersion, cancellationToken);
        return new(saved, $"Journey process marked {command.Status}.", true, processId);
    }

    private static IReadOnlyList<ExplorationTableResult> OverlayResults(
        IReadOnlyList<ExplorationTableResult> existing,
        IReadOnlyList<ExplorationTableResult> rolled)
    {
        ExplorationTableResult.ValidateCollection(existing);
        ExplorationTableResult.ValidateCollection(rolled);
        var replacement = rolled.Select(value => value.TableKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. existing.Where(value => !replacement.Contains(value.TableKey)), .. rolled];
    }

    private static ExplorationOperationRecord ExplorationSubmission(
        RecordExplorationCellCommand command, HexCrawl.Domain.Spatial.WorldCellId cell)
    {
        var consequences = command.Consequences ?? [];
        // Typed, deterministic serialization prevents replay of an operation ID
        // with changed time, resource, recovery, or other consequence inputs.
        var fingerprint = consequences.Count == 0 ? null :
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(consequences)));
        var tableFingerprint = command.LocationTableResults is null
            && command.RegionTableResults is null && (command.TableRolls?.Count ?? 0) == 0
            ? null
            : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                command.LocationTableResults, command.RegionTableResults, command.TableRolls
            })));
        return new ExplorationOperationRecord(command.OperationId, cell, command.ActionKey?.Trim(),
            command.MarkVisited, command.MarkRestSite, command.LocationName?.Trim(),
            command.DmNote?.Trim(), command.EncounterOccurs, command.ProcessId,
            command.Provenance)
        {
            ConsequenceFingerprint = fingerprint,
            RegionKey = command.RegionKey,
            RegionName = command.RegionName?.Trim(),
            RegionDmNote = command.RegionDmNote?.Trim(),
            TableFingerprint = tableFingerprint
        };
    }

    private static ExpeditionJourneyState CompleteRecordedExplorationAction(
        ExpeditionJourneyState journey, Guid processId, Guid operationId,
        ExpeditionConsequenceProvenance provenance, StoredExpedition expedition)
    {
        var process = journey.ActiveProcesses.SingleOrDefault(value => value.Id == processId)
            ?? throw new InvalidOperationException("The recorded exploration process is no longer active.");
        var action = journey.ExplorationStageActions.Single(value =>
            value.ProcessId == processId && value.OperationId == operationId);
        if (process.CurrentStageKey != action.StageKey)
            throw new InvalidOperationException("The recorded action no longer matches the active stage.");
        if (action.EncounterOccurrenceId is { } eventId
            && journey.EventOccurrences.Single(value => value.Id == eventId).Status
                != JourneyEventStatus.Resolved)
            throw new InvalidOperationException("The recorded encounter remains unresolved.");
        var transition = JourneyProcessEngine.Resolve(
            journey, new JourneyProcessResolutionInput
            {
                ResolutionId = JourneyProcessEngine.DeterministicId(operationId, "exploration-stage-completed"),
                ProcessId = processId,
                StageKey = action.StageKey,
                OutcomeKey = action.ActionKey,
                ProgressState = "resolved",
                CompleteStage = true,
                Provenance = provenance
            }, JourneyClockReference.From(expedition.Runtime), expedition.Party);
        return transition.State;
    }

    private async Task<StoredExpedition> LoadAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        if (expedition.Version != expectedVersion)
        {
            throw new HexCrawlConcurrencyException(
                "The expedition was changed by another request. Reload current journey state before applying this operation.");
        }
        return expedition;
    }

    private async Task<StoredExpedition> SaveAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ValidateJourneyAggregate(expedition);
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The expedition was changed by another request. Reload current journey state before applying this operation."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };
    }

    private async Task<IReadOnlyList<JourneyEnvironmentFactSnapshot>> SnapshotEnvironmentAsync(
        StoredExpedition expedition,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        EffectiveEnvironmentContext context;
        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            var world = await coreService.GetOverworldAsync(worldContext.WorldId, ownerUserId, cancellationToken);
            context = EnvironmentContextResolver.Resolve(expedition, world.World);
        }
        else
        {
            context = EnvironmentContextResolver.Resolve(expedition);
        }

        return context.Facts.Where(value => value.Effective).Select(value =>
        {
            var fact = value.Fact;
            var factValue = fact.ValueKind == EnvironmentValueKind.Tag
                ? fact.Tag!
                : fact.Measurement!.Value.ToString("R", CultureInfo.InvariantCulture);
            var unit = fact.ValueKind == EnvironmentValueKind.Measurement ? fact.Measurement!.Unit : null;
            var source = $"{value.Source.Kind}:{fact.Id:D}";
            return new JourneyEnvironmentFactSnapshot(fact.Dimension, factValue, unit, source);
        }).ToArray();
    }

    private static JourneyProcessDefinition DefinitionFromPinnedPolicy(
        JourneyProcessPolicy policy,
        StartJourneyProcessCommand command)
    {
        if (policy.StageKeys.Count == 0)
        {
            throw new InvalidOperationException(
                "The exact pinned journey.process policy does not provide a concrete stage structure. Supply a typed process definition.");
        }
        var processKey = RequiredText(command.ProcessKey, "Journey process key");
        var displayName = string.IsNullOrWhiteSpace(command.DisplayName) ? Humanize(processKey) : command.DisplayName.Trim();
        var stages = policy.StageKeys.Select(stageKey => new JourneyStageDefinition
        {
            StageKey = stageKey,
            DisplayName = Humanize(stageKey),
            CompletionModel = JourneyStageCompletionModel.Explicit,
            InitialProgressState = policy.ProgressKind == JourneyProgressValueKind.ExplicitState ? "initial" : null
        }).ToArray();
        return new JourneyProcessDefinition
        {
            ProcessKey = processKey,
            DisplayName = displayName,
            Description = Trim(command.Description),
            InitialStageKey = policy.StageKeys[0],
            StageOrder = policy.StageKeys,
            Stages = stages,
            DestinationReference = Trim(command.DestinationReference),
            RouteReference = Trim(command.RouteReference),
            LocationReference = Trim(command.LocationReference)
        };
    }

    private static void ValidateDefinitionAgainstPinnedPolicy(
        JourneyProcessPolicy policy,
        JourneyProcessDefinition definition)
    {
        if (policy.StageKeys.Count > 0)
        {
            if (!policy.StageKeys.SequenceEqual(definition.StageOrder, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "The supplied process definition does not match the explicit stage keys stored in the pinned journey.process policy.");
            }
            if (!string.Equals(definition.InitialStageKey, policy.StageKeys[0], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The supplied process definition must start at the first explicit stage stored in the pinned journey.process policy.");
            }
        }
        if (policy.ProgressKind == JourneyProgressValueKind.ExplicitState
            && definition.Stages.Any(value => string.IsNullOrWhiteSpace(value.InitialProgressState)))
        {
            throw new InvalidOperationException("Every stage of an explicit-state journey process requires an initial progress state.");
        }
    }

    private static void ValidateRoleDrivenPolicy(StoredExpedition expedition, JourneyProcessPolicy policy)
    {
        if (!policy.RoleDriven) return;
        var activities = ParticipantActivityPolicyResolver.Resolve(expedition.CampaignProcedure);
        if (activities.Support != ParticipantActivityPolicySupport.Supported || activities.RoleKeys.Count == 0)
        {
            throw new InvalidOperationException(
                "The exact pinned role-driven journey.process policy requires a supported party.activities policy with declared role keys.");
        }
    }

    private static void ValidateDefinitionRoleKeys(StoredExpedition expedition, JourneyProcessDefinition definition)
    {
        foreach (var roleKey in definition.Stages.SelectMany(value => value.RoleKeys).Distinct(StringComparer.Ordinal))
        {
            ValidateRoleKey(expedition, roleKey);
        }
    }

    private static void ValidateRoleKey(StoredExpedition expedition, string? roleKey)
    {
        if (roleKey is null) return;
        var activities = ParticipantActivityPolicyResolver.Resolve(expedition.CampaignProcedure);
        if (activities.Support != ParticipantActivityPolicySupport.Supported
            || !activities.RoleKeys.Contains(roleKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Journey role '{roleKey}' is not declared by the exact pinned party.activities policy.");
        }
    }

    private static void ValidateNewConsequenceIds(
        StoredExpedition expedition,
        IReadOnlyList<ExpeditionConsequence> consequences)
    {
        if (consequences.Select(value => value.Id).Distinct().Count() != consequences.Count)
            throw new InvalidOperationException(
                "Journey operation consequence ids must be unique within one submission.");
        var existing = expedition.Effects.AppliedConsequences
            .Select(value => value.ConsequenceId)
            .ToHashSet();
        var collision = consequences.FirstOrDefault(value => existing.Contains(value.Id));
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"Journey operation consequence id '{collision.Id:D}' was already consumed by a prior expedition consequence.");
        }
    }

    private static ExpeditionConsequence WithSourceReference(
        ExpeditionConsequence consequence,
        string sourceReference) =>
        consequence with { SourceReference = sourceReference };

    private static void RequireSupportedProcess(JourneyProcessPolicy policy)
    {
        if (policy.Support == JourneyPolicySupport.None)
        {
            throw new InvalidOperationException("The exact pinned CampaignProcedure has no journey.process capability.");
        }
        if (policy.Support == JourneyPolicySupport.Unsupported)
        {
            throw new InvalidOperationException(policy.UnsupportedReason ?? "The exact pinned journey-process policy is unsupported.");
        }
    }

    private static void ValidateJourneyAggregate(StoredExpedition expedition) =>
        expedition.Journey.Validate(
            expedition.Party,
            expedition.Effects.AppliedConsequences.Select(value => value.ConsequenceId).ToHashSet());

    private static string Humanize(string key)
    {
        var value = key.Replace('-', ' ').Replace('_', ' ').Trim();
        return value.Length == 0 ? key : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string RequiredText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
        return value.Trim();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
