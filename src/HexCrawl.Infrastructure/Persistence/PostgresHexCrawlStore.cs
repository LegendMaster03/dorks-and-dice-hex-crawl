using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL persistence facade. Aggregate operations and serialization snapshots are split
/// into partial files so storage behavior remains centralized without becoming monolithic.
/// </summary>
public sealed partial class PostgresHexCrawlStore : IHexCrawlStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _connectionString;

    public PostgresHexCrawlStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        new PostgresSchemaMigrator(_connectionString).MigrateAsync(cancellationToken);

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM hex_crawl_schema_migrations;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return version == PostgresSchemaMigrator.CurrentVersion;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
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
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE id = @id AND owner_user_id = @owner);";
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidDataException($"Persisted {typeof(T).Name} JSON was empty.");

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(reader.GetFieldValue<DateTime>(ordinal));

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value.UtcDateTime);

    private static void AddJsonb(NpgsqlCommand command, string name, string? json)
    {
        var parameter = new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = json is null ? DBNull.Value : json
        };
        command.Parameters.Add(parameter);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
