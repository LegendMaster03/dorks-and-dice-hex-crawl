using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureComposerEndpointsTests
{
    [Fact]
    public async Task DraftEndpointsExposePresetMinimalCustomAndStructuralComposerContracts()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var presetResponse = await client.PostAsJsonAsync("/api/procedures/composer/draft", new
            {
                presetKey = CrawlProcedureCatalog.Dnd2024PresetKey,
                procedureId = (Guid?)null,
                revision = (int?)null,
                overrides = Array.Empty<object>()
            });
            presetResponse.EnsureSuccessStatusCode();
            var preset = await presetResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                CrawlProcedureCatalog.Dnd2024PresetKey,
                preset.GetProperty("origin").GetProperty("presetKey").GetString());

            var terrain = Assert.Single(
                preset.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.TerrainMovementModule);
            Assert.Equal("map<string>", terrain.GetProperty("mechanic")
                .GetProperty("parameterSchema")
                .GetProperty("terrainAdjustments")
                .GetProperty("type")
                .GetString());
            Assert.Contains(
                "arctic=fast-if-appropriately-equipped",
                terrain.GetProperty("parameters").GetProperty("terrainAdjustments").GetString()!,
                StringComparison.Ordinal);
            Assert.Contains(
                terrain.GetProperty("alternatives").EnumerateArray(),
                mechanic => mechanic.GetProperty("key").GetString() == GenericProcedureCatalog.TerrainMovementPolicyMechanic);
            Assert.Equal("Declarative", terrain.GetProperty("mechanic").GetProperty("executionSupport").GetString());

            using var customResponse = await client.PostAsJsonAsync("/api/procedures/composer/draft", new
            {
                presetKey = (string?)null,
                procedureId = (Guid?)null,
                revision = (int?)null,
                overrides = Array.Empty<object>()
            });
            customResponse.EnsureSuccessStatusCode();
            var custom = await customResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("origin").ValueKind);
            var customModule = Assert.Single(custom.GetProperty("modules").EnumerateArray());
            Assert.Equal(
                GenericProcedureCatalog.TimeIntervalModule,
                customModule.GetProperty("moduleKey").GetString());
            Assert.Empty(custom.GetProperty("dependencies").GetProperty("issues").EnumerateArray());

            using var composedResponse = await client.PostAsJsonAsync("/api/procedures/composer/draft", new
            {
                presetKey = (string?)null,
                procedureId = (Guid?)null,
                revision = (int?)null,
                overrides = Array.Empty<object>(),
                moduleSelections = new[]
                {
                    new { moduleKey = GenericProcedureCatalog.JourneyProcessModule, included = true },
                    new { moduleKey = GenericProcedureCatalog.NavigationOutcomeModule, included = true }
                }
            });
            composedResponse.EnsureSuccessStatusCode();
            var composed = await composedResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(
                composed.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("category").GetString() == "Journey processes");

            var unresolved = Assert.Single(
                composed.GetProperty("dependencies").GetProperty("issues").EnumerateArray(),
                issue => issue.GetProperty("kind").GetString() == "UnresolvedInput"
                    && issue.GetProperty("inputKey").GetString() == "navigation.check-result");
            var sources = unresolved.GetProperty("allowedInputSources")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray();
            Assert.Contains("SelectedModule", sources);
            Assert.Contains("Dm", sources);
            Assert.Contains("OptionalProvider", sources);
            Assert.Contains("ExternalState", sources);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DraftChangeRefreshesDependenciesAndModificationMetadata()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/procedures/composer/draft", new
            {
                presetKey = CrawlProcedureCatalog.OneRing2ePresetKey,
                procedureId = (Guid?)null,
                revision = (int?)null,
                overrides = new[]
                {
                    new
                    {
                        overrideId = "composer-movement-budget",
                        moduleKey = GenericProcedureCatalog.MovementBudgetModule,
                        replacementMechanicKey = GenericProcedureCatalog.MovementBudgetMechanic,
                        replacementMechanicVersion = 1,
                        parameters = new Dictionary<string, string>
                        {
                            ["budgetModel"] = "fixed-per-interval",
                            ["baseBudget"] = "1",
                            ["budgetUnit"] = "interval",
                            ["limitingScope"] = "party"
                        },
                        note = (string?)null
                    }
                }
            });
            response.EnsureSuccessStatusCode();
            var draft = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(1, draft.GetProperty("modificationCount").GetInt32());
            Assert.Equal(1, draft.GetProperty("modifiedModuleCount").GetInt32());
            var movement = Assert.Single(
                draft.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.MovementBudgetModule);
            Assert.True(movement.GetProperty("isModified").GetBoolean());
            Assert.Equal(1, movement.GetProperty("modificationCount").GetInt32());
            Assert.Equal(
                GenericProcedureCatalog.MovementBudgetMechanic,
                movement.GetProperty("mechanic").GetProperty("key").GetString());
            Assert.Contains(
                draft.GetProperty("dependencies").GetProperty("issues").EnumerateArray(),
                issue => issue.GetProperty("kind").GetString() == "MissingRequiredProducer"
                    && issue.GetProperty("inputKey").GetString() == "time.interval-duration");
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task SavingCreatesImmutablePostgresRevisionsAndCustomProcedureHasNoOrigin()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var customResponse = await client.PostAsJsonAsync("/api/procedures", new
            {
                presetKey = (string?)null,
                campaignId = (Guid?)null,
                overrides = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.Created, customResponse.StatusCode);
            var custom = await customResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("origin").ValueKind);

            using var createResponse = await client.PostAsJsonAsync("/api/procedures", new
            {
                presetKey = "simple-fixed-distance",
                campaignId = (Guid?)null,
                overrides = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var procedureId = created.GetProperty("procedureId").GetGuid();
            Assert.Equal(1, created.GetProperty("revision").GetInt32());

            const string durationTicks = "216000000000";
            using var revisionResponse = await client.PostAsJsonAsync(
                $"/api/procedures/{procedureId:D}/revisions",
                new
                {
                    expectedRevision = 1,
                    overrides = new[]
                    {
                        new
                        {
                            overrideId = "six-hour-watch",
                            moduleKey = GenericProcedureCatalog.TimeIntervalModule,
                            replacementMechanicKey = (string?)null,
                            replacementMechanicVersion = (int?)null,
                            parameters = new Dictionary<string, string>
                            {
                                ["durationTicks"] = durationTicks
                            },
                            note = (string?)null
                        }
                    }
                });
            revisionResponse.EnsureSuccessStatusCode();
            var revised = await revisionResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(procedureId, revised.GetProperty("procedureId").GetGuid());
            Assert.Equal(2, revised.GetProperty("revision").GetInt32());

            var first = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/revisions/1");
            var latest = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}");
            Assert.Equal(1, first.GetProperty("revision").GetInt32());
            Assert.Equal(2, latest.GetProperty("revision").GetInt32());

            var firstTime = Assert.Single(
                first.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.TimeIntervalModule);
            var latestTime = Assert.Single(
                latest.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.TimeIntervalModule);
            Assert.NotEqual(
                durationTicks,
                firstTime.GetProperty("parameters").GetProperty("durationTicks").GetString());
            Assert.Equal(
                durationTicks,
                latestTime.GetProperty("parameters").GetProperty("durationTicks").GetString());

            var revisions = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/revisions");
            Assert.Equal(2, revisions.GetArrayLength());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
