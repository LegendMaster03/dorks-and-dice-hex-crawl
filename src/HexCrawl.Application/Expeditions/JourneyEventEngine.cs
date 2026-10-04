using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record JourneyEventOpportunityInput
{
    public required Guid OccurrenceId { get; init; }
    public Guid? ProcessId { get; init; }
    public string? StageKey { get; init; }
    public required JourneyEventTriggerKind Trigger { get; init; }
    public required string TriggerReference { get; init; }
    public JourneyEventTargetKind TargetKind { get; init; } = JourneyEventTargetKind.Unresolved;
    public string? TargetRoleKey { get; init; }
    public Guid? TargetId { get; init; }
    public IReadOnlyList<JourneyEnvironmentFactSnapshot> Environment { get; init; } = [];
    public required ExpeditionConsequenceProvenance Provenance { get; init; }
    public string? Note { get; init; }
}

public sealed record JourneyEventResolutionInput
{
    public required Guid OccurrenceId { get; init; }
    public required JourneyEventStatus Status { get; init; }
    public string? EventKey { get; init; }
    public string? EventType { get; init; }
    public JourneyEventTargetKind TargetKind { get; init; } = JourneyEventTargetKind.Unresolved;
    public string? TargetRoleKey { get; init; }
    public Guid? TargetId { get; init; }
    public IReadOnlyList<JourneyEnvironmentFactSnapshot>? Environment { get; init; }
    public IReadOnlyList<ExpeditionConsequence> Consequences { get; init; } = [];
    public required ExpeditionConsequenceProvenance Provenance { get; init; }
    public string? Note { get; init; }
}

public sealed record JourneyEventTransitionResult(
    ExpeditionJourneyState State,
    JourneyEventOccurrence Occurrence,
    bool StateChanged);

