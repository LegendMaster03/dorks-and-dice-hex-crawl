using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HexCrawl.IntegrationTests;

public sealed class ExpeditionDeletionEndpointsTests
{
    [Fact]
    public async Task OwnerCanDeleteRunningSheetAndDependentEventsWithoutDeletingWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Disposable QA world");
            var worldId = world.GetProperty("id").GetGuid();
            var expedition = await StartExpedition(client, worldId, "Disposable QA expedition");
            var expeditionId = expedition.GetProperty("id").GetGuid();
            var version = expedition.GetProperty("version").GetInt64();
            await InsertDependentEvent(database, expeditionId);
            Assert.Equal(1, await CountRows(database, "expedition_events", "expedition_id", expeditionId));

            using var response = await client.DeleteAsync($"/api/expeditions/{expeditionId:D}?expectedVersion={version}");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            using var missing = await client.GetAsync($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal(0, await CountRows(database, "expeditions", "id", expeditionId));
            Assert.Equal(0, await CountRows(database, "expedition_events", "expedition_id", expeditionId));

            using var preservedWorld = await client.GetAsync($"/api/overworlds/{worldId:D}");
            preservedWorld.EnsureSuccessStatusCode();
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OtherOwnerCanNotDeleteRunningSheet()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            long version;
            using (var ownerFactory = TestWebHost.Create(database, "alice"))
            using (var owner = ownerFactory.CreateClient())
            {
                var world = await CreateWorld(owner, "Alice world");
                var expedition = await StartExpedition(owner, world.GetProperty("id").GetGuid(), "Alice session");
                expeditionId = expedition.GetProperty("id").GetGuid();
                version = expedition.GetProperty("version").GetInt64();
            }

            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            using (var response = await other.DeleteAsync($"/api/expeditions/{expeditionId:D}?expectedVersion={version}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using var reopenedFactory = TestWebHost.Create(database, "alice");
            using var reopenedClient = reopenedFactory.CreateClient();
            using var preserved = await reopenedClient.GetAsync($"/api/expeditions/{expeditionId:D}");
            preserved.EnsureSuccessStatusCode();
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task StaleVersionCanNotDeleteRunningSheetOrDependentEvents()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Versioned world");
            var expedition = await StartExpedition(client, world.GetProperty("id").GetGuid(), "Versioned session");
            var expeditionId = expedition.GetProperty("id").GetGuid();
            var staleVersion = expedition.GetProperty("version").GetInt64();
            await InsertDependentEvent(database, expeditionId);
            await IncrementVersion(database, expeditionId);

            using var response = await client.DeleteAsync($"/api/expeditions/{expeditionId:D}?expectedVersion={staleVersion}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            using var preserved = await client.GetAsync($"/api/expeditions/{expeditionId:D}");
            preserved.EnsureSuccessStatusCode();
            Assert.Equal(1, await CountRows(database, "expedition_events", "expedition_id", expeditionId));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DeletingQaSessionPreservesOtherWorldsAndOriginalMagnostephisSession()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var humblewood = await CreateWorld(client, "Humblewood");
            var magnostephis = await CreateWorld(client, "Magnostephis");
            var sessionReady = await CreateWorld(client, "Magnostephis — Session Ready");
            var qaWorld = await CreateWorld(client, "QA generic Bellowing Wilds 2026-09-27");
            var realSession = await StartExpedition(
                client,
                magnostephis.GetProperty("id").GetGuid(),
                "Original Magnostephis expedition");
            var qaSession = await StartExpedition(
                client,
                qaWorld.GetProperty("id").GetGuid(),
                "Disposable QA expedition");

            using (var delete = await client.DeleteAsync(
                $"/api/expeditions/{qaSession.GetProperty("id").GetGuid():D}?expectedVersion={qaSession.GetProperty("version").GetInt64()}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            }

            foreach (var world in new[] { humblewood, magnostephis, sessionReady, qaWorld })
            {
                using var preserved = await client.GetAsync($"/api/overworlds/{world.GetProperty("id").GetGuid():D}");
                preserved.EnsureSuccessStatusCode();
            }

            using var real = await client.GetAsync($"/api/expeditions/{realSession.GetProperty("id").GetGuid():D}");
            real.EnsureSuccessStatusCode();
            using var qaMissing = await client.GetAsync($"/api/expeditions/{qaSession.GetProperty("id").GetGuid():D}");
            Assert.Equal(HttpStatusCode.NotFound, qaMissing.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> CreateWorld(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name,
            orientation = "PointyTop",
            origin = new { x = 0, y = 0 },
            rotationDegrees = 0,
            hexRadiusWorldUnits = 1,
            neighborCenterDistance = 12,
            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> StartExpedition(HttpClient client, Guid worldId, string name)
    {
        using var response = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
        {
            name,
            procedureKey = "simple-fixed-distance",
            presentationKey = "exploration-map",
            startHex = new { q = 0, r = 0 }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task InsertDependentEvent(string database, Guid expeditionId)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO expedition_events(expedition_id, sequence, kind, subject_id, subject_type, event_json)
            VALUES($expedition, 999999, 'TestDependency', NULL, NULL, '{}');
            """;
        command.Parameters.AddWithValue("$expedition", expeditionId.ToString("D"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task IncrementVersion(string database, Guid expeditionId)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE expeditions SET version = version + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", expeditionId.ToString("D"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<long> CountRows(string database, string table, string keyColumn, Guid id)
    {
        if (table is not ("expeditions" or "expedition_events")) throw new ArgumentOutOfRangeException(nameof(table));
        if (keyColumn is not ("id" or "expedition_id")) throw new ArgumentOutOfRangeException(nameof(keyColumn));
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {keyColumn} = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
