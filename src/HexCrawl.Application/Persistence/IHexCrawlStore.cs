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
    ExpeditionState State,
    PlayerKnowledgeState Knowledge,
    CrawlProcedureProfile Procedure,
    RuntimePauseReason? PauseReason,
    TimeSpan RemainingWatchTime,
    string OwnerUserId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExpeditionSummary(
    Guid Id,
    Guid OverworldId,
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