public static class JourneyEventEngine
{
    public static JourneyEventTransitionResult CreateOpportunity(
        ExpeditionJourneyState state,
        JourneyEventPolicy policy,
        JourneyEventOpportunityInput input,
        JourneyClockReference clock,
        CrawlPartySheet party)
    {
        RequireSupported(policy);
        if (!policy.Supports(input.Trigger))
        {
            throw new InvalidOperationException(
                $"The exact pinned journey-event policy does not declare trigger source '{input.Trigger}'.");
        }
        if (input.OccurrenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey event occurrence id is required.");
        }
        input.Provenance.Validate();
        ValidateLink(policy, input.ProcessId);
        ValidateProcessStage(state, input.ProcessId, input.StageKey);
        if (!policy.SupportsTarget(input.TargetKind, allowUnresolved: true))
        {
            throw new InvalidOperationException(
                $"The exact pinned journey-event targeting model '{policy.TargetingModel}' does not support target kind '{input.TargetKind}'.");
        }
        foreach (var fact in input.Environment) fact.Validate();

        var triggerReference = RequiredText(input.TriggerReference, "Journey event trigger reference");
        var targetRole = Trim(input.TargetRoleKey);
        var note = Trim(input.Note);
        var existing = state.EventOccurrences.SingleOrDefault(value => value.Id == input.OccurrenceId);
        if (existing is not null)
        {
            if (existing.Trigger != input.Trigger
                || existing.ProcessId != input.ProcessId
                || !string.Equals(existing.StageKey, input.StageKey, StringComparison.Ordinal)
                || !string.Equals(existing.TriggerReference, triggerReference, StringComparison.Ordinal)
                || existing.TargetKind != input.TargetKind
                || !string.Equals(existing.TargetRoleKey, targetRole, StringComparison.Ordinal)
                || existing.TargetId != input.TargetId
                || !existing.Environment.SequenceEqual(input.Environment)
                || existing.Provenance != input.Provenance
                || !string.Equals(existing.Note, note, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Journey event occurrence id is already used by a different opportunity.");
            }
            return new(state, existing, false);
        }

        var occurrence = new JourneyEventOccurrence
        {
            Id = input.OccurrenceId,
            ProcessId = input.ProcessId,
            StageKey = input.StageKey,
            Trigger = input.Trigger,
            TriggerReference = triggerReference,
            Status = JourneyEventStatus.ResolutionRequired,
            TargetKind = input.TargetKind,
            TargetRoleKey = targetRole,
            TargetId = input.TargetId,
            Environment = input.Environment,
            Provenance = input.Provenance,
            Note = note
        };
        occurrence.Validate(party, ProcessIds(state));
        var updated = state with
        {
            EventOccurrences = state.EventOccurrences.Append(occurrence).ToArray(),
            History = state.History.Append(History(
                JourneyHistoryKind.EventOpportunityCreated,
                clock,
                input.Provenance,
                $"Created journey-event resolution opportunity from '{input.Trigger}' trigger.",
                input.ProcessId,
                input.StageKey,
                occurrence.Id)).ToArray()
        };
        updated.Validate(party);
        return new(updated, occurrence, true);
    }

    public static JourneyEventTransitionResult Resolve(
        ExpeditionJourneyState state,
        JourneyEventPolicy policy,
        JourneyEventResolutionInput input,
        JourneyClockReference clock,
        CrawlPartySheet party)
    {
        RequireSupported(policy);
        if (input.OccurrenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey event occurrence id is required.");
        }
        if (input.Status == JourneyEventStatus.ResolutionRequired)
        {
            throw new InvalidOperationException("Journey event resolution must resolve, skip, or mark the opportunity not applicable.");
        }
        input.Provenance.Validate();
        var occurrence = state.EventOccurrences.SingleOrDefault(value => value.Id == input.OccurrenceId)
            ?? throw new InvalidOperationException("Journey event occurrence was not found.");
        if (occurrence.Status != JourneyEventStatus.ResolutionRequired)
        {
            ValidateResolutionReplay(occurrence, input);
            return new(state, occurrence, false);
        }

        JourneyParticipantSnapshot? snapshot = null;
        Guid? targetId = input.TargetId;
        string? targetRole = Trim(input.TargetRoleKey);
        var targetKind = input.TargetKind;
        if (input.Status == JourneyEventStatus.Resolved)
        {
            if (!policy.SupportsTarget(targetKind, allowUnresolved: false))
            {
                throw new InvalidOperationException(
                    $"The exact pinned journey-event targeting model '{policy.TargetingModel}' does not support resolved target kind '{targetKind}'.");
            }
            if (string.IsNullOrWhiteSpace(input.EventKey))
            {
                throw new InvalidOperationException("A resolved journey event requires an explicit event key; event content is never fabricated.");
            }
            ValidateResolvedTarget(targetKind, targetRole, targetId, party, out snapshot);
            foreach (var consequence in input.Consequences)
            {
                consequence.ValidateAgainst(party);
            }
            if (input.Consequences.Select(value => value.Id).Distinct().Count() != input.Consequences.Count)
            {
                throw new InvalidOperationException("Journey event consequence ids must be unique.");
            }
        }
        else
        {
            if (input.Consequences.Count > 0 || input.EventKey is not null || input.EventType is not null)
            {
                throw new InvalidOperationException("Skipped or not-applicable journey events can not generate event content or consequences.");
            }
            if (targetKind != JourneyEventTargetKind.Unresolved || targetRole is not null || targetId.HasValue)
            {
                throw new InvalidOperationException("Skipped or not-applicable journey events can not retain a resolved target.");
            }
        }

        var environment = input.Environment ?? occurrence.Environment;
        foreach (var fact in environment) fact.Validate();
        var resolved = occurrence with
        {
            Status = input.Status,
            TargetKind = targetKind,
            TargetRoleKey = targetRole,
            TargetId = targetId,
            ParticipantSnapshot = snapshot,
            EventKey = Trim(input.EventKey),
            EventType = Trim(input.EventType),
            Environment = environment,
            ConsequenceIds = input.Consequences.Select(value => value.Id).ToArray(),
            Provenance = input.Provenance,
            Note = Trim(input.Note) ?? occurrence.Note
        };
        resolved.Validate(party, ProcessIds(state));
        var kind = input.Status == JourneyEventStatus.Resolved
            ? JourneyHistoryKind.EventResolved
            : JourneyHistoryKind.EventSkipped;
        var updated = state with
        {
            EventOccurrences = state.EventOccurrences.Select(value => value.Id == input.OccurrenceId ? resolved : value).ToArray(),
            History = state.History.Append(History(
                kind,
                clock,
                input.Provenance,
                input.Status == JourneyEventStatus.Resolved
                    ? $"Resolved journey event '{resolved.EventKey}'."
                    : $"Journey event opportunity marked {input.Status}.",
                resolved.ProcessId,
                resolved.StageKey,
                resolved.Id)).ToArray()
        };
        updated.Validate(party);
        return new(updated, resolved, true);
    }

    private static void ValidateResolutionReplay(
        JourneyEventOccurrence occurrence,
        JourneyEventResolutionInput input)
    {
        var environment = input.Environment ?? occurrence.Environment;
        var note = Trim(input.Note) ?? occurrence.Note;
        var consequenceIds = input.Consequences.Select(value => value.Id).ToArray();
        if (occurrence.Status != input.Status
            || !string.Equals(occurrence.EventKey, Trim(input.EventKey), StringComparison.Ordinal)
            || !string.Equals(occurrence.EventType, Trim(input.EventType), StringComparison.Ordinal)
            || occurrence.TargetKind != input.TargetKind
            || !string.Equals(occurrence.TargetRoleKey, Trim(input.TargetRoleKey), StringComparison.Ordinal)
            || occurrence.TargetId != input.TargetId
            || !occurrence.Environment.SequenceEqual(environment)
            || !occurrence.ConsequenceIds.SequenceEqual(consequenceIds)
            || occurrence.Provenance != input.Provenance
            || !string.Equals(occurrence.Note, note, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Journey event occurrence id is already resolved with different resolved data.");
        }
    }

    private static void ValidateResolvedTarget(
        JourneyEventTargetKind kind,
        string? roleKey,
        Guid? targetId,
        CrawlPartySheet party,
        out JourneyParticipantSnapshot? participant)
    {
        participant = null;
        switch (kind)
        {
            case JourneyEventTargetKind.Role:
                participant = JourneyProcessEngine.ResolveParticipantSnapshot(party, targetId, roleKey, requireRole: true);
                return;
            case JourneyEventTargetKind.Participant:
                participant = JourneyProcessEngine.ResolveParticipantSnapshot(party, targetId, null, requireRole: false)
                    ?? throw new InvalidOperationException("Participant-targeted journey event requires a participant.");
                return;
            case JourneyEventTargetKind.Party:
            case JourneyEventTargetKind.Expedition:
                if (targetId.HasValue || roleKey is not null)
                {
                    throw new InvalidOperationException("Party/expedition journey event target can not contain a participant or role id.");
                }
                return;
            case JourneyEventTargetKind.Mount:
            case JourneyEventTargetKind.Vehicle:
                var contributorKind = kind == JourneyEventTargetKind.Mount
                    ? MovementCapabilityContributorKind.Mount
                    : MovementCapabilityContributorKind.Vehicle;
                if (!targetId.HasValue || !party.MovementContributors.Any(value => value.Id == targetId && value.Kind == contributorKind))
                {
                    throw new InvalidOperationException("Journey event target references a mount or vehicle that does not exist.");
                }
                if (roleKey is not null)
                {
                    throw new InvalidOperationException("Mount/vehicle journey event targets do not use a participant role key.");
                }
                return;
            case JourneyEventTargetKind.Unresolved:
                throw new InvalidOperationException("A resolved journey event requires an explicit target kind.");
            default:
                throw new InvalidOperationException("Journey event target kind is not supported.");
        }
    }

    private static void ValidateLink(JourneyEventPolicy policy, Guid? processId)
    {
        if (processId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey event process id can not be empty.");
        }
        if (processId.HasValue && policy.LinkMode == JourneyEventLinkMode.Standalone)
        {
            throw new InvalidOperationException("The exact pinned journey-event policy supports standalone events only.");
        }
        if (!processId.HasValue && policy.LinkMode == JourneyEventLinkMode.ProcessLinked)
        {
            throw new InvalidOperationException("The exact pinned journey-event policy requires a process-linked event.");
        }
    }

    private static void ValidateProcessStage(ExpeditionJourneyState state, Guid? processId, string? stageKey)
    {
        if (!processId.HasValue)
        {
            if (stageKey is not null)
            {
                throw new InvalidOperationException("A standalone journey event can not reference a process stage.");
            }
            return;
        }
        var process = state.ActiveProcesses.Concat(state.ClosedProcesses).SingleOrDefault(value => value.Id == processId.Value)
            ?? throw new InvalidOperationException("Journey event references a process that does not exist.");
        if (stageKey is not null && !process.Definition.Stages.Any(value => string.Equals(value.StageKey, stageKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Journey event references a stage that does not exist in its process.");
        }
    }

    private static void RequireSupported(JourneyEventPolicy policy)
    {
        if (policy.Support == JourneyPolicySupport.None)
        {
            throw new InvalidOperationException("The exact pinned CampaignProcedure has no journey.events capability.");
        }
        if (policy.Support == JourneyPolicySupport.Unsupported)
        {
            throw new InvalidOperationException(policy.UnsupportedReason ?? "The exact pinned journey-event policy is unsupported.");
        }
    }

    private static IReadOnlySet<Guid> ProcessIds(ExpeditionJourneyState state) =>
        state.ActiveProcesses.Concat(state.ClosedProcesses).Select(value => value.Id).ToHashSet();

    private static JourneyHistoryRecord History(
        JourneyHistoryKind kind,
        JourneyClockReference clock,
        ExpeditionConsequenceProvenance provenance,
        string detail,
        Guid? processId,
        string? stageKey,
        Guid occurrenceId) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        ProcessId = processId,
        StageKey = stageKey,
        EventOccurrenceId = occurrenceId,
        ExpeditionTime = clock.ExpeditionTime,
        CompletedWatches = clock.CompletedWatches,
        Detail = detail,
        Provenance = provenance
    };

    private static string RequiredText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
        return value.Trim();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
