using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class PostgresHexCrawlStore
{
    public async Task<StoredExpedition> CreateExpeditionAsync(
        StoredExpedition expedition,
        CancellationToken cancellationToken = default)
    {
        ExpeditionProcedureConsistency.ValidateForPersistence(expedition);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO expeditions(
                id, overworld_id, context_json, owner_user_id, name, state_json, knowledge_json,
                party_json, generated_resolutions_json, procedure_json, procedure_origin_json, campaign_procedure_json, pause_reason,
                remaining_watch_ticks, version, created_at, updated_at)
            VALUES(
                @id, @world, @context, @owner, @name, @state, @knowledge,
                @party, @generatedResolutions, @procedure, @procedureOrigin, @campaignProcedure, @pause,
                @remaining, @version, @created, @updated);
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
            SELECT id, context_json::text, name, procedure_json::text, version, created_at, updated_at
            FROM expeditions
            WHERE owner_user_id = @owner
            ORDER BY updated_at DESC, name;
            """;
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ExpeditionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var context = Deserialize<CrawlSessionContextSnapshot>(reader.GetString(1)).ToDomain();
            var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(3));
            result.Add(new ExpeditionSummary(
                reader.GetGuid(0),
                context,
                reader.GetString(2),
                procedure.Name,
                reader.GetInt64(4),
                ReadTimestamp(reader, 5),
                ReadTimestamp(reader, 6)));
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
            SELECT id, name, procedure_json::text, version, created_at, updated_at
            FROM expeditions
            WHERE overworld_id = @world AND owner_user_id = @owner
            ORDER BY updated_at DESC, name;
            """;
        command.Parameters.AddWithValue("world", NpgsqlDbType.Uuid, overworldId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ExpeditionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(2));
            result.Add(new ExpeditionSummary(
                reader.GetGuid(0),
                new WorldBoundCrawlSessionContext(overworldId),
                reader.GetString(1),
                procedure.Name,
                reader.GetInt64(3),
                ReadTimestamp(reader, 4),
                ReadTimestamp(reader, 5)));
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
            SELECT name, context_json::text, state_json::text, knowledge_json::text, party_json::text,
                   generated_resolutions_json::text, procedure_json::text, procedure_origin_json::text,
                   campaign_procedure_json::text, pause_reason, remaining_watch_ticks, version, created_at, updated_at
            FROM expeditions
            WHERE id = @id AND owner_user_id = @owner;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expeditionId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var name = reader.GetString(0);
        var contextSnapshot = Deserialize<CrawlSessionContextSnapshot>(reader.GetString(1));
        var context = contextSnapshot.ToDomain();
        var runtime = Deserialize<RuntimeStateSnapshot>(reader.GetString(2)).ToDomain();
        var knowledge = reader.IsDBNull(3) ? null : Deserialize<PlayerKnowledgeState>(reader.GetString(3));
        var party = Deserialize<CrawlPartySheet>(reader.GetString(4));
        party.Validate();
        var generatedResolutions = Deserialize<IReadOnlyList<GeneratedProcedureResolution>>(reader.GetString(5));
        var procedure = Deserialize<CrawlProcedureProfile>(reader.GetString(6));
        var procedureOrigin = reader.IsDBNull(7)
            ? null
            : Deserialize<ProcedureOriginMetadata>(reader.GetString(7));
        var campaignProcedure = reader.IsDBNull(8)
            ? null
            : Deserialize<CampaignProcedure>(reader.GetString(8));
        RuntimePauseReason? pauseReason = reader.IsDBNull(9)
            ? null
            : Enum.Parse<RuntimePauseReason>(reader.GetString(9), true);
        var remaining = TimeSpan.FromTicks(reader.GetInt64(10));
        var version = reader.GetInt64(11);
        var created = ReadTimestamp(reader, 12);
        var updated = ReadTimestamp(reader, 13);
        await reader.CloseAsync();
        var events = await ReadEventsAsync(connection, expeditionId, cancellationToken);
        runtime = runtime switch
        {
            ExpeditionState spatial => spatial with { History = events },
            NonSpatialSessionState nonSpatial => nonSpatial with { History = events },
            _ => throw new InvalidDataException("Persisted crawl session runtime kind is not supported.")
        };
        var stored = new StoredExpedition(
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
            Party = party,
            GeneratedProcedureResolutions = generatedResolutions,
            CampaignId = contextSnapshot.CampaignId,
            ProcedureOrigin = procedureOrigin,
            CampaignProcedure = campaignProcedure
        };
        ExpeditionProcedureConsistency.ValidateLoaded(stored);
        return stored;
    }

    public async Task<SaveResult<StoredExpedition>> SaveExpeditionAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ExpeditionProcedureConsistency.ValidateForPersistence(expedition);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = expedition with
        {
            Version = expectedVersion + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE expeditions
            SET overworld_id = @world,
                context_json = @context,
                name = @name,
                state_json = @state,
                knowledge_json = @knowledge,
                party_json = @party,
                generated_resolutions_json = @generatedResolutions,
                procedure_json = @procedure,
                procedure_origin_json = @procedureOrigin,
                campaign_procedure_json = @campaignProcedure,
                pause_reason = @pause,
                remaining_watch_ticks = @remaining,
                version = @version,
                updated_at = @updated
            WHERE id = @id AND owner_user_id = @owner AND version = @expectedVersion;
            """;
        BindMutableExpedition(command, updated);
        command.Parameters.AddWithValue("expectedVersion", NpgsqlDbType.Bigint, expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            var exists = await ExistsAsync(connection, transaction, "expeditions", expedition.Id, expedition.OwnerUserId, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return new SaveResult<StoredExpedition>(exists ? SaveOutcome.Conflict : SaveOutcome.NotFound, null);
        }

        await InsertEventsAsync(connection, transaction, updated.Runtime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SaveResult<StoredExpedition>(SaveOutcome.Saved, updated);
    }

    public async Task<DeleteExpeditionOutcome> DeleteExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM expeditions
            WHERE id = @id AND owner_user_id = @owner AND version = @expectedVersion;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expeditionId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        command.Parameters.AddWithValue("expectedVersion", NpgsqlDbType.Bigint, expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return DeleteExpeditionOutcome.Deleted;
        }

        var exists = await ExistsAsync(connection, transaction, "expeditions", expeditionId, ownerUserId, cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return exists ? DeleteExpeditionOutcome.Conflict : DeleteExpeditionOutcome.NotFound;
    }

    private static async Task<IReadOnlyList<CrawlRuntimeEvent>> ReadEventsAsync(
        NpgsqlConnection connection,
        Guid expeditionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_json::text
            FROM expedition_events
            WHERE expedition_id = @id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expeditionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<CrawlRuntimeEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(Deserialize<CrawlRuntimeEvent>(reader.GetString(0)));
        }
        return events;
    }

    private static async Task InsertEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CrawlSessionRuntimeState state,
        CancellationToken cancellationToken)
    {
        foreach (var runtimeEvent in state.History)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO expedition_events(
                    expedition_id, sequence, kind, subject_id, subject_type, event_json)
                VALUES(@expedition, @sequence, @kind, @subjectId, @subjectType, @event)
                ON CONFLICT (expedition_id, sequence) DO NOTHING;
                """;
            command.Parameters.AddWithValue("expedition", NpgsqlDbType.Uuid, state.Id);
            command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, runtimeEvent.Sequence);
            command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, runtimeEvent.Kind.ToString());
            command.Parameters.AddWithValue("subjectId", NpgsqlDbType.Uuid, runtimeEvent.SubjectId.HasValue ? runtimeEvent.SubjectId.Value : DBNull.Value);
            command.Parameters.AddWithValue("subjectType", NpgsqlDbType.Text, runtimeEvent.SubjectType?.ToString() is { } subjectType ? subjectType : DBNull.Value);
            AddJsonb(command, "event", Serialize(runtimeEvent));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void BindExpedition(NpgsqlCommand command, StoredExpedition expedition)
    {
        BindMutableExpedition(command, expedition);
        AddTimestamp(command, "created", expedition.CreatedAt);
    }

    private static void BindMutableExpedition(NpgsqlCommand command, StoredExpedition expedition)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expedition.Id);
        command.Parameters.AddWithValue("world", NpgsqlDbType.Uuid, expedition.Context.OverworldId.HasValue ? expedition.Context.OverworldId.Value : DBNull.Value);
        AddJsonb(command, "context", Serialize(CrawlSessionContextSnapshot.FromDomain(expedition.Context, expedition.CampaignId)));
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, expedition.OwnerUserId);
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, expedition.Name);
        AddJsonb(command, "state", Serialize(RuntimeStateSnapshot.FromDomain(expedition.Runtime)));
        AddJsonb(command, "knowledge", expedition.Knowledge is null ? null : Serialize(expedition.Knowledge));
        expedition.Party.Validate();
        AddJsonb(command, "party", Serialize(expedition.Party));
        AddJsonb(command, "generatedResolutions", Serialize(expedition.GeneratedProcedureResolutions));
        AddJsonb(command, "procedure", Serialize(expedition.Procedure));
        AddJsonb(command, "procedureOrigin", expedition.ProcedureOrigin is null ? null : Serialize(expedition.ProcedureOrigin));
        AddJsonb(command, "campaignProcedure", expedition.CampaignProcedure is null ? null : Serialize(expedition.CampaignProcedure));
        command.Parameters.AddWithValue("pause", NpgsqlDbType.Text, expedition.PauseReason?.ToString() is { } pause ? pause : DBNull.Value);
        command.Parameters.AddWithValue("remaining", NpgsqlDbType.Bigint, expedition.RemainingWatchTime.Ticks);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, expedition.Version);
        AddTimestamp(command, "updated", expedition.UpdatedAt);
    }
}
