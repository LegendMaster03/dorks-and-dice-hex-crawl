using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

/// <summary>
/// One-time, transactional authored-world cutover for schema 10 databases.
/// Remove this temporary converter after the authorized development cutover.
/// It is never used by ordinary world reads, writes, or spatial execution.
/// </summary>
internal static class PostgresLegacyWorldCutover
{
    private static readonly JsonSerializerOptions Json = CreateOptions();

    public static async Task ConvertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var worlds = new List<(Guid Id, string Json)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id, world_json::text FROM overworlds ORDER BY id FOR UPDATE;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                worlds.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        // Convert and validate all rows before writing any. PostgreSQL also
        // rolls back this entire transaction if a later update fails.
        var converted = worlds.Select(world => (
            world.Id, Value: ConvertSnapshot(world.Id, world.Json))).ToArray();
        foreach (var world in converted)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE overworlds SET world_json = @world
                WHERE id = @id;
                """;
            update.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, world.Id);
            update.Parameters.AddWithValue("world", NpgsqlDbType.Jsonb, world.Value);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("World disappeared during the locked authored-data cutover.");
        }
    }

    private static string ConvertSnapshot(Guid identity, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
            throw new InvalidDataException("An authored world must be a JSON object.");
        if (root["id"] is null || root["id"]!.GetValue<Guid>() != identity)
            throw new InvalidDataException("Authored-world JSON identity does not match its database row.");
        int version = root["formatVersion"]?.GetValue<int>() ?? 1;
        if (version is not (1 or 2 or 3))
            throw new InvalidDataException("Unknown authored-world snapshot format; no records have been changed.");

        var gridNode = root["grid"];
        var tilingNode = root["tiling"];
        PeriodicWorldTiling tiling;
        if (gridNode is not null)
        {
            if (version == 3)
                throw new InvalidDataException("A converted world still contains a legacy grid.");
            var grid = gridNode.Deserialize<HexGridDefinition>(Json)
                ?? throw new InvalidDataException("The authored grid was not readable.");
            grid.Validate();
            tiling = LegacyHexTilingCompatibility.Create(grid);
            if (tilingNode is not null)
            {
                var recorded = tilingNode.Deserialize<PeriodicWorldTiling>(Json)
                    ?? throw new InvalidDataException("Authored periodic geometry is missing.");
                if (!LegacyHexTilingCompatibility.Matches(recorded, grid))
                    throw new InvalidDataException(
                        "Original grid and retained periodic geometry disagree; world preserved.");
                tiling = recorded;
            }
        }
        else
        {
            tiling = tilingNode?.Deserialize<PeriodicWorldTiling>(Json)
                ?? throw new InvalidDataException(
                    "Authored world has no verified periodic geometry; original record preserved.");
            if (version == 1)
                throw new InvalidDataException("Original-format world has no grid to convert.");
        }

        tiling.Validate();
        if (root["environmentAnnotations"] is not JsonArray annotations)
            throw new InvalidDataException("World environment annotations must be an array.");
        foreach (var annotation in annotations)
        {
            if (annotation is not JsonObject item || item["scope"] is not JsonObject scope)
                throw new InvalidDataException("Invalid authored environment scope.");
            var kind = scope["kind"]?.GetValue<string>()
                ?? throw new InvalidDataException("Authored environment scope has no kind.");
            if (kind == "Hex")
            {
                if (gridNode is null || scope["hex"] is not JsonObject coordinates)
                    throw new InvalidDataException("Axial environment scope has no originating grid.");
                var hex = coordinates.Deserialize<HexCoordinate>(Json);
                var address = LegacyHexTilingCompatibility.ToAddress(hex);
                tiling.Resolve(address);
                scope["kind"] = "Cell";
                scope["cell"] = JsonSerializer.SerializeToNode(
                    new WorldCellId(tiling.Id, address), Json);
            }
            else if (kind is not ("World" or "Cell" or "SpatialFeature"))
                throw new InvalidDataException("Unsupported authored environment scope kind.");
            scope.Remove("hex");
            if (scope["cell"] is JsonNode cell)
            {
                var qualified = cell.Deserialize<WorldCellId>(Json);
                if (qualified.TilingId != tiling.Id)
                    throw new InvalidDataException("Authored cell scope references another tiling.");
                tiling.Resolve(qualified.Address);
            }
        }

        root["tiling"] = JsonSerializer.SerializeToNode(tiling, Json);
        root.Remove("grid");
        root["formatVersion"] = 3;
        return root.ToJsonString(Json);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
