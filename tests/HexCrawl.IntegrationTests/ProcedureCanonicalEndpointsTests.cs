using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HexCrawl.Application;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureCanonicalEndpointsTests
{
    [Fact]
    public async Task CanonicalEditorRoundTripsThroughAuthoritativeRevisionEndpoints()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var draftResponse = await client.PostAsJsonAsync(
                "/api/procedures/composer/canonical/draft",
                new
                {
                    presetKey = CrawlProcedureCatalog.Dnd2024PresetKey,
                    procedureId = (Guid?)null,
                    revision = (int?)null,
                    overrides = Array.Empty<object>()
                });
            draftResponse.EnsureSuccessStatusCode();
            var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
            var canonicalJson = draft.GetProperty("canonicalJson").GetString()!;

            using var validateResponse = await client.PostAsJsonAsync(
                "/api/procedures/composer/canonical/validate",
                new { canonicalJson });
            validateResponse.EnsureSuccessStatusCode();
            var validation = await validateResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(validation.GetProperty("isValid").GetBoolean());
            Assert.Equal(1, validation.GetProperty("revision").GetInt32());

            using var createResponse = await client.PostAsJsonAsync(
                "/api/procedures/canonical",
                new
                {
                    canonicalJson,
                    presetKey = CrawlProcedureCatalog.Dnd2024PresetKey,
                    campaignId = (Guid?)null
                });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var procedureId = created.GetProperty("procedureId").GetGuid();
            Assert.Equal(1, created.GetProperty("revision").GetInt32());
            Assert.Equal(
                CrawlProcedureCatalog.Dnd2024PresetKey,
                created.GetProperty("origin").GetProperty("presetKey").GetString());

            var editedNode = JsonNode.Parse(canonicalJson)!.AsObject();
            editedNode["name"] = "Canonical endpoint procedure";
            var editedJson = editedNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            using var revisionResponse = await client.PostAsJsonAsync(
                $"/api/procedures/{procedureId:D}/canonical/revisions",
                new { expectedRevision = 1, canonicalJson = editedJson });
            revisionResponse.EnsureSuccessStatusCode();
            var revised = await revisionResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, revised.GetProperty("revision").GetInt32());
            Assert.Equal("Canonical endpoint procedure", revised.GetProperty("name").GetString());

            var first = await client.GetFromJsonAsync<JsonElement>(
                $"/api/procedures/{procedureId:D}/revisions/1");
            Assert.NotEqual("Canonical endpoint procedure", first.GetProperty("name").GetString());

            using var latestCanonicalResponse = await client.PostAsJsonAsync(
                "/api/procedures/composer/canonical/draft",
                new
                {
                    presetKey = (string?)null,
                    procedureId,
                    revision = 2,
                    overrides = Array.Empty<object>()
                });
            latestCanonicalResponse.EnsureSuccessStatusCode();
            var latestCanonical = await latestCanonicalResponse.Content.ReadFromJsonAsync<JsonElement>();
            var latestJson = latestCanonical.GetProperty("canonicalJson").GetString()!;

            using var noOpResponse = await client.PostAsJsonAsync(
                $"/api/procedures/{procedureId:D}/canonical/revisions",
                new { expectedRevision = 2, canonicalJson = latestJson });
            Assert.Equal(HttpStatusCode.BadRequest, noOpResponse.StatusCode);

            using var staleResponse = await client.PostAsJsonAsync(
                $"/api/procedures/{procedureId:D}/canonical/revisions",
                new { expectedRevision = 1, canonicalJson = latestJson });
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task CanonicalValidationReturnsDiagnosticsWithoutSavingInvalidJson()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(
                "/api/procedures/composer/canonical/validate",
                new { canonicalJson = "{\n  \"procedureId\":" });
            response.EnsureSuccessStatusCode();
            var validation = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.False(validation.GetProperty("isValid").GetBoolean());
            Assert.Equal(JsonValueKind.Null, validation.GetProperty("procedureId").ValueKind);
            Assert.Equal(JsonValueKind.String, validation.GetProperty("error").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, validation.GetProperty("lineNumber").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, validation.GetProperty("bytePositionInLine").ValueKind);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
