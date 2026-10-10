using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace HexCrawl.IntegrationTests;

/// <summary>
/// Protects the distinction between removing an expedition card and committed
/// deletion from the authoritative PostgreSQL tables after a fresh connection.
/// </summary>
public sealed class Phase18ExpeditionPhysicalDeletionTests
{
    [Theory]
    [InlineData("AbstractHex")]
    [InlineData("NonSpatial")]
    public async Task StandaloneDeletePhysicallyRemovesOnlyRequestedSessionAcrossRestart(
        string contextKind)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid deletedId;
            Guid preservedId;
            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                var deleted = await StartAsync(client, contextKind, "Delete me");
                var preserved = await StartAsync(client, contextKind, "Keep me");
                deletedId = deleted.GetProperty("id").GetGuid();
                preservedId = preserved.GetProperty("id").GetGuid();
                var version = deleted.GetProperty("version").GetInt64();

                await InsertEventAsync(database, deletedId);
                await InsertEventAsync(database, preservedId);

                using (var stale = await client.DeleteAsync(
                    $"/api/expeditions/{deletedId:D}?expectedVersion={version + 1}"))
                {
                    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                }
                Assert.Equal(1, await CountAsync(database, "expeditions", "id", deletedId));
                Assert.Equal(1, await CountAsync(database, "expedition_events", "expedition_id", deletedId));

                using (var delete = await client.DeleteAsync(
                    $"/api/expeditions/{deletedId:D}?expectedVersion={version}"))
                {
                    Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
                }

                using (var missing = await client.GetAsync($"/api/expeditions/{deletedId:D}"))
                {
                    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                }

                Assert.Equal(0, await CountAsync(database, "expeditions", "id", deletedId));
                Assert.Equal(0, await CountAsync(database, "expedition_events", "expedition_id", deletedId));
                Assert.Equal(1, await CountAsync(database, "expeditions", "id", preservedId));
                Assert.Equal(1, await CountAsync(database, "expedition_events", "expedition_id", preservedId));

                using var repeated = await client.DeleteAsync(
                    $"/api/expeditions/{deletedId:D}?expectedVersion={version}");
                Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
            }

            // Recreate the service and connection pool: UI state and the prior
            // Npgsql session can not mask data that remains on disk.
            using (var reopenedFactory = TestWebHost.Create(database, "alice"))
            using (var reopened = reopenedFactory.CreateClient())
            {
                var listed = await reopened.GetFromJsonAsync<JsonElement>("/api/expeditions");
                var ids = listed.EnumerateArray()
                    .Select(item => item.GetProperty("id").GetGuid())
                    .ToArray();
                Assert.DoesNotContain(deletedId, ids);
                Assert.Contains(preservedId, ids);
                using var missing = await reopened.GetAsync($"/api/expeditions/{deletedId:D}");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                using var preserved = await reopened.GetAsync($"/api/expeditions/{preservedId:D}");
                Assert.Equal(HttpStatusCode.OK, preserved.StatusCode);
            }

            Assert.Equal(0, await CountAsync(database, "expeditions", "id", deletedId));
            Assert.Equal(0, await CountAsync(database, "expedition_events", "expedition_id", deletedId));
            Assert.Equal(1, await CountAsync(database, "expeditions", "id", preservedId));
            Assert.Equal(1, await CountAsync(database, "expedition_events", "expedition_id", preservedId));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> StartAsync(
        HttpClient client, string contextKind, string name)
    {
        object request = contextKind == "AbstractHex"
            ? new
            {
                name,
                procedureKey = "alexandrian-advanced",
                context = new
                {
                    kind = "AbstractHex",
                    name = "Mapless movement",
                    orientation = "PointyTop",
                    hexCenterDistance = 12,
                    distanceUnit = new
                    {
                        kind = "Mile",
                        symbol = "mi",
                        metersPerUnit = 1609.344
                    }
                },
                startHex = new { q = 0, r = 0 }
            }
            : new
            {
                name,
                procedureKey = "alexandrian-advanced",
                context = new
                {
                    kind = "NonSpatial",
                    name = "Travel without a grid"
                }
            };
        using var response = await client.PostAsJsonAsync("/api/expeditions", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task InsertEventAsync(string connectionString, Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO expedition_events(expedition_id, sequence, kind,
                subject_id, subject_type, event_json)
            VALUES(@id, 999999, 'DeletionProof', NULL, NULL, '{}');
            """;
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<long> CountAsync(
        string connectionString, string table, string column, Guid id)
    {
        if (table is not ("expeditions" or "expedition_events"))
            throw new ArgumentOutOfRangeException(nameof(table));
        if (column is not ("id" or "expedition_id"))
            throw new ArgumentOutOfRangeException(nameof(column));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table} WHERE {column} = @id;";
        command.Parameters.AddWithValue("id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
