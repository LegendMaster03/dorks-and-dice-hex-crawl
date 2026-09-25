using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using Microsoft.Data.Sqlite;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class SqliteHexCrawlStore
{
    public async Task<StoredExpedition> CreateExpeditionAsync(
        StoredExpedition expedition,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO expeditions(
                id, overworld_id, context_json, owner_user_id, name, state_json, knowledge_json,
                party_json, procedure_json, pause_reason, remaining_watch_ticks, version, created_at, updated_at)
            VALUES(
                $id, $world, $context, $owner, $name, $state, $knowledge,
                $party, $procedure, $pause, $remaining, $version, $created, $updated);
            """;
        BindExpedition(command, expedition);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await InsertEventsAsync(connection, transaction, expedition.Runtime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expedition;
    }

    public async Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, context_json, name, procedure_json, version, created_at, updated_at
            FROM expeditions
            WHERE owner_user_id = $owner
            ORDER BY updated_at DESC, name;
            """;
        command.Parameters.AddWithValue("$owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ExpeditionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var context = Deserialize<CrawlSessionContextSnapshot>(reader.GetString(1)).ToDomain();
            var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(3));
            result.Add(new ExpeditionSummary(
                Guid.Parse(reader.GetString(0)),
                context,
                reader.GetString(2),
                procedure.Name,
                reader.GetInt64(4),
                ParseDate(reader.GetString(5)),
                ParseDate(reader.GetString(6))));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, procedure_json, version, created_at, updated_at
            FROM expeditions
            WHERE overworld_id = $world AND owner_user_id = $owner
            ORDER BY updated_at DESC, name;
            """;
        command.Parameters.AddWithValue("$world", overworldId.ToString("D"));
        command.Parameters.AddWithValue("$owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ExpeditionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(2));
            result.Add(new ExpeditionSummary(
                Guid.Parse(reader.GetString(0)),
                new WorldBoundCrawlSessionContext(overworldId),
                reader.GetString(1),
                procedure.Name,
                reader.GetInt64(3),
                ParseDate(reader.GetString(4)),
                ParseDate(reader.GetString(5))));
        }
        return result;
    }

    public async Task<StoredExpedition?> GetExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, context_json, state_json, knowledge_json, party_json, procedure_json, pause_reason,
                   remaining_watch_ticks, version, created_at, updated_at
            FROM expeditions
            WHERE id = $id AND owner_user_id = $owner;
            """;
        command.Parameters.AddWithValue("$id", expeditionId.ToString("D"));
        command.Parameters.AddWithValue("$owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var name = reader.GetString(0);
        var context = Deserialize<CrawlSessionContextSnapshot>(reader.GetString(1)).ToDomain();
        var runtime = Deserialize<RuntimeStateSnapshot>(reader.GetString(2)).ToDomain();
        var knowledge = reader.IsDBNull(3) ? null : Deserialize<PlayerKnowledgeState>(reader.GetString(3));
        var party = Deserialize<CrawlPartySheet>(reader.GetString(4));
        party.Validate();
        var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(5));
        RuntimePauseReason? pauseReason = reader.IsDBNull(6)
            ? null
            : Enum.Parse<RuntimePauseReason>(reader.GetString(6), true);
        var remaining = TimeSpan.FromTicks(reader.GetInt64(7));
        var version = reader.GetInt64(8);
        var created = ParseDate(reader.GetString(9));
        var updated = ParseDate(reader.GetString(10));
        await reader.CloseAsync();
        var events = await ReadEventsAsync(connection, expeditionId, cancellationToken);
        runtime = runtime switch
        {
            ExpeditionState spatial => spatial with { History = events },
            NonSpatialSessionState nonSpatial => nonSpatial with { History = events },
            _ => throw new InvalidDataException("Persisted crawl session runtime kind is not supported.")
        };
        return new StoredExpedition(
            name,
            runtime,
            context,
            knowledge,
            procedure,
            pauseReason,
            remaining,
            ownerUserId,
            version,
            created,
            updated)
        {
            Party = party
        };
    }

    public async Task<SaveResult<StoredExpedition>> SaveExpeditionAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var currentVersion = await ReadVersionAsync(
            connection,
            transaction,
            "expeditions",
            expedition.Id,
            expedition.OwnerUserId,
            cancellationToken);
        if (!currentVersion.HasValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredExpedition>(SaveOutcome.NotFound, null);
        }
        if (currentVersion.Value != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredExpedition>(SaveOutcome.Conflict, null);
        }

        var updated = expedition with
        {
            Version = expectedVersion + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE expeditions
            SET overworld_id = $world,
                context_json = $context,
                name = $name,
                state_json = $state,
                knowledge_json = $knowledge,
                party_json = $party,
                procedure_json = $procedure,
                pause_reason = $pause,
                remaining_watch_ticks = $remaining,
                version = $version,
                updated_at = $updated
            WHERE id = $id AND owner_user_id = $owner AND version = $expectedVersion;
            """;
        command.Parameters.AddWithValue("$id", updated.Id.ToString("D"));
        command.Parameters.AddWithValue("$world", updated.Context.OverworldId?.ToString("D") is { } worldId ? worldId : DBNull.Value);
        command.Parameters.AddWithValue("$context", Serialize(CrawlSessionContextSnapshot.FromDomain(updated.Context)));
        command.Parameters.AddWithValue("$owner", updated.OwnerUserId);
        command.Parameters.AddWithValue("$name", updated.Name);
        command.Parameters.AddWithValue("$state", Serialize(RuntimeStateSnapshot.FromDomain(updated.Runtime)));
        command.Parameters.AddWithValue("$knowledge", updated.Knowledge is null ? DBNull.Value : Serialize(updated.Knowledge));
        updated.Party.Validate();
        command.Parameters.AddWithValue("$party", Serialize(updated.Party));
        command.Parameters.AddWithValue("$procedure", Serialize(updated.Procedure));
        command.Parameters.AddWithValue("$pause", updated.PauseReason?.ToString() is { } pause ? pause : DBNull.Value);
        command.Parameters.AddWithValue("$remaining", updated.RemainingWatchTime.Ticks);
        command.Parameters.AddWithValue("$version", updated.Version);
        command.Parameters.AddWithValue("$updated", updated.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$expectedVersion", expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredExpedition>(SaveOutcome.Conflict, null);
        }
        await InsertEventsAsync(connection, transaction, updated.Runtime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SaveResult<StoredExpedition>(SaveOutcome.Saved, updated);
    }

    private static async Task<IReadOnlyList<CrawlRuntimeEvent>> ReadEventsAsync(
        SqliteConnection connection,
        Guid expeditionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_json
            FROM expedition_events
            WHERE expedition_id = $id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$id", expeditionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<CrawlRuntimeEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(Deserialize<CrawlRuntimeEvent>(reader.GetString(0)));
        }
        return events;
    }

    private static async Task InsertEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CrawlSessionRuntimeState state,
        CancellationToken cancellationToken)
    {
        foreach (var runtimeEvent in state.History)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO expedition_events(
                    expedition_id, sequence, kind, subject_id, subject_type, event_json)
                VALUES($expedition, $sequence, $kind, $subjectId, $subjectType, $event);
                """;
            command.Parameters.AddWithValue("$expedition", state.Id.ToString("D"));
            command.Parameters.AddWithValue("$sequence", runtimeEvent.Sequence);
            command.Parameters.AddWithValue("$kind", runtimeEvent.Kind.ToString());
            command.Parameters.AddWithValue("$subjectId", runtimeEvent.SubjectId?.ToString("D") is { } subjectId ? subjectId : DBNull.Value);
            command.Parameters.AddWithValue("$subjectType", runtimeEvent.SubjectType?.ToString() is { } subjectType ? subjectType : DBNull.Value);
            command.Parameters.AddWithValue("$event", Serialize(runtimeEvent));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void BindExpedition(SqliteCommand command, StoredExpedition expedition)
    {
        command.Parameters.AddWithValue("$id", expedition.Id.ToString("D"));
        command.Parameters.AddWithValue("$world", expedition.Context.OverworldId?.ToString("D") is { } worldId ? worldId : DBNull.Value);
        command.Parameters.AddWithValue("$context", Serialize(CrawlSessionContextSnapshot.FromDomain(expedition.Context)));
        command.Parameters.AddWithValue("$owner", expedition.OwnerUserId);
        command.Parameters.AddWithValue("$name", expedition.Name);
        command.Parameters.AddWithValue("$state", Serialize(RuntimeStateSnapshot.FromDomain(expedition.Runtime)));
        command.Parameters.AddWithValue("$knowledge", expedition.Knowledge is null ? DBNull.Value : Serialize(expedition.Knowledge));
        expedition.Party.Validate();
        command.Parameters.AddWithValue("$party", Serialize(expedition.Party));
        command.Parameters.AddWithValue("$procedure", Serialize(expedition.Procedure));
        command.Parameters.AddWithValue("$pause", expedition.PauseReason?.ToString() is { } pause ? pause : DBNull.Value);
        command.Parameters.AddWithValue("$remaining", expedition.RemainingWatchTime.Ticks);
        command.Parameters.AddWithValue("$version", expedition.Version);
        command.Parameters.AddWithValue("$created", expedition.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", expedition.UpdatedAt.ToString("O"));
    }


}
