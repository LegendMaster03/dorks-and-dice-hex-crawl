using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed record CreateEncounterHandoffCommand(
    long ExpectedVersion,
    Guid HandoffId,
    string? ReturnPath,
    long? RuntimeEncounterSequence,
    Guid? JourneyEventOccurrenceId);

public sealed record EncounterHandoffIdentity(
    Guid HandoffId,
    string EncounterOccurrenceId,
    Guid ExpeditionId,
    string ExpeditionName);

public sealed record EncounterHandoffReturnContext(string? ReturnPath);

public sealed record EncounterHandoffTimeContext(
    int Day,
    int? WatchNumber,
    double ExpeditionElapsedHours);

public sealed record EncounterHandoffLocation(
    Guid Id,
    string Name,
    string Category);

public sealed record EncounterHandoffWorldContext(
    Guid? OverworldId,
    HexCoordinate? Hex,
    EncounterHandoffLocation? Location);

public sealed record EncounterHandoffEncounter(
    string Outcome,
    string Summary,
    string? DmNote);

public sealed record EncounterHandoffTarget(
    ExpeditionEffectScope Scope,
    Guid? TargetId);

public sealed record EncounterHandoffProvenance(
    ExpeditionConsequenceSourceKind SourceKind,
    string SourceKey,
    string? SourceReference,
    string? ProviderName,
    string? Note)
{
    public static EncounterHandoffProvenance From(ExpeditionConsequenceProvenance value) => new(
        value.SourceKind,
        value.SourceKey,
        value.SourceReference,
        value.ProviderName,
        value.Note);
}

public sealed record EncounterHandoffCircumstance(
    Guid ConsequenceId,
    string ConsequenceKey,
    string CircumstanceKey,
    string? Value,
    EncounterHandoffTarget Target,
    EncounterHandoffProvenance Provenance);

public sealed record EncounterHandoffEffect(
    Guid Id,
    string EffectKey,
    EncounterHandoffTarget Target,
    int? Level,
    double? Magnitude,
    string? Unit,
    string? State,
    IReadOnlyList<Guid> SourceConsequenceIds);

public sealed record EncounterHandoffResource(
    Guid Id,
    string ResourceKey,
    EncounterHandoffTarget Target,
    ExpeditionResourceInventoryModel InventoryModel,
    bool IsDepleted,
    double? Quantity,
    string? Unit,
    string? SymbolicState,
    int? SupplyDieSides);

public sealed record EncounterHandoffJourneyProvenance(
    Guid EventOccurrenceId,
    Guid? ProcessId,
    string? ProcessKey,
    string? DestinationReference,
    string? RouteReference,
    string? LocationReference,
    string? StageKey,
    string EventKey,
    string? EventType,
    string TriggerReference,
    EncounterHandoffProvenance Provenance);

public sealed record EncounterHandoffLinkedScene(
    Guid Id,
    string Kind,
    string ReferenceKey);

public sealed record EncounterHandoffV2(
    int Version,
    string SourceTool,
    EncounterHandoffIdentity Identity,
    EncounterHandoffReturnContext ReturnContext,
    EncounterHandoffTimeContext TimeContext,
    EncounterHandoffWorldContext WorldContext,
    EncounterHandoffEncounter Encounter,
    IReadOnlyList<object> Combatants,
    IReadOnlyList<EncounterHandoffCircumstance> Circumstances,
    IReadOnlyList<EncounterHandoffEffect> Effects,
    IReadOnlyList<EncounterHandoffResource> Resources,
    EncounterHandoffJourneyProvenance? JourneyProvenance,
    IReadOnlyList<EncounterHandoffLinkedScene> LinkedScenes);

public sealed class EncounterHandoffService(HexCrawlService coreService)
{
    private const int MaximumContextItems = 50;

    public async Task<EncounterHandoffV2> CreateAsync(
        Guid expeditionId,
        string ownerUserId,
        CreateEncounterHandoffCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.HandoffId == Guid.Empty)
        {
            throw new ArgumentException("Encounter handoff id is required.");
        }
        if (command.RuntimeEncounterSequence.HasValue == command.JourneyEventOccurrenceId.HasValue)
        {
            throw new ArgumentException(
                "Encounter handoff must identify exactly one runtime encounter or journey event occurrence.");
        }

        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        if (expedition.Version != command.ExpectedVersion)
        {
            throw new HexCrawlConcurrencyException(
                "The expedition changed before the encounter handoff was generated. Reload the expedition and try again.");
        }

        var returnPath = ValidateReturnPath(command.ReturnPath);
        var overworldId = expedition.Context is WorldBoundCrawlSessionContext worldContext
            ? worldContext.WorldId
            : (Guid?)null;

