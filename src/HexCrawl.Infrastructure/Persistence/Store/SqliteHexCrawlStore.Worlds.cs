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


}
