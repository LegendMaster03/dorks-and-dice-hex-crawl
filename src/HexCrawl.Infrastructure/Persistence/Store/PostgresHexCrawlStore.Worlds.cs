using HexCrawl.Application.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class PostgresHexCrawlStore
{
    public async Task<StoredOverworld> CreateOverworldAsync(
        StoredOverworld overworld,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO overworlds(id, owner_user_id, name, world_json, version, created_at, updated_at)
            VALUES(@id, @owner, @name, @world, @version, @created, @updated);
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
            WHERE owner_user_id = @owner
            ORDER BY updated_at DESC, name;
            """;
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<OverworldSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OverworldSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt64(2),
                ReadTimestamp(reader, 3),
                ReadTimestamp(reader, 4)));
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
            SELECT world_json::text, version, created_at, updated_at
            FROM overworlds
            WHERE id = @id AND owner_user_id = @owner;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, overworldId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        var world = Deserialize<WorldSnapshot>(reader.GetString(0)).ToDomain();
        if (world.Id != overworldId)
            throw new InvalidDataException(
                "Persisted overworld identity disagrees with the requested database row.");
        return new StoredOverworld(
            world,
            ownerUserId,
            reader.GetInt64(1),
            ReadTimestamp(reader, 2),
            ReadTimestamp(reader, 3));
    }

    public async Task<SaveResult<StoredOverworld>> SaveOverworldAsync(
        StoredOverworld overworld,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = overworld with
        {
            Version = expectedVersion + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE overworlds
            SET name = @name, world_json = @world, version = @version, updated_at = @updated
            WHERE id = @id AND owner_user_id = @owner AND version = @expectedVersion;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, updated.World.Id);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, updated.OwnerUserId);
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, updated.World.Name);
        AddJsonb(command, "world", Serialize(WorldSnapshot.FromDomain(updated.World)));
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, updated.Version);
        AddTimestamp(command, "updated", updated.UpdatedAt);
        command.Parameters.AddWithValue("expectedVersion", NpgsqlDbType.Bigint, expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return new SaveResult<StoredOverworld>(SaveOutcome.Saved, updated);
        }

        var exists = await ExistsAsync(connection, transaction, "overworlds", overworld.World.Id, overworld.OwnerUserId, cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return new SaveResult<StoredOverworld>(exists ? SaveOutcome.Conflict : SaveOutcome.NotFound, null);
    }

    public async Task<DeleteOverworldOutcome> DeleteOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "SELECT version FROM overworlds WHERE id = @id AND owner_user_id = @owner;";
            versionCommand.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, overworldId);
            versionCommand.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
            var value = await versionCommand.ExecuteScalarAsync(cancellationToken);
            if (value is null or DBNull)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeleteOverworldOutcome.NotFound;
            }
            if (Convert.ToInt64(value) != expectedVersion)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeleteOverworldOutcome.Conflict;
            }
        }

        await using (var dependentCommand = connection.CreateCommand())
        {
            dependentCommand.Transaction = transaction;
            dependentCommand.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM expeditions
                    WHERE overworld_id = @world AND owner_user_id = @owner
                );
                """;
            dependentCommand.Parameters.AddWithValue("world", NpgsqlDbType.Uuid, overworldId);
            dependentCommand.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
            if ((bool)(await dependentCommand.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeleteOverworldOutcome.HasExpeditions;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM overworlds
            WHERE id = @id AND owner_user_id = @owner AND version = @expectedVersion;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, overworldId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        command.Parameters.AddWithValue("expectedVersion", NpgsqlDbType.Bigint, expectedVersion);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeleteOverworldOutcome.Conflict;
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return DeleteOverworldOutcome.HasExpeditions;
        }

        await transaction.CommitAsync(cancellationToken);
        return DeleteOverworldOutcome.Deleted;
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
                WHERE overworld_id = @world AND owner_user_id = @owner
            );
            """;
        command.Parameters.AddWithValue("world", NpgsqlDbType.Uuid, overworldId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static void BindWorld(NpgsqlCommand command, StoredOverworld overworld)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, overworld.World.Id);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, overworld.OwnerUserId);
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, overworld.World.Name);
        AddJsonb(command, "world", Serialize(WorldSnapshot.FromDomain(overworld.World)));
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, overworld.Version);
        AddTimestamp(command, "created", overworld.CreatedAt);
        AddTimestamp(command, "updated", overworld.UpdatedAt);
    }
}
