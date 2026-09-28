using System.Globalization;
using System.Text.Json.Nodes;
using HexCrawl.Application;
using HexCrawl.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.PersistenceMigration;

public sealed record MigrationVerificationReport(
    int SourceSchemaVersion,
    int OverworldCount,
    int ExpeditionCount,
    int EventCount,
    IReadOnlyList<string> ContextKinds);

public sealed class SqliteToPostgresMigrator(
    string sqliteConnectionString,
    string postgresConnectionString)
{
    public async Task<MigrationVerificationReport> MigrateAndVerifyAsync(
        CancellationToken cancellationToken = default)
    {
        var source = await LegacySqliteSnapshot.ReadAsync(sqliteConnectionString, cancellationToken);
        ValidateSourceRelationships(source);

        var store = new PostgresHexCrawlStore(postgresConnectionString);
        await store.InitializeAsync(cancellationToken);

        if (await TargetContainsDataAsync(cancellationToken))
        {
            try
            {
                return await VerifyAsync(source, store, cancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL target already contains Hex Crawl data that does not exactly match the SQLite source. " +
                    "The migration tool will not merge or overwrite it.",
                    exception);
            }
        }

        await ImportAsync(source, cancellationToken);
        return await VerifyAsync(source, store, cancellationToken);
    }

    public async Task<MigrationVerificationReport> VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        var source = await LegacySqliteSnapshot.ReadAsync(sqliteConnectionString, cancellationToken);
        ValidateSourceRelationships(source);
        var store = new PostgresHexCrawlStore(postgresConnectionString);
        try
        {
            if (!await store.IsReadyAsync(cancellationToken))
            {
                throw new InvalidDataException(
                    "The PostgreSQL target schema is not at the Hex Crawl version required for verification. " +
                    "Run the migration before using --verify-only.");
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            throw new InvalidDataException(
                "The PostgreSQL target schema is not initialized. Run the migration before using --verify-only.",
                exception);
        }
        return await VerifyAsync(source, store, cancellationToken);
    }

    private async Task ImportAsync(LegacySqliteSnapshot source, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var world in source.Overworlds)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO overworlds(id, owner_user_id, name, world_json, version, created_at, updated_at)
                    VALUES(@id, @owner, @name, @world, @version, @created, @updated);
                    """;
                command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, world.Id);
                command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, world.OwnerUserId);
                command.Parameters.AddWithValue("name", NpgsqlDbType.Text, world.Name);
                AddJsonb(command, "world", world.WorldJson);
                command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, world.Version);
                AddTimestamp(command, "created", world.CreatedAt);
                AddTimestamp(command, "updated", world.UpdatedAt);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var expedition in source.Expeditions)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO expeditions(
                        id, overworld_id, context_json, owner_user_id, name, state_json, knowledge_json,
                        party_json, generated_resolutions_json, procedure_json, procedure_origin_json,
                        pause_reason, remaining_watch_ticks, version, created_at, updated_at)
                    VALUES(
                        @id, @world, @context, @owner, @name, @state, @knowledge,
                        @party, @generated, @procedure, @origin,
                        @pause, @remaining, @version, @created, @updated);
                    """;
                command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expedition.Id);
                AddNullableUuid(command, "world", expedition.OverworldId);
                AddJsonb(command, "context", expedition.ContextJson);
                command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, expedition.OwnerUserId);
                command.Parameters.AddWithValue("name", NpgsqlDbType.Text, expedition.Name);
                AddJsonb(command, "state", expedition.StateJson);
                AddJsonb(command, "knowledge", expedition.KnowledgeJson);
                AddJsonb(command, "party", expedition.PartyJson);
                AddJsonb(command, "generated", expedition.GeneratedResolutionsJson);
                AddJsonb(command, "procedure", expedition.ProcedureJson);
                AddJsonb(command, "origin", expedition.ProcedureOriginJson);
                command.Parameters.AddWithValue("pause", NpgsqlDbType.Text, expedition.PauseReason is null ? DBNull.Value : expedition.PauseReason);
                command.Parameters.AddWithValue("remaining", NpgsqlDbType.Bigint, expedition.RemainingWatchTicks);
                command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, expedition.Version);
                AddTimestamp(command, "created", expedition.CreatedAt);
                AddTimestamp(command, "updated", expedition.UpdatedAt);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var runtimeEvent in source.Events.OrderBy(item => item.ExpeditionId).ThenBy(item => item.Sequence))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO expedition_events(
                        expedition_id, sequence, kind, subject_id, subject_type, event_json)
                    VALUES(@expedition, @sequence, @kind, @subjectId, @subjectType, @event);
                    """;
                command.Parameters.AddWithValue("expedition", NpgsqlDbType.Uuid, runtimeEvent.ExpeditionId);
                command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, runtimeEvent.Sequence);
                command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, runtimeEvent.Kind);
                AddNullableUuid(command, "subjectId", runtimeEvent.SubjectId);
                command.Parameters.AddWithValue("subjectType", NpgsqlDbType.Text, runtimeEvent.SubjectType is null ? DBNull.Value : runtimeEvent.SubjectType);
                AddJsonb(command, "event", runtimeEvent.EventJson);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<MigrationVerificationReport> VerifyAsync(
        LegacySqliteSnapshot source,
        PostgresHexCrawlStore store,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        var worldCount = await CountAsync(connection, "overworlds", cancellationToken);
        var expeditionCount = await CountAsync(connection, "expeditions", cancellationToken);
        var eventCount = await CountAsync(connection, "expedition_events", cancellationToken);
        Require(worldCount == source.Overworlds.Count, $"Overworld row count differs: SQLite={source.Overworlds.Count}, PostgreSQL={worldCount}.");
        Require(expeditionCount == source.Expeditions.Count, $"Expedition row count differs: SQLite={source.Expeditions.Count}, PostgreSQL={expeditionCount}.");
        Require(eventCount == source.Events.Count, $"Event row count differs: SQLite={source.Events.Count}, PostgreSQL={eventCount}.");

        var targetWorlds = await ReadTargetWorldsAsync(connection, cancellationToken);
        foreach (var world in source.Overworlds)
        {
            Require(targetWorlds.TryGetValue(world.Id, out var target), $"PostgreSQL is missing overworld {world.Id}.");
            Require(target!.OwnerUserId == world.OwnerUserId, $"Owner differs for overworld {world.Id}.");
            Require(target.Name == world.Name, $"Name differs for overworld {world.Id}.");
            Require(target.Version == world.Version, $"Version differs for overworld {world.Id}.");
            Require(JsonEquivalent(target.WorldJson, world.WorldJson), $"World snapshot differs for overworld {world.Id}.");
            Require(SameInstant(target.CreatedAt, world.CreatedAt), $"Created timestamp differs for overworld {world.Id}.");
            Require(SameInstant(target.UpdatedAt, world.UpdatedAt), $"Updated timestamp differs for overworld {world.Id}.");
        }

        var targetExpeditions = await ReadTargetExpeditionsAsync(connection, cancellationToken);
        foreach (var expedition in source.Expeditions)
        {
            Require(targetExpeditions.TryGetValue(expedition.Id, out var target), $"PostgreSQL is missing expedition {expedition.Id}.");
            Require(target!.OverworldId == expedition.OverworldId, $"Overworld reference differs for expedition {expedition.Id}.");
            Require(target.OwnerUserId == expedition.OwnerUserId, $"Owner differs for expedition {expedition.Id}.");
            Require(target.Name == expedition.Name, $"Name differs for expedition {expedition.Id}.");
            Require(target.Version == expedition.Version, $"Version differs for expedition {expedition.Id}.");
            Require(target.RemainingWatchTicks == expedition.RemainingWatchTicks, $"Remaining watch differs for expedition {expedition.Id}.");
            Require(target.PauseReason == expedition.PauseReason, $"Pause state differs for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.ContextJson, expedition.ContextJson), $"Context differs for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.StateJson, expedition.StateJson), $"Runtime state differs for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.KnowledgeJson, expedition.KnowledgeJson), $"Player knowledge differs for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.PartyJson, expedition.PartyJson), $"Party state differs for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.GeneratedResolutionsJson, expedition.GeneratedResolutionsJson), $"Generated resolutions differ for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.ProcedureJson, expedition.ProcedureJson), $"Procedure snapshot differs for expedition {expedition.Id}.");
            Require(JsonEquivalent(target.ProcedureOriginJson, expedition.ProcedureOriginJson), $"Procedure origin differs for expedition {expedition.Id}.");
            Require(SameInstant(target.CreatedAt, expedition.CreatedAt), $"Created timestamp differs for expedition {expedition.Id}.");
            Require(SameInstant(target.UpdatedAt, expedition.UpdatedAt), $"Updated timestamp differs for expedition {expedition.Id}.");
        }

        var targetEvents = await ReadTargetEventsAsync(connection, cancellationToken);
        foreach (var sourceGroup in source.Events.GroupBy(item => item.ExpeditionId))
        {
            var expected = sourceGroup.OrderBy(item => item.Sequence).ToArray();
            Require(targetEvents.TryGetValue(sourceGroup.Key, out var actual), $"PostgreSQL is missing event history for expedition {sourceGroup.Key}.");
            Require(actual!.Count == expected.Length, $"Event count differs for expedition {sourceGroup.Key}.");
            Require(actual.Select(item => item.Sequence).Distinct().Count() == actual.Count, $"Duplicate event sequence exists for expedition {sourceGroup.Key}.");
            Require(actual[0].Sequence == expected[0].Sequence, $"Minimum event sequence differs for expedition {sourceGroup.Key}.");
            Require(actual[^1].Sequence == expected[^1].Sequence, $"Maximum event sequence differs for expedition {sourceGroup.Key}.");
            for (var index = 0; index < expected.Length; index++)
            {
                var left = expected[index];
                var right = actual[index];
                Require(left.Sequence == right.Sequence, $"Event sequence ordering differs for expedition {sourceGroup.Key}.");
                Require(left.Kind == right.Kind, $"Event kind differs for expedition {sourceGroup.Key}, sequence {left.Sequence}.");
                Require(left.SubjectId == right.SubjectId, $"Event subject ID differs for expedition {sourceGroup.Key}, sequence {left.Sequence}.");
                Require(left.SubjectType == right.SubjectType, $"Event subject type differs for expedition {sourceGroup.Key}, sequence {left.Sequence}.");
                Require(JsonEquivalent(left.EventJson, right.EventJson), $"Event payload differs for expedition {sourceGroup.Key}, sequence {left.Sequence}.");
            }
        }

        await RequireNoOrphansAsync(connection, cancellationToken);

        // This is intentionally an application-path verification, not merely a JSON syntax check.
        // Every aggregate must deserialize through the production PostgreSQL store and application service.
        var service = new HexCrawlService(store);
        foreach (var world in source.Overworlds)
        {
            var loaded = await service.GetOverworldAsync(world.Id, world.OwnerUserId, cancellationToken);
            Require(loaded.Version == world.Version, $"Application-path world version differs for {world.Id}.");
        }

        var contextKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expedition in source.Expeditions)
        {
            var loaded = await service.GetExpeditionAsync(expedition.Id, expedition.OwnerUserId, cancellationToken);
            contextKinds.Add(loaded.Context.Kind.ToString());
            var expectedEvents = source.Events.Count(item => item.ExpeditionId == expedition.Id);
            Require(loaded.Runtime.History.Count == expectedEvents, $"Application-path event history differs for expedition {expedition.Id}.");
            Require(loaded.Version == expedition.Version, $"Application-path expedition version differs for {expedition.Id}.");
        }

        return new MigrationVerificationReport(
            source.SchemaVersion,
            source.Overworlds.Count,
            source.Expeditions.Count,
            source.Events.Count,
            contextKinds.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private async Task<bool> TargetContainsDataAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await CountAsync(connection, "overworlds", cancellationToken) > 0
            || await CountAsync(connection, "expeditions", cancellationToken) > 0
            || await CountAsync(connection, "expedition_events", cancellationToken) > 0;
    }

    private static void ValidateSourceRelationships(LegacySqliteSnapshot source)
    {
        var worldIds = source.Overworlds.Select(item => item.Id).ToHashSet();
        var expeditionIds = source.Expeditions.Select(item => item.Id).ToHashSet();
        foreach (var expedition in source.Expeditions)
        {
            if (expedition.OverworldId is { } worldId && !worldIds.Contains(worldId))
            {
                throw new InvalidDataException($"SQLite expedition {expedition.Id} references missing overworld {worldId}.");
            }
        }
        foreach (var runtimeEvent in source.Events)
        {
            if (!expeditionIds.Contains(runtimeEvent.ExpeditionId))
            {
                throw new InvalidDataException($"SQLite event references missing expedition {runtimeEvent.ExpeditionId}.");
            }
        }
    }

    private static async Task RequireNoOrphansAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM expeditions e LEFT JOIN overworlds o ON o.id = e.overworld_id
                 WHERE e.overworld_id IS NOT NULL AND o.id IS NULL),
                (SELECT COUNT(*) FROM expedition_events ee LEFT JOIN expeditions e ON e.id = ee.expedition_id
                 WHERE e.id IS NULL);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        Require(reader.GetInt64(0) == 0, "PostgreSQL contains an invalid expedition-to-overworld reference.");
        Require(reader.GetInt64(1) == 0, "PostgreSQL contains an invalid event-to-expedition reference.");
    }

    private static async Task<long> CountAsync(
        NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        if (table is not ("overworlds" or "expeditions" or "expedition_events"))
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<Dictionary<Guid, LegacyOverworld>> ReadTargetWorldsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, owner_user_id, name, world_json::text, version, created_at, updated_at FROM overworlds;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, LegacyOverworld>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new LegacyOverworld(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(5)),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(6)));
            result.Add(item.Id, item);
        }
        return result;
    }

    private static async Task<Dictionary<Guid, LegacyExpedition>> ReadTargetExpeditionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, overworld_id, context_json::text, owner_user_id, name, state_json::text,
                   knowledge_json::text, party_json::text, generated_resolutions_json::text,
                   procedure_json::text, procedure_origin_json::text, pause_reason,
                   remaining_watch_ticks, version, created_at, updated_at
            FROM expeditions;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, LegacyExpedition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new LegacyExpedition(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetInt64(12),
                reader.GetInt64(13),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(14)),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(15)));
            result.Add(item.Id, item);
        }
        return result;
    }

    private static async Task<Dictionary<Guid, List<LegacyEvent>>> ReadTargetEventsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT expedition_id, sequence, kind, subject_id, subject_type, event_json::text
            FROM expedition_events
            ORDER BY expedition_id, sequence;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, List<LegacyEvent>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new LegacyEvent(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5));
            if (!result.TryGetValue(item.ExpeditionId, out var events))
            {
                events = [];
                result.Add(item.ExpeditionId, events);
            }
            events.Add(item);
        }
        return result;
    }

    private static bool JsonEquivalent(string? left, string? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
    }

    private static bool SameInstant(DateTimeOffset left, DateTimeOffset right)
    {
        // PostgreSQL timestamptz stores microsecond precision while .NET/SQLite can retain 100 ns ticks.
        // Treat sub-microsecond differences as equivalent during migration verification.
        const long ticksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;
        var deltaTicks = left.ToUniversalTime().Ticks - right.ToUniversalTime().Ticks;
        return Math.Abs(deltaTicks) < ticksPerMicrosecond;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void AddJsonb(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = value is null ? DBNull.Value : value
        });
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });
    }

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value.UtcDateTime);

    private sealed record LegacyOverworld(
        Guid Id,
        string OwnerUserId,
        string Name,
        string WorldJson,
        long Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record LegacyExpedition(
        Guid Id,
        Guid? OverworldId,
        string ContextJson,
        string OwnerUserId,
        string Name,
        string StateJson,
        string? KnowledgeJson,
        string PartyJson,
        string GeneratedResolutionsJson,
        string ProcedureJson,
        string? ProcedureOriginJson,
        string? PauseReason,
        long RemainingWatchTicks,
        long Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record LegacyEvent(
        Guid ExpeditionId,
        long Sequence,
        string Kind,
        Guid? SubjectId,
        string? SubjectType,
        string EventJson);

    private sealed record LegacySqliteSnapshot(
        int SchemaVersion,
        IReadOnlyList<LegacyOverworld> Overworlds,
        IReadOnlyList<LegacyExpedition> Expeditions,
        IReadOnlyList<LegacyEvent> Events)
    {
        public static async Task<LegacySqliteSnapshot> ReadAsync(
            string connectionString,
            CancellationToken cancellationToken)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString)
            {
                Mode = SqliteOpenMode.ReadOnly
            };
            await using var connection = new SqliteConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var schemaVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
            if (schemaVersion is < 1 or > 5)
            {
                throw new InvalidDataException(
                    $"Unsupported Hex Crawl SQLite schema version {schemaVersion}. Supported versions are 1 through 5.");
            }

            var columns = await ReadExpeditionColumnsAsync(connection, cancellationToken);
            var worlds = await ReadWorldsAsync(connection, cancellationToken);
            var expeditions = await ReadExpeditionsAsync(connection, columns, cancellationToken);
            var events = await ReadEventsAsync(connection, cancellationToken);
            return new LegacySqliteSnapshot(schemaVersion, worlds, expeditions, events);
        }

        private static async Task<int> ReadSchemaVersionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        private static async Task<HashSet<string>> ReadExpeditionColumnsAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(expeditions);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(1));
            return result;
        }

        private static async Task<IReadOnlyList<LegacyOverworld>> ReadWorldsAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, owner_user_id, name, world_json, version, created_at, updated_at FROM overworlds ORDER BY id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<LegacyOverworld>();
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new LegacyOverworld(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                    ParseDate(reader.GetString(5)), ParseDate(reader.GetString(6))));
            }
            return result;
        }

        private static async Task<IReadOnlyList<LegacyExpedition>> ReadExpeditionsAsync(
            SqliteConnection connection,
            HashSet<string> columns,
            CancellationToken cancellationToken)
        {
            var context = columns.Contains("context_json") ? "context_json" : "NULL AS context_json";
            var party = columns.Contains("party_json") ? "party_json" : "'{}' AS party_json";
            var generated = columns.Contains("generated_resolutions_json") ? "generated_resolutions_json" : "'[]' AS generated_resolutions_json";
            var origin = columns.Contains("procedure_origin_json") ? "procedure_origin_json" : "NULL AS procedure_origin_json";
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT id, overworld_id, {context}, owner_user_id, name, state_json, knowledge_json,
                       {party}, {generated}, procedure_json, {origin}, pause_reason,
                       remaining_watch_ticks, version, created_at, updated_at
                FROM expeditions
                ORDER BY id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<LegacyExpedition>();
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid? overworldId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1));
                var contextJson = reader.IsDBNull(2)
                    ? overworldId.HasValue
                        ? $"{{\"kind\":\"WorldBound\",\"overworldId\":\"{overworldId.Value:D}\"}}"
                        : throw new InvalidDataException("Legacy SQLite expedition has neither context_json nor overworld_id.")
                    : reader.GetString(2);
                result.Add(new LegacyExpedition(
                    Guid.Parse(reader.GetString(0)),
                    overworldId,
                    contextJson,
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetInt64(12),
                    reader.GetInt64(13),
                    ParseDate(reader.GetString(14)),
                    ParseDate(reader.GetString(15))));
            }
            return result;
        }

        private static async Task<IReadOnlyList<LegacyEvent>> ReadEventsAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT expedition_id, sequence, kind, subject_id, subject_type, event_json
                FROM expedition_events
                ORDER BY expedition_id, sequence;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<LegacyEvent>();
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new LegacyEvent(
                    Guid.Parse(reader.GetString(0)), reader.GetInt64(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
            }
            return result;
        }

        private static DateTimeOffset ParseDate(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
