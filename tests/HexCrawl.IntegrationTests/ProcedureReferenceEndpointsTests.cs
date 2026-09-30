using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureReferenceEndpointsTests
{
    [Fact]
    public async Task SavedProcedureReferenceIsRevisionAwareAndKeepsOlderSnapshotStable()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var createResponse = await client.PostAsJsonAsync("/api/procedures", new
            {
                presetKey = "simple-fixed-distance",
                campaignId = (Guid?)null,
                overrides = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var procedureId = created.GetProperty("procedureId").GetGuid();

            var first = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/revisions/1/reference");
            Assert.Equal(1, first.GetProperty("revision").GetInt32());
            Assert.Equal("4 hours", Parameter(first, GenericProcedureCatalog.TimeIntervalModule, "durationTicks")
                .GetProperty("displayValue").GetString());

            var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
            using var revisionResponse = await client.PostAsJsonAsync(
                $"/api/procedures/{procedureId:D}/revisions",
                new
                {
                    expectedRevision = 1,
                    overrides = new[]
                    {
                        new
                        {
                            overrideId = "six-hour-reference-http",
                            moduleKey = GenericProcedureCatalog.TimeIntervalModule,
                            replacementMechanicKey = (string?)null,
                            replacementMechanicVersion = (int?)null,
                            parameters = new Dictionary<string, string> { ["durationTicks"] = sixHours },
                            note = "HTTP reference revision proof"
                        }
                    }
                });
            revisionResponse.EnsureSuccessStatusCode();

            var firstAgain = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/revisions/1/reference");
            var second = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/revisions/2/reference");
            var latest = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/reference");

            Assert.Equal(1, firstAgain.GetProperty("revision").GetInt32());
            Assert.Equal(2, second.GetProperty("revision").GetInt32());
            Assert.Equal(2, latest.GetProperty("revision").GetInt32());
            Assert.Equal("4 hours", Parameter(firstAgain, GenericProcedureCatalog.TimeIntervalModule, "durationTicks")
                .GetProperty("displayValue").GetString());
            Assert.Equal("6 hours", Parameter(second, GenericProcedureCatalog.TimeIntervalModule, "durationTicks")
                .GetProperty("displayValue").GetString());

            var secondTime = Module(second, GenericProcedureCatalog.TimeIntervalModule);
            Assert.True(secondTime.GetProperty("isModified").GetBoolean());
            Assert.Equal(1, secondTime.GetProperty("modificationCount").GetInt32());
            Assert.Contains(
                secondTime.GetProperty("modificationNotes").EnumerateArray(),
                value => value.GetString() == "HTTP reference revision proof");
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task CustomReferenceNeedsNoOriginAndExposesAllMaterializedModules()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var createResponse = await client.PostAsJsonAsync("/api/procedures", new
            {
                presetKey = (string?)null,
                campaignId = (Guid?)null,
                overrides = Array.Empty<object>()
            });
            createResponse.EnsureSuccessStatusCode();
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var procedureId = created.GetProperty("procedureId").GetGuid();
            var expectedModuleCount = created.GetProperty("modules").GetArrayLength();

            var reference = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/reference");

            Assert.Equal(JsonValueKind.Null, reference.GetProperty("origin").ValueKind);
            Assert.Equal(
                expectedModuleCount,
                reference.GetProperty("sections").EnumerateArray()
                    .Sum(section => section.GetProperty("modules").GetArrayLength()));
            Assert.Contains(
                reference.GetProperty("sections").EnumerateArray(),
                section => section.GetProperty("name").GetString() == "Journey Processes");

            var navigation = Module(reference, GenericProcedureCatalog.NavigationOutcomeModule);
            var input = Assert.Single(
                navigation.GetProperty("requiredInputs").EnumerateArray(),
                value => value.GetProperty("key").GetString() == "navigation.check-result");
            var sources = input.GetProperty("allowedSources").EnumerateArray()
                .Select(value => value.GetProperty("key").GetString())
                .ToArray();
            Assert.Contains("SelectedModule", sources);
            Assert.Contains("Dm", sources);
            Assert.Contains("OptionalProvider", sources);
            Assert.Contains("ExternalState", sources);
            Assert.Contains(
                navigation.GetProperty("diagnostics").EnumerateArray(),
                value => value.GetProperty("kind").GetString() == "UnresolvedInput");
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task PublishedProofReferencesPreserveArcticAndOneRingGraph()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            var dnd = await CreateAndReferenceAsync(client, CrawlProcedureCatalog.Dnd2024PresetKey);
            var terrain = Parameter(dnd, GenericProcedureCatalog.TerrainMovementModule, "terrainAdjustments");
            Assert.Contains(
                terrain.GetProperty("mapEntries").EnumerateArray(),
                entry => entry.GetProperty("key").GetString() == "arctic"
                    && entry.GetProperty("value").GetString() == "fast-if-appropriately-equipped");

            var oneRing = await CreateAndReferenceAsync(client, CrawlProcedureCatalog.OneRing2ePresetKey);
            var modules = oneRing.GetProperty("sections").EnumerateArray()
                .SelectMany(section => section.GetProperty("modules").EnumerateArray())
                .ToArray();
            Assert.DoesNotContain(
                modules,
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.TimeIntervalModule);
            Assert.Contains(
                modules,
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.MovementBudgetModule
                    && module.GetProperty("mechanic").GetProperty("key").GetString() == GenericProcedureCatalog.JourneyProgressBudgetMechanic);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> CreateAndReferenceAsync(HttpClient client, string presetKey)
    {
        using var response = await client.PostAsJsonAsync("/api/procedures", new
        {
            presetKey,
            campaignId = (Guid?)null,
            overrides = Array.Empty<object>()
        });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var procedureId = created.GetProperty("procedureId").GetGuid();
        return await client.GetFromJsonAsync<JsonElement>($"/api/procedures/{procedureId:D}/reference");
    }

    private static JsonElement Module(JsonElement reference, string moduleKey) =>
        Assert.Single(
            reference.GetProperty("sections").EnumerateArray()
                .SelectMany(section => section.GetProperty("modules").EnumerateArray()),
            module => module.GetProperty("moduleKey").GetString() == moduleKey);

    private static JsonElement Parameter(JsonElement reference, string moduleKey, string parameterKey) =>
        Assert.Single(
            Module(reference, moduleKey).GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("key").GetString() == parameterKey);
}
