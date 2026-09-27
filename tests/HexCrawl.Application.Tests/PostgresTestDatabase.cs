using Npgsql;

namespace HexCrawl.Application.Tests;

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _schema;

    public string ConnectionString { get; }

    private PostgresTestDatabase(string adminConnectionString, string schema, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _schema = schema;
        ConnectionString = connectionString;
    }

    public static async Task<PostgresTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("HEXCRAWL_TEST_POSTGRES")
            ?? "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        var schema = $"hex_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schema}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            SearchPath = schema
        };
        return new PostgresTestDatabase(adminConnectionString, schema, builder.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;";
        await command.ExecuteNonQueryAsync();
    }
}
