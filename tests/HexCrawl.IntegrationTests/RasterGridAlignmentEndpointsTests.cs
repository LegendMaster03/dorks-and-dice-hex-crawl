using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class RasterGridAlignmentEndpointsTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task GridAlignmentEndpointAtomicallyPersistsGridAndRasterRegistration()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            Guid sourceMapId;
            long appliedVersion;

            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                var world = await CreateWorld(client, "Raster grid HTTP");
                worldId = world.GetProperty("id").GetGuid();
                var uploaded = await Upload(
                    client,
                    worldId,
                    world.GetProperty("version").GetInt64());
                sourceMapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();
                var versionBefore = uploaded.GetProperty("version").GetInt64();
                var gridId = uploaded.GetProperty("grid").GetProperty("id").GetGuid();

                var request = new
                {
                    expectedVersion = versionBefore,
                    grid = new
                    {
                        id = gridId,
                        orientation = "FlatTop",
                        coordinateConvention = "AxialQr",
                        origin = new { x = 2.5, y = -3.25 },
                        rotationDegrees = 4.5,
                        hexRadiusWorldUnits = 1.75,
                        neighborCenterDistance = new
                        {
                            value = 12,
                            unit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                        }
                    },
                    alignment = new
                    {
                        kind = "Affine",
                        m11 = 0.2,
                        m12 = 0,
                        m13 = 5,
                        m21 = 0,
                        m22 = 0.2,
                        m23 = 6,
                        m31 = 0,
                        m32 = 0
                    }
                };

                using var response = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-alignment",
                    request);
                response.EnsureSuccessStatusCode();
                var applied = await response.Content.ReadFromJsonAsync<JsonElement>();
                appliedVersion = applied.GetProperty("version").GetInt64();

                Assert.Equal(versionBefore + 1, appliedVersion);
                Assert.Equal("FlatTop", applied.GetProperty("grid").GetProperty("orientation").GetString());
                Assert.Equal(2.5, applied.GetProperty("grid").GetProperty("origin").GetProperty("x").GetDouble(), 8);
                Assert.Equal(-3.25, applied.GetProperty("grid").GetProperty("origin").GetProperty("y").GetDouble(), 8);
                Assert.Equal(4.5, applied.GetProperty("grid").GetProperty("rotationDegrees").GetDouble(), 8);
                Assert.Equal(1.75, applied.GetProperty("grid").GetProperty("hexRadiusWorldUnits").GetDouble(), 8);

                var map = Assert.Single(applied.GetProperty("sourceMaps").EnumerateArray());
                var alignment = map.GetProperty("alignment");
                Assert.Equal("Affine", alignment.GetProperty("kind").GetString());
                Assert.Equal(0.2, alignment.GetProperty("m11").GetDouble(), 8);
                Assert.Equal(5, alignment.GetProperty("m13").GetDouble(), 8);
                Assert.Equal(6, alignment.GetProperty("m23").GetDouble(), 8);
                Assert.Equal(4, map.GetProperty("worldCoverageBoundary").GetArrayLength());

                using var stale = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-alignment",
                    request);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            using (var reopenedFactory = TestWebHost.Create(database, "alice"))
            using (var reopened = reopenedFactory.CreateClient())
            {
                var world = await reopened.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
                Assert.Equal(appliedVersion, world.GetProperty("version").GetInt64());
                Assert.Equal("FlatTop", world.GetProperty("grid").GetProperty("orientation").GetString());
                var map = Assert.Single(world.GetProperty("sourceMaps").EnumerateArray());
                Assert.Equal(0.2, map.GetProperty("alignment").GetProperty("m22").GetDouble(), 8);
            }

            using var otherFactory = TestWebHost.Create(database, "bob");
            using var other = otherFactory.CreateClient();
            using var forbidden = await other.PutAsJsonAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-alignment",
                new
                {
                    expectedVersion = appliedVersion,
                    grid = new
                    {
                        id = Guid.NewGuid(),
                        orientation = "PointyTop",
                        coordinateConvention = "AxialQr",
                        origin = new { x = 0, y = 0 },
                        rotationDegrees = 0,
                        hexRadiusWorldUnits = 1,
                        neighborCenterDistance = new
                        {
                            value = 12,
                            unit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                        }
                    },
                    alignment = new
                    {
                        kind = "Affine",
                        m11 = 1,
                        m12 = 0,
                        m13 = 0,
                        m21 = 0,
                        m22 = 1,
                        m23 = 0,
                        m31 = 0,
                        m32 = 0
                    }
                });
            Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
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

    private static async Task<JsonElement> Upload(HttpClient client, Guid worldId, long expectedVersion)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TinyPng);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(file, "file", "baked-grid.png");
        content.Add(new StringContent("Baked grid"), "name");
        content.Add(new StringContent("http-fixture"), "geographyKey");
        content.Add(new StringContent("GM"), "role");
        content.Add(new StringContent("True"), "containsBakedGrid");
        content.Add(new StringContent(expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");

        using var response = await client.PostAsync(
            $"/api/overworlds/{worldId:D}/source-maps",
            content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
