using System.Globalization;
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
    HexCrawlService coreService)
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
        var definition = command.Definition ?? DefinitionFromPinnedPolicy(policy, command);
        definition.Validate();
        ValidateDefinitionAgainstPinnedPolicy(policy, definition);
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

    public async Task<JourneyOperationResult> ResolveProcessAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveJourneyProcessCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        ValidateRoleKey(expedition, command.Resolution.RoleKey);
        var transition = JourneyProcessEngine.Resolve(
            expedition.Journey,
            command.Resolution,
            JourneyClockReference.From(expedition.Runtime),
            expedition.Party);
        if (!transition.StateChanged)
        {
            return new(expedition, "Journey resolution was already consumed; no state was changed.", false, command.Resolution.ProcessId);
        }

        var working = expedition with { Journey = transition.State };
        foreach (var consequence in command.Resolution.Consequences)
        {
            var sourced = EnsureSourceReference(
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
                    JourneyClockReference.From(working.Runtime),
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
                    JourneyClockReference.From(working.Runtime),
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

        var working = expedition with { Journey = transition.State };
        foreach (var consequence in resolution.Consequences)
        {
            var sourced = EnsureSourceReference(consequence, $"journey-event:{resolution.OccurrenceId:D}");
            var applied = ExpeditionConsequenceAggregateTransition.Apply(working, sourced, resolution.Provenance);
            working = applied.Expedition;
        }
        ValidateJourneyAggregate(working);
        var saved = await SaveAsync(working, command.ExpectedVersion, cancellationToken);
        return new(saved, "Journey event resolution and generated consequences were committed atomically.", true,
            transition.Occurrence.ProcessId, transition.Occurrence.Id);
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
        if (policy.StageKeys.Count > 0 && !policy.StageKeys.SequenceEqual(definition.StageOrder, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The supplied process definition does not match the explicit stage keys stored in the pinned journey.process policy.");
        }
        if (policy.ProgressKind == JourneyProgressValueKind.ExplicitState
            && definition.Stages.Any(value => string.IsNullOrWhiteSpace(value.InitialProgressState)))
        {
            throw new InvalidOperationException("Every stage of an explicit-state journey process requires an initial progress state.");
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

    private static ExpeditionConsequence EnsureSourceReference(ExpeditionConsequence consequence, string sourceReference) =>
        string.IsNullOrWhiteSpace(consequence.SourceReference)
            ? consequence with { SourceReference = sourceReference }
            : consequence;

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
