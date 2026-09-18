using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Persistence;

public sealed record StoredOverworld(
    OverworldDefinition World,
    string OwnerUserId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OverworldSummary(
    Guid Id,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StoredExpedition(
    string Name,
    CrawlSessionRuntimeState Runtime,
    CrawlSessionContext Context,
    PlayerKnowledgeState? Knowledge,
    CrawlProcedureProfile Procedure,
    RuntimePauseReason? PauseReason,
    TimeSpan RemainingWatchTime,
    string OwnerUserId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public Guid Id => Runtime.Id;

    public ExpeditionState State => Runtime as ExpeditionState
        ?? throw new InvalidOperationException("This crawl session does not have spatial expedition state.");

    public NonSpatialSessionState NonSpatialState => Runtime as NonSpatialSessionState
        ?? throw new InvalidOperationException("This crawl session is spatial.");

    public PlayerKnowledgeState RequireKnowledge() => Knowledge
        ?? throw new InvalidOperationException("This crawl session does not have world-bound player knowledge.");
}

public sealed record ExpeditionSummary(
    Guid Id,
    CrawlSessionContext Context,
    string Name,
    string ProcedureName,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum SaveOutcome
{
    Saved,
    NotFound,
    Conflict
}

public sealed record SaveResult<T>(SaveOutcome Outcome, T? Value);

public interface IHexCrawlStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<StoredOverworld> CreateOverworldAsync(
        StoredOverworld overworld,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverworldSummary>> ListOverworldsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<StoredOverworld?> GetOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<SaveResult<StoredOverworld>> SaveOverworldAsync(
        StoredOverworld overworld,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    Task<bool> HasExpeditionsAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<StoredExpedition> CreateExpeditionAsync(
        StoredExpedition expedition,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<StoredExpedition?> GetExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<SaveResult<StoredExpedition>> SaveExpeditionAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
