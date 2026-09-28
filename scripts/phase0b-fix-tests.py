from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"Expected text not found: {label}")
    return text.replace(old, new, 1)


# Convert the final shared workbench fixture from SQLite to PostgreSQL.
path = Path("tests/HexCrawl.Application.Tests/ExpeditionWorkbenchTests.cs")
text = path.read_text()
start = text.index("    private sealed class TestDatabase : IAsyncDisposable\n")
replacement = """    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        public string ConnectionString => _database.ConnectionString;

        private TestDatabase(PostgresTestDatabase database)
        {
            _database = database;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var store = new PostgresHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return new TestDatabase(database);
        }

        public async Task<ExpeditionAssistantService> AssistantServiceAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            var resolver = new CrawlSessionContextResolver(core);
            return new ExpeditionAssistantService(store, core, resolver);
        }

        public async Task<(HexCrawlService Core, ExpeditionWorkbenchService Workbench)> ServicesAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            var resolver = new CrawlSessionContextResolver(core);
            return (core, new ExpeditionWorkbenchService(store, core, resolver));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
"""
path.write_text(text[:start] + replacement)

# Correct migration acceptance test code for current xUnit and runtime shapes.
path = Path("tests/HexCrawl.Application.Tests/PostgresPersistenceMigrationTests.cs")
text = path.read_text()
text = replace_once(
    text,
    "            var worldBound = Assert.NotNull(await store.GetExpeditionAsync(seeded.WorldBoundId, Owner));\n",
    "            var worldBound = await store.GetExpeditionAsync(seeded.WorldBoundId, Owner);\n            Assert.NotNull(worldBound);\n            worldBound = worldBound!;\n",
    "world-bound NotNull assignment",
)
text = replace_once(
    text,
    "                var abstractHex = Assert.NotNull(await store.GetExpeditionAsync(seeded.AbstractHexId, Owner));\n                var nonSpatial = Assert.NotNull(await store.GetExpeditionAsync(seeded.NonSpatialId, Owner));\n",
    "                var abstractHex = await store.GetExpeditionAsync(seeded.AbstractHexId, Owner);\n                var nonSpatial = await store.GetExpeditionAsync(seeded.NonSpatialId, Owner);\n                Assert.NotNull(abstractHex);\n                Assert.NotNull(nonSpatial);\n                abstractHex = abstractHex!;\n                nonSpatial = nonSpatial!;\n",
    "standalone NotNull assignments",
)
text = replace_once(
    text,
    "        var generatedId = Guid.NewGuid();\n        var generated = new GeneratedProcedureResolution(\n",
    "        var spatialRuntime = (ExpeditionState)worldBound.Runtime;\n        var generatedId = Guid.NewGuid();\n        var generated = new GeneratedProcedureResolution(\n",
    "spatial runtime cast",
)
text = replace_once(
    text,
    "            worldBound.Runtime.CompletedWatches + 1,\n",
    "            spatialRuntime.CompletedWatches + 1,\n",
    "completed watches access",
)
text = replace_once(
    text,
    "        worldBound = Assert.NotNull(save.Value);\n",
    "        Assert.NotNull(save.Value);\n        worldBound = save.Value!;\n",
    "save NotNull assignment",
)
path.write_text(text)

# Ensure no runtime/test fixture still references the removed SQLite store implementation.
remaining = []
for candidate in Path("tests").rglob("*.cs"):
    if "SqliteHexCrawlStore" in candidate.read_text():
        remaining.append(str(candidate))
if remaining:
    raise SystemExit("Remaining SqliteHexCrawlStore references in tests: " + ", ".join(remaining))
