using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace HexCrawl.IntegrationTests;

internal static class TestWebHost
{
    public static string NewDatabasePath()
    {
        var adminConnectionString = BaseConnectionString();
        var schema = $"hex_web_{Guid.NewGuid():N}";
        using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schema}\";";
            command.ExecuteNonQuery();
        }

        return new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            SearchPath = schema
        }.ConnectionString;
    }

    public static WebApplicationFactory<Program> Create(
        string databasePath,
        string? userId = "integration-user",
        long? maxMapFileBytes = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:HexCrawl", databasePath);
            builder.UseSetting("MapAssets:RootPath", AssetRoot(databasePath));
            if (maxMapFileBytes.HasValue)
            {
                builder.UseSetting("MapImport:MaxFileBytes", maxMapFileBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            builder.UseSetting("ToolHost:StandaloneIdentity:Enabled", userId is null ? "false" : "true");
            if (userId is not null)
            {
                builder.UseSetting("ToolHost:StandaloneIdentity:UserId", userId);
                builder.UseSetting("ToolHost:StandaloneIdentity:DisplayName", $"Integration {userId}");
            }
        });

    public static void DeleteDatabase(string path)
    {
        var builder = new NpgsqlConnectionStringBuilder(path);
        var schema = builder.SearchPath;
        if (!string.IsNullOrWhiteSpace(schema))
        {
            builder.SearchPath = string.Empty;
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;";
            command.ExecuteNonQuery();
        }

        var assets = AssetRoot(path);
        if (Directory.Exists(assets)) Directory.Delete(assets, recursive: true);
    }

    private static string BaseConnectionString() =>
        Environment.GetEnvironmentVariable("HEXCRAWL_TEST_POSTGRES")
        ?? "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private static string AssetRoot(string connectionString)
    {
        var schema = new NpgsqlConnectionStringBuilder(connectionString).SearchPath ?? "unknown";
        return Path.Combine(Path.GetTempPath(), $"hex-crawl-assets-{schema}");
    }
}
