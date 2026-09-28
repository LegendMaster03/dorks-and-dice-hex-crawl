namespace HexCrawl.PersistenceMigration;

public static class MigrationCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = Parse(args);
            var migrator = new SqliteToPostgresMigrator(options.SqliteConnectionString, options.PostgresConnectionString);
            MigrationVerificationReport report;
            if (options.VerifyOnly)
            {
                report = await migrator.VerifyAsync();
            }
            else
            {
                report = await migrator.MigrateAndVerifyAsync();
            }

            Console.WriteLine(
                $"Verified SQLite schema v{report.SourceSchemaVersion}: " +
                $"{report.OverworldCount} overworld(s), {report.ExpeditionCount} expedition(s), " +
                $"{report.EventCount} event(s); contexts={string.Join(',', report.ContextKinds)}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Hex Crawl persistence migration failed: {exception.Message}");
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        string? sqlite = null;
        string? postgres = null;
        var verifyOnly = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--sqlite" when index + 1 < args.Length:
                    sqlite = args[++index];
                    break;
                case "--postgres" when index + 1 < args.Length:
                    postgres = args[++index];
                    break;
                case "--verify-only":
                    verifyOnly = true;
                    break;
                default:
                    throw new ArgumentException(
                        "Usage: HexCrawl.PersistenceMigration --sqlite <path-or-connection-string> " +
                        "[--postgres <connection-string>] [--verify-only]. " +
                        "When --postgres is omitted, ConnectionStrings__HexCrawl must be set.");
            }
        }

        postgres ??= Environment.GetEnvironmentVariable("ConnectionStrings__HexCrawl");
        if (string.IsNullOrWhiteSpace(sqlite) || string.IsNullOrWhiteSpace(postgres))
        {
            throw new ArgumentException(
                "--sqlite is required, and PostgreSQL must be supplied by --postgres or ConnectionStrings__HexCrawl. " +
                "PostgreSQL credentials are never written by the migration tool.");
        }

        var sqliteConnectionString = sqlite.Contains('=', StringComparison.Ordinal)
            ? sqlite
            : $"Data Source={Path.GetFullPath(sqlite)};Mode=ReadOnly";
        return new Options(sqliteConnectionString, postgres, verifyOnly);
    }

    private sealed record Options(
        string SqliteConnectionString,
        string PostgresConnectionString,
        bool VerifyOnly);
}
