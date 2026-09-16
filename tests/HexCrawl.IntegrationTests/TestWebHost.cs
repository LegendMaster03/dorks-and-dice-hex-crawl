using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HexCrawl.IntegrationTests;

internal static class TestWebHost
{
    public static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"hex-crawl-web-{Guid.NewGuid():N}.db");

    public static WebApplicationFactory<Program> Create(string databasePath, string? userId = "integration-user") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:HexCrawl", $"Data Source={databasePath}");
            builder.UseSetting("ToolHost:StandaloneIdentity:Enabled", userId is null ? "false" : "true");
            if (userId is not null)
            {
                builder.UseSetting("ToolHost:StandaloneIdentity:UserId", userId);
                builder.UseSetting("ToolHost:StandaloneIdentity:DisplayName", $"Integration {userId}");
            }
        });

    public static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = path + suffix;
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
