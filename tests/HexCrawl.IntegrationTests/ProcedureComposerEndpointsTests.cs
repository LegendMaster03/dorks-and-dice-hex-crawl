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
            Assert.Empty(custom.GetProperty("modules").EnumerateArray());
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
    public async Task NeutralCustomCanonicalDraftCanBeEditedBeforeItIsPersistable()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var draftResponse = await client.PostAsJsonAsync("/api/procedures/composer/canonical/draft", new
            {
                presetKey = (string?)null,
                procedureId = (Guid?)null,
                revision = (int?)null,
                moduleSelections = Array.Empty<object>(),
                overrides = Array.Empty<object>()
            });
            draftResponse.EnsureSuccessStatusCode();
            var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
            var canonicalJson = draft.GetProperty("canonicalJson").GetString();
            Assert.False(string.IsNullOrWhiteSpace(canonicalJson));

            using var validationResponse = await client.PostAsJsonAsync(
                "/api/procedures/composer/canonical/validate",
                new { canonicalJson });
            validationResponse.EnsureSuccessStatusCode();
            var validation = await validationResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(validation.GetProperty("isValid").GetBoolean());
            Assert.Contains("module", validation.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ResolutionHelperContractExposesFieldsAndFlagsIncompleteEnabledHelper()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/procedures/composer/draft", new
            {
                presetKey = (string?)null,
                procedureId = (Guid?)null,
                revision = (int?)null,
                moduleSelections = new[]
                {
                    new { moduleKey = GenericProcedureCatalog.ResolutionHelpersModule, included = true }
                },
                overrides = new[]
                {
                    new
                    {
                        overrideId = "enable-travel-helper",
                        moduleKey = GenericProcedureCatalog.ResolutionHelpersModule,
                        replacementMechanicKey = (string?)null,
                        replacementMechanicVersion = (int?)null,
                        parameters = new Dictionary<string, string>
                        {
                            ["travel.enabled"] = "true"
                        },
                        note = (string?)null
                    }
                }
            });
            response.EnsureSuccessStatusCode();
            var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
            var helper = Assert.Single(
                draft.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleKey").GetString() == GenericProcedureCatalog.ResolutionHelpersModule);

            var schema = helper.GetProperty("mechanic").GetProperty("parameterSchema");
            Assert.True(schema.TryGetProperty("travel.diceCount", out _));
            Assert.True(schema.TryGetProperty("encounter.wanderingResults", out _));
            Assert.Contains(
                helper.GetProperty("validationIssues").EnumerateArray(),
                issue => issue.GetString()!.Contains("travel.diceCount", StringComparison.Ordinal));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExtensionMechanicRemainsVisibleAsSelectedComposerMechanic()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/procedures/composer/draft", new
            {
                presetKey = (string?)null,
                procedureId = (Guid?)null,
                revision = (int?)null,
                moduleSelections = new[]
                {
                    new { moduleKey = Phase11GenericProcedureCatalog.ExposureModule, included = true }
                },
                overrides = Array.Empty<object>()
            });
            response.EnsureSuccessStatusCode();
            var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
            var exposure = Assert.Single(
                draft.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleKey").GetString() == Phase11GenericProcedureCatalog.ExposureModule);

            Assert.Equal(
                Phase11GenericProcedureCatalog.ExposurePolicyMechanic,
                exposure.GetProperty("mechanic").GetProperty("key").GetString());
            Assert.DoesNotContain(
                exposure.GetProperty("alternatives").EnumerateArray(),
                mechanic => mechanic.GetProperty("key").GetString() == Phase11GenericProcedureCatalog.ExposurePolicyMechanic);
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
                name = "North March procedure",
                moduleSelections = new[]
                {
                    new { moduleKey = GenericProcedureCatalog.TimeIntervalModule, included = true }
                },
                overrides = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.Created, customResponse.StatusCode);
            var custom = await customResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("origin").ValueKind);
            Assert.Equal("North March procedure", custom.GetProperty("name").GetString());

            var customId = custom.GetProperty("procedureId").GetGuid();
            using var renameResponse = await client.PostAsJsonAsync(
                $"/api/procedures/{customId:D}/revisions",
                new
                {
                    expectedRevision = 1,
                    name = "Winter North March",
                    moduleSelections = Array.Empty<object>(),
                    overrides = Array.Empty<object>()
                });
            renameResponse.EnsureSuccessStatusCode();
            var renamedCustom = await renameResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, renamedCustom.GetProperty("revision").GetInt32());
            Assert.Equal("Winter North March", renamedCustom.GetProperty("name").GetString());

            var originalCustom = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{customId:D}/revisions/1");
            Assert.Equal("North March procedure", originalCustom.GetProperty("name").GetString());

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
