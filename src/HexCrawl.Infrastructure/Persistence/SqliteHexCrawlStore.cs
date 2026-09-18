using HexCrawl.Application;
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

public sealed class SqliteHexCrawlStore(string connectionString) : IHexCrawlStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        new SqliteSchemaMigrator(connectionString).MigrateAsync(cancellationToken);

    public async Task<StoredOverworld> CreateOverworldAsync(
        StoredOverworld overworld,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO overworlds(id, owner_user_id, name, world_json, version, created_at, updated_at)
            VALUES($id, $owner, $name, $world, $version, $created, $updated);
            """;
        BindWorld(command, overworld);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return overworld;
    }

    public async Task<IReadOnlyList<OverworldSummary>> ListOverworldsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, version, created_at, updated_at
            FROM overworlds
            WHERE owner_user_id = $owner
            ORDER BY updated_at DESC, name;
            """;
        command.Parameters.AddWithValue("$owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<OverworldSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OverworldSummary(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                ParseDate(reader.GetString(3)),
                ParseDate(reader.GetString(4))));
        }
        return result;
    }

    public async Task<StoredOverworld?> GetOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT world_json, version, created_at, updated_at
            FROM overworlds
            WHERE id = $id AND owner_user_id = $owner;
            """;
        command.Parameters.AddWithValue("$id", overworldId.ToString("D"));
        command.Parameters.AddWithValue("$owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new StoredOverworld(
            Deserialize<WorldSnapshot>(reader.GetString(0)).ToDomain(),
            ownerUserId,
            reader.GetInt64(1),
            ParseDate(reader.GetString(2)),
            ParseDate(reader.GetString(3)));
    }

    public async Task<SaveResult<StoredOverworld>> SaveOverworldAsync(
        StoredOverworld overworld,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var currentVersion = await ReadVersionAsync(
            connection,
            transaction,
            "overworlds",
            overworld.World.Id,
            overworld.OwnerUserId,
            cancellationToken);
        if (!currentVersion.HasValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredOverworld>(SaveOutcome.NotFound, null);
        }
        if (currentVersion.Value != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredOverworld>(SaveOutcome.Conflict, null);
        }

        var updated = overworld with
        {
            Version = expectedVersion + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE overworlds
            SET name = $name, world_json = $world, version = $version, updated_at = $updated
            WHERE id = $id AND owner_user_id = $owner AND version = $expectedVersion;
            """;
        command.Parameters.AddWithValue("$id", updated.World.Id.ToString("D"));
        command.Parameters.AddWithValue("$owner", updated.OwnerUserId);
        command.Parameters.AddWithValue("$name", updated.World.Name);
        command.Parameters.AddWithValue("$world", Serialize(WorldSnapshot.FromDomain(updated.World)));
        command.Parameters.AddWithValue("$version", updated.Version);
        command.Parameters.AddWithValue("$updated", updated.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$expectedVersion", expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredOverworld>(SaveOutcome.Conflict, null);
        }
        await transaction.CommitAsync(cancellationToken);
        return new SaveResult<StoredOverworld>(SaveOutcome.Saved, updated);
    }

    public async Task<bool> HasExpeditionsAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM expeditions
                WHERE overworld_id = $world AND owner_user_id = $owner
            );
            """;
        command.Parameters.AddWithValue("$world", overworldId.ToString("D"));
        command.Parameters.AddWithValue("$owner", ownerUserId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

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
                procedure_json, pause_reason, remaining_watch_ticks, version, created_at, updated_at,
                generated_resolutions_json)
            VALUES(
                $id, $world, $context, $owner, $name, $state, $knowledge,
                $procedure, $pause, $remaining, $version, $created, $updated, $generatedResolutions);
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
            SELECT name, context_json, state_json, knowledge_json, procedure_json, pause_reason,
                   remaining_watch_ticks, version, created_at, updated_at, generated_resolutions_json
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
        var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(4));
        RuntimePauseReason? pauseReason = reader.IsDBNull(5)
            ? null
            : Enum.Parse<RuntimePauseReason>(reader.GetString(5), true);
        var remaining = TimeSpan.FromTicks(reader.GetInt64(6));
        var version = reader.GetInt64(7);
        var created = ParseDate(reader.GetString(8));
        var updated = ParseDate(reader.GetString(9));
        var generatedResolutions = Deserialize<IReadOnlyList<GeneratedProcedureResolution>>(reader.GetString(10));
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
            updated,
            generatedResolutions);
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
                procedure_json = $procedure,
                pause_reason = $pause,
                remaining_watch_ticks = $remaining,
                generated_resolutions_json = $generatedResolutions,
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
        command.Parameters.AddWithValue("$procedure", Serialize(updated.Procedure));
        command.Parameters.AddWithValue("$pause", updated.PauseReason?.ToString() is { } pause ? pause : DBNull.Value);
        command.Parameters.AddWithValue("$remaining", updated.RemainingWatchTime.Ticks);
        command.Parameters.AddWithValue("$generatedResolutions", Serialize(updated.GeneratedProcedureResolutions ?? []));
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

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<long?> ReadVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        Guid id,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        if (table is not ("overworlds" or "expeditions"))
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT version FROM {table} WHERE id = $id AND owner_user_id = $owner;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$owner", ownerUserId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
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

    private static void BindWorld(SqliteCommand command, StoredOverworld overworld)
    {
        command.Parameters.AddWithValue("$id", overworld.World.Id.ToString("D"));
        command.Parameters.AddWithValue("$owner", overworld.OwnerUserId);
        command.Parameters.AddWithValue("$name", overworld.World.Name);
        command.Parameters.AddWithValue("$world", Serialize(WorldSnapshot.FromDomain(overworld.World)));
        command.Parameters.AddWithValue("$version", overworld.Version);
        command.Parameters.AddWithValue("$created", overworld.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", overworld.UpdatedAt.ToString("O"));
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
        command.Parameters.AddWithValue("$procedure", Serialize(expedition.Procedure));
        command.Parameters.AddWithValue("$pause", expedition.PauseReason?.ToString() is { } pause ? pause : DBNull.Value);
        command.Parameters.AddWithValue("$remaining", expedition.RemainingWatchTime.Ticks);
        command.Parameters.AddWithValue("$generatedResolutions", Serialize(expedition.GeneratedProcedureResolutions ?? []));
        command.Parameters.AddWithValue("$version", expedition.Version);
        command.Parameters.AddWithValue("$created", expedition.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", expedition.UpdatedAt.ToString("O"));
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidDataException($"Persisted {typeof(T).Name} JSON was empty.");

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private enum RuntimeStateKind
    {
        Spatial,
        NonSpatial
    }

    private sealed record RuntimeStateSnapshot
    {
        public RuntimeStateKind Kind { get; init; } = RuntimeStateKind.Spatial;
        public Guid Id { get; init; }

        // Legacy v1 snapshots included OverworldId here. It is ignored after
        // migration because CrawlSessionContext is now authoritative.
        public Guid? OverworldId { get; init; }

        public WorldPoint? Position { get; init; }
        public WorldPositionPrecision? PositionPrecision { get; init; }
        public HexCoordinate? CurrentHex { get; init; }
        public int? EntryDirection { get; init; }
        public int? LastTravelDirection { get; init; }
        public DistanceMeasure? Progress { get; init; }
        public DistanceMeasure? CurrentExitRequirement { get; init; }
        public int? IntendedDirection { get; init; }
        public int? ActualDirection { get; init; }
        public bool IsLost { get; init; }
        public int VeerSteps { get; init; }
        public DistanceMeasure? DistanceTraveled { get; init; }
        public long ElapsedTravelTicks { get; init; }
        public int CompletedWatches { get; init; }
        public ActiveWatchSnapshot? ActiveWatch { get; init; }
        public NonSpatialActiveWatchSnapshot? NonSpatialActiveWatch { get; init; }

        public static RuntimeStateSnapshot FromDomain(CrawlSessionRuntimeState runtime) => runtime switch
        {
            ExpeditionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.Spatial,
                Id = state.Id,
                Position = state.Position,
                PositionPrecision = state.PositionPrecision,
                CurrentHex = state.Traversal.CurrentHex,
                EntryDirection = state.Traversal.EntryDirection?.Value,
                LastTravelDirection = state.Traversal.LastTravelDirection?.Value,
                Progress = state.Traversal.Progress,
                CurrentExitRequirement = state.Traversal.CurrentExitRequirement,
                IntendedDirection = state.IntendedDirection?.Value,
                ActualDirection = state.ActualDirection?.Value,
                IsLost = state.Navigation.IsLost,
                VeerSteps = state.Navigation.VeerSteps,
                DistanceTraveled = state.DistanceTraveled,
                ElapsedTravelTicks = state.ElapsedTravelTime.Ticks,
                CompletedWatches = state.CompletedWatches,
                ActiveWatch = state.ActiveWatch is null ? null : ActiveWatchSnapshot.FromDomain(state.ActiveWatch)
            },
            NonSpatialSessionState state => new RuntimeStateSnapshot
            {
                Kind = RuntimeStateKind.NonSpatial,
                Id = state.Id,
                ElapsedTravelTicks = state.ElapsedTime.Ticks,
                CompletedWatches = state.CompletedWatches,
                NonSpatialActiveWatch = state.ActiveWatch is null
                    ? null
                    : NonSpatialActiveWatchSnapshot.FromDomain(state.ActiveWatch)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(runtime))
        };

        public CrawlSessionRuntimeState ToDomain() => Kind switch
        {
            RuntimeStateKind.Spatial => ToSpatial(),
            RuntimeStateKind.NonSpatial => new NonSpatialSessionState
            {
                Id = Id,
                ElapsedTime = TimeSpan.FromTicks(ElapsedTravelTicks),
                CompletedWatches = CompletedWatches,
                ActiveWatch = NonSpatialActiveWatch?.ToDomain(),
                History = []
            },
            _ => throw new InvalidDataException("Persisted crawl session runtime kind is not supported.")
        };

        private ExpeditionState ToSpatial()
        {
            var currentHex = CurrentHex
                ?? throw new InvalidDataException("Persisted spatial crawl state has no current hex.");
            var progress = Progress
                ?? throw new InvalidDataException("Persisted spatial crawl state has no traversal progress.");
            var distance = DistanceTraveled
                ?? throw new InvalidDataException("Persisted spatial crawl state has no total distance.");
            return new ExpeditionState
            {
                Id = Id,
                Position = Position,
                PositionPrecision = PositionPrecision,
                Traversal = new HexTraversalState
                {
                    CurrentHex = currentHex,
                    EntryDirection = EntryDirection.HasValue ? new HexDirection(EntryDirection.Value) : null,
                    LastTravelDirection = LastTravelDirection.HasValue ? new HexDirection(LastTravelDirection.Value) : null,
                    Progress = progress,
                    CurrentExitRequirement = CurrentExitRequirement
                },
                IntendedDirection = IntendedDirection.HasValue ? new HexDirection(IntendedDirection.Value) : null,
                ActualDirection = ActualDirection.HasValue ? new HexDirection(ActualDirection.Value) : null,
                Navigation = new NavigationRuntimeState(IsLost, VeerSteps),
                DistanceTraveled = distance,
                ElapsedTravelTime = TimeSpan.FromTicks(ElapsedTravelTicks),
                CompletedWatches = CompletedWatches,
                ActiveWatch = ActiveWatch?.ToDomain(),
                History = []
            };
        }
    }

    private sealed record NonSpatialActiveWatchSnapshot(
        int WatchNumber,
        long TotalDurationTicks,
        long ElapsedTicks)
    {
        public static NonSpatialActiveWatchSnapshot FromDomain(NonSpatialActiveWatchState active) => new(
            active.WatchNumber,
            active.TotalDuration.Ticks,
            active.Elapsed.Ticks);

        public NonSpatialActiveWatchState ToDomain()
        {
            var state = new NonSpatialActiveWatchState(
                WatchNumber,
                TimeSpan.FromTicks(TotalDurationTicks),
                TimeSpan.FromTicks(ElapsedTicks));
            state.Validate();
            return state;
        }
    }

    private sealed record CrawlSessionContextSnapshot(
        CrawlSessionContextKind Kind,
        Guid? OverworldId = null,
        string? Name = null,
        HexOrientation? Orientation = null,
        DistanceMeasure? HexCenterDistance = null)
    {
        public static CrawlSessionContextSnapshot FromDomain(CrawlSessionContext context) => context switch
        {
            WorldBoundCrawlSessionContext world => new(context.Kind, world.WorldId),
            AbstractHexCrawlSessionContext hex => new(
                context.Kind,
                Name: hex.DisplayName,
                Orientation: hex.Orientation,
                HexCenterDistance: hex.HexContext.HexCenterDistance),
            NonSpatialCrawlSessionContext nonSpatial => new(
                context.Kind,
                Name: nonSpatial.DisplayName),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };

        public CrawlSessionContext ToDomain() => Kind switch
        {
            CrawlSessionContextKind.WorldBound => new WorldBoundCrawlSessionContext(
                OverworldId ?? throw new InvalidDataException("Persisted world-bound context has no overworld id.")),
            CrawlSessionContextKind.AbstractHex => new AbstractHexCrawlSessionContext(
                Name ?? "Abstract hex crawl",
                Orientation ?? HexOrientation.PointyTop,
                new CrawlRuntimeContext(
                    HexCenterDistance ?? throw new InvalidDataException("Persisted abstract-hex context has no hex-center distance."))),
            CrawlSessionContextKind.NonSpatial => new NonSpatialCrawlSessionContext(Name ?? "Non-spatial session"),
            _ => throw new InvalidDataException("Persisted crawl session context kind is not supported.")
        };
    }

    private sealed record ActiveWatchSnapshot(
        int WatchNumber,
        long TotalDurationTicks,
        long ElapsedTicks,
        WatchTravelPlanSnapshot Plan,
        ResolvedEncounterSnapshot Encounter,
        bool EncounterHandled,
        RuntimePauseReason? PendingDecision)
    {
        public static ActiveWatchSnapshot FromDomain(ActiveWatchState active) => new(
            active.WatchNumber,
            active.TotalDuration.Ticks,
            active.Elapsed.Ticks,
            WatchTravelPlanSnapshot.FromDomain(active.Plan),
            ResolvedEncounterSnapshot.FromDomain(active.Encounter),
            active.EncounterHandled,
            active.PendingDecision);

        public ActiveWatchState ToDomain() => new(
            WatchNumber,
            TimeSpan.FromTicks(TotalDurationTicks),
            TimeSpan.FromTicks(ElapsedTicks),
            Plan.ToDomain(),
            Encounter.ToDomain(),
            EncounterHandled,
            PendingDecision);
    }

    private sealed record WatchTravelPlanSnapshot(
        int IntendedDirection,
        string PaceKey,
        IReadOnlyList<string> Activities,
        string NavigationAidKey,
        bool SuppressesNavigationCheck,
        bool ResetsVeerAtBoundary,
        bool DeliberateDoubleBack,
        bool ContinueAcrossBoundaries)
    {
        public static WatchTravelPlanSnapshot FromDomain(WatchTravelPlan plan) => new(
            plan.IntendedDirection.Value,
            plan.Mode.PaceKey,
            plan.Mode.Activities.ToArray(),
            plan.NavigationAid.Key,
            plan.NavigationAid.SuppressesNavigationCheck,
            plan.NavigationAid.ResetsVeerAtBoundary,
            plan.DeliberateDoubleBack,
            plan.ContinueAcrossBoundaries);

        public WatchTravelPlan ToDomain() => new(
            new HexDirection(IntendedDirection),
            new TravelModeSelection(PaceKey, Activities.ToArray()),
            new NavigationAidSelection(NavigationAidKey, SuppressesNavigationCheck, ResetsVeerAtBoundary),
            DeliberateDoubleBack,
            ContinueAcrossBoundaries);
    }

    private sealed record ResolvedEncounterSnapshot(
        EncounterOutcomeKind Kind,
        long? OccursAtTicks,
        Guid? LocationId,
        string? Note,
        ResolutionSource Source,
        string? ProvenanceNote)
    {
        public static ResolvedEncounterSnapshot FromDomain(ResolvedEncounter encounter) => new(
            encounter.Kind,
            encounter.OccursAt?.Ticks,
            encounter.LocationId,
            encounter.Note,
            encounter.Provenance.Source,
            encounter.Provenance.Note);

        public ResolvedEncounter ToDomain() => new(
            Kind,
            OccursAtTicks.HasValue ? TimeSpan.FromTicks(OccursAtTicks.Value) : null,
            LocationId,
            Note,
            new ResolutionProvenance(Source, ProvenanceNote));
    }

    private sealed record FeatureSnapshot(
        Guid Id,
        string Name,
        string Category,
        SpatialFeatureKind Kind,
        WorldPoint? Position,
        IReadOnlyList<WorldPoint>? Path,
        IReadOnlyList<WorldPoint>? Boundary)
    {
        public static FeatureSnapshot FromDomain(SpatialFeature feature) => feature switch
        {
            PointFeature point => new(point.Id, point.Name, point.Category, point.Kind, point.Position, null, null),
            LinearFeature line => new(line.Id, line.Name, line.Category, line.Kind, null, line.Path, null),
            RegionFeature region => new(region.Id, region.Name, region.Category, region.Kind, null, null, region.Boundary),
            _ => throw new ArgumentOutOfRangeException(nameof(feature))
        };

        public SpatialFeature ToDomain() => Kind switch
        {
            SpatialFeatureKind.Point => new PointFeature(Id, Name, Category, Position ?? throw new InvalidDataException("Persisted point feature has no position.")),
            SpatialFeatureKind.Line => new LinearFeature(Id, Name, Category, Path ?? throw new InvalidDataException("Persisted line feature has no path.")),
            SpatialFeatureKind.Region => new RegionFeature(Id, Name, Category, Boundary ?? throw new InvalidDataException("Persisted region feature has no boundary.")),
            _ => throw new InvalidDataException("Persisted feature kind is not supported.")
        };
    }

    private sealed record WorldSnapshot(
        Guid Id,
        string Name,
        HexGridDefinition Grid,
        IReadOnlyList<FeatureSnapshot> Features,
        IReadOnlyList<Location> Locations,
        IReadOnlyList<SourceMapRepresentation> SourceMaps)
    {
        public static WorldSnapshot FromDomain(OverworldDefinition world) => new(
            world.Id,
            world.Name,
            world.Grid,
            world.Features.Select(FeatureSnapshot.FromDomain).ToArray(),
            world.Locations,
            world.SourceMaps);

        public OverworldDefinition ToDomain() => new()
        {
            Id = Id,
            Name = Name,
            Grid = Grid,
            Features = Features.Select(item => item.ToDomain()).ToArray(),
            Locations = Locations.ToArray(),
            SourceMaps = SourceMaps.ToArray()
        };
    }
}