        return command.RuntimeEncounterSequence is { } sequence
            ? BuildFromRuntime(expedition, overworldId, command.HandoffId, returnPath, sequence)
            : BuildFromJourney(
                expedition,
                overworldId,
                command.HandoffId,
                returnPath,
                command.JourneyEventOccurrenceId!.Value);
    }

    private static EncounterHandoffV2 BuildFromRuntime(
        StoredExpedition expedition,
        Guid? overworldId,
        Guid handoffId,
        string? returnPath,
        long sequence)
    {
        var runtimeEvent = expedition.Runtime.History.SingleOrDefault(value => value.Sequence == sequence)
            ?? throw new ArgumentException("The requested runtime encounter occurrence does not exist.");
        if (runtimeEvent.Kind != CrawlRuntimeEventKind.EncounterTriggered
            || runtimeEvent.EncounterOutcome is null)
        {
            throw new ArgumentException(
                "The requested runtime event is not a structured encounter occurrence.");
        }
        if (runtimeEvent.SubjectId.HasValue && runtimeEvent.EncounterLocation is null)
        {
            throw new InvalidOperationException(
                "The runtime encounter references a location without retained historical location metadata.");
        }
        if (runtimeEvent.EncounterLocation is { } snapshot
            && runtimeEvent.SubjectId != snapshot.Id)
        {
            throw new InvalidOperationException(
                "The retained encounter location metadata does not match the runtime encounter subject.");
        }

        var location = runtimeEvent.EncounterLocation is null
            ? null
            : new EncounterHandoffLocation(
                runtimeEvent.EncounterLocation.Id,
                runtimeEvent.EncounterLocation.Name,
                runtimeEvent.EncounterLocation.Category);
        var linkedScenes = ProjectLinkedScenes(runtimeEvent.EncounterLocation);
        var elapsed = runtimeEvent.ExpeditionElapsedTime;
        int? watchNumber = expedition.Context.Kind == CrawlSessionContextKind.NonSpatial
            ? null
            : runtimeEvent.WatchNumber;

        return Build(
            expedition,
            handoffId,
            $"runtime:{runtimeEvent.Sequence}",
            returnPath,
            elapsed,
            watchNumber,
            overworldId,
            runtimeEvent.Hex,
            location,
            runtimeEvent.EncounterOutcome.Value.ToString(),
            runtimeEvent.Message,
            runtimeEvent.EncounterNote,
            new HashSet<Guid>(),
            null,
            linkedScenes);
    }

    private static EncounterHandoffV2 BuildFromJourney(
        StoredExpedition expedition,
        Guid? overworldId,
        Guid handoffId,
        string? returnPath,
        Guid eventOccurrenceId)
    {
        var occurrence = expedition.Journey.EventOccurrences.SingleOrDefault(value => value.Id == eventOccurrenceId)
            ?? throw new ArgumentException("The requested journey event occurrence does not exist.");
        if (occurrence.Status != JourneyEventStatus.Resolved || string.IsNullOrWhiteSpace(occurrence.EventKey))
        {
            throw new ArgumentException("Only a resolved journey event can generate an encounter handoff.");
        }

        var sourceConsequenceIds = occurrence.ConsequenceIds.ToHashSet();
        var circumstances = ProjectCircumstances(expedition, sourceConsequenceIds);
        if (circumstances.Count == 0)
        {
            throw new ArgumentException(
                "The requested journey event has no retained encounter-circumstance consequence.");
        }

        var history = expedition.Journey.History
            .LastOrDefault(value => value.EventOccurrenceId == occurrence.Id);
        var elapsed = history?.ExpeditionTime ?? TimeSpan.Zero;
        var process = occurrence.ProcessId.HasValue
            ? expedition.Journey.ActiveProcesses.Concat(expedition.Journey.ClosedProcesses)
                .SingleOrDefault(value => value.Id == occurrence.ProcessId.Value)
            : null;
        var journey = new EncounterHandoffJourneyProvenance(
            occurrence.Id,
            occurrence.ProcessId,
            process?.ProcessKey,
            process?.Definition.DestinationReference,
            process?.Definition.RouteReference,
            process?.Definition.LocationReference,
            occurrence.StageKey,
            occurrence.EventKey,
            occurrence.EventType,
            occurrence.TriggerReference,
            EncounterHandoffProvenance.From(occurrence.Provenance));

        return Build(
            expedition,
            handoffId,
            $"journey-event:{occurrence.Id:D}",
            returnPath,
            elapsed,
            expedition.Context.Kind == CrawlSessionContextKind.NonSpatial ? null : history?.CompletedWatches,
            overworldId,
            null,
            null,
            "JourneyEvent",
            occurrence.EventKey,
            occurrence.Note,
            sourceConsequenceIds,
            journey,
            []);
    }

    private static EncounterHandoffV2 Build(
        StoredExpedition expedition,
        Guid handoffId,
        string encounterOccurrenceId,
        string? returnPath,
        TimeSpan elapsed,
        int? watchNumber,
        Guid? overworldId,
        HexCoordinate? hex,
        EncounterHandoffLocation? location,
        string outcome,
        string summary,
        string? dmNote,
        IReadOnlySet<Guid> sourceConsequenceIds,
        EncounterHandoffJourneyProvenance? journey,
        IReadOnlyList<EncounterHandoffLinkedScene> linkedScenes)
    {
        var circumstances = sourceConsequenceIds.Count == 0
            ? Array.Empty<EncounterHandoffCircumstance>()
            : ProjectCircumstances(expedition, sourceConsequenceIds);
        var effects = ProjectEffects(expedition, sourceConsequenceIds);
        var resources = ProjectResources(expedition, sourceConsequenceIds);
        EnsureBounded(circumstances.Count, "encounter circumstances");
        EnsureBounded(effects.Count, "encounter effects");
        EnsureBounded(resources.Count, "encounter resources");
        EnsureBounded(linkedScenes.Count, "linked encounter scenes");

        return new EncounterHandoffV2(
            2,
            "hex-crawl",
            new EncounterHandoffIdentity(handoffId, encounterOccurrenceId, expedition.Id, expedition.Name),
            new EncounterHandoffReturnContext(returnPath),
            new EncounterHandoffTimeContext(
                Math.Max(1, (int)Math.Floor(elapsed.TotalHours / 24d) + 1),
                watchNumber,
                elapsed.TotalHours),
            new EncounterHandoffWorldContext(
                overworldId,
                hex,
                location),
            new EncounterHandoffEncounter(outcome, summary, dmNote),
            [],
            circumstances,
            effects,
            resources,
            journey,
            linkedScenes);
    }

    private static IReadOnlyList<EncounterHandoffCircumstance> ProjectCircumstances(
        StoredExpedition expedition,
        IReadOnlySet<Guid> sourceConsequenceIds) =>
        expedition.Effects.PendingConsequences
            .Where(value => sourceConsequenceIds.Contains(value.Consequence.Id))
            .SelectMany(pending => pending.UnresolvedComponents
                .OfType<EncounterCircumstanceConsequenceComponent>()
                .Select(component => new EncounterHandoffCircumstance(
                    pending.Consequence.Id,
                    pending.Consequence.ConsequenceKey,
                    component.CircumstanceKey,
                    component.Value,
                    new EncounterHandoffTarget(
                        pending.Consequence.Target.Scope,
                        pending.Consequence.Target.TargetId),
                    EncounterHandoffProvenance.From(pending.Consequence.Provenance))))
            .ToArray();

    private static IReadOnlyList<EncounterHandoffEffect> ProjectEffects(
        StoredExpedition expedition,
        IReadOnlySet<Guid> sourceConsequenceIds)
    {
        if (sourceConsequenceIds.Count == 0) return [];
        return expedition.Effects.ActiveEffects
            .Where(value => value.SourceConsequenceIds.Any(sourceConsequenceIds.Contains))
            .Select(value => new EncounterHandoffEffect(
                value.Id,
                value.EffectKey,
                new EncounterHandoffTarget(value.Target.Scope, value.Target.TargetId),
                value.Level,
                value.Magnitude,
                value.Unit,
                value.State,
                value.SourceConsequenceIds.Where(sourceConsequenceIds.Contains).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<EncounterHandoffResource> ProjectResources(
        StoredExpedition expedition,
        IReadOnlySet<Guid> sourceConsequenceIds)
    {
        if (sourceConsequenceIds.Count == 0) return [];
        var resourceIds = expedition.Resources.History
            .Where(value => value.ConsequenceId.HasValue && sourceConsequenceIds.Contains(value.ConsequenceId.Value))
            .Select(value => value.ResourceId)
            .ToHashSet();
        return expedition.Resources.Resources
            .Where(value => resourceIds.Contains(value.Id))
            .Select(value => new EncounterHandoffResource(
                value.Id,
                value.ResourceKey,
                new EncounterHandoffTarget(value.Target.Scope, value.Target.TargetId),
                value.InventoryModel,
                value.IsDepleted,
                value.Quantity,
                value.Unit,
                value.SymbolicState,
                value.SupplyDieSides))
            .ToArray();
    }

    private static IReadOnlyList<EncounterHandoffLinkedScene> ProjectLinkedScenes(
        CrawlRuntimeLocationSnapshot? location) =>
        location?.LinkedScenes
            .Select(value => new EncounterHandoffLinkedScene(value.Id, value.Kind, value.ReferenceKey))
            .ToArray()
        ?? [];

    private static string? ValidateReturnPath(string? value)
    {
        if (value is null) return null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048
            || value.Contains('\\') || value.Contains('\r') || value.Contains('\n')
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Encounter handoff return path is not safe.");
        }

        var question = value.IndexOf('?');
        var fragment = value.IndexOf('#');
        var end = question < 0 ? fragment : fragment < 0 ? question : Math.Min(question, fragment);
        var pathOnly = end < 0 ? value : value[..end];
        if (!string.Equals(pathOnly, "/tools/hex-crawl", StringComparison.Ordinal)
            && !pathOnly.StartsWith("/tools/hex-crawl/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Encounter handoff return path must remain inside Hex Crawl.");
        }
        return value;
    }

    private static void EnsureBounded(int count, string label)
    {
        if (count > MaximumContextItems)
        {
            throw new InvalidOperationException(
                $"The {label} collection is too large for encounter handoff transport.");
        }
    }
}
