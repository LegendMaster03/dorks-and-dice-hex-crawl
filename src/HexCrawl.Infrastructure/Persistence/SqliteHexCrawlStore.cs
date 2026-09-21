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

/// <summary>
/// SQLite persistence facade. Aggregate operations and serialization snapshots are split
/// into partial files so storage behavior remains centralized without becoming monolithic.
/// </summary>
public sealed partial class SqliteHexCrawlStore : IHexCrawlStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _connectionString;

    public SqliteHexCrawlStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        new SqliteSchemaMigrator(_connectionString).MigrateAsync(cancellationToken);

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
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


}
