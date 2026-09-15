using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class PersistenceHistoryDiagnosticTests
{
    [Fact]
    public async Task RuntimeHistoryPersistencePhasesComplete()
    {
        var logPath = Environment.GetEnvironmentVariable("HEX_CRAWL_HISTORY_PHASE_LOG");
        void Phase(string message)
        {
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
            }
        }

        Phase("before database create");
        await using var database = await TestDatabase.CreateAsync();
        Phase("after database create");
        var service = await database.ServiceAsync();
        Phase("after service create");
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        Phase("after world create");
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "History diagnostic", "simple-fixed-distance", new HexCoordinate(0, 0)));
        Phase($"after expedition start version={expedition.Version} history={expedition.State.History.Count}");

        Phase("before first advance");
        expedition = await service.AdvanceExpeditionAsync(
            expedition.State.Id,
            "alice",
            FixedAdvance(expedition.Version, 1, true));
        Phase($"after first advance version={expedition.Version} history={expedition.State.History.Count}");

        Phase("before second advance");
        expedition = await service.AdvanceExpeditionAsync(
            expedition.State.Id,
            "alice",
            FixedAdvance(expedition.Version, 1, true));
        Phase($"after second advance version={expedition.Version} history={expedition.State.History.Count}");

        Phase("before restart service");
        var restarted = await database.ServiceAsync();
        Phase("after restart service");
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        Phase($"after reload version={loaded.Version} history={loaded.State.History.Count}");

        Assert.NotEmpty(loaded.State.History);
        Assert.Equal(
            loaded.State.History.Count,
            loaded.State.History.Select(item => item.Sequence).Distinct().Count());
        Phase("assertions complete");
    }

    private static CreateOverworldCommand WorldCommand() => new(
        "Diagnostic world",
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);

    private static AdvanceExpeditionCommand FixedAdvance(long version, double miles, bool continueAcrossBoundaries) => new()
    {
        ExpectedVersion = version,
        IntendedDirection = 0,
        ExpectedDistance = miles,
        ActualDistance = miles,
        ResolutionSource = ResolutionSource.ManualRoll,
        ContinueAcrossBoundaries = continueAcrossBoundaries
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path;
        public string ConnectionString { get; }

        private TestDatabase(string path)
        {
            _path = path;
            ConnectionString = $"Data Source={path}";
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = new TestDatabase(Path.Combine(Path.GetTempPath(), $"hex-crawl-history-diagnostic-{Guid.NewGuid():N}.db"));
            var store = new SqliteHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return database;
        }

        public async Task<HexCrawlService> ServiceAsync()
        {
            var store = new SqliteHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            return new HexCrawlService(store);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = _path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            return ValueTask.CompletedTask;
        }
    }
}
