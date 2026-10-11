using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class Phase20OneTimeStartupCutoverTests
{
    [Fact]
    public async Task StartupUpgradeRemovesV1AndItsEventsButPreservesOtherKindsAndUnrelatedRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        await migrator.MigrateAsync();

        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var nonspatial = Guid.NewGuid();
        var savedProcedure = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            // Simulate an already-deployed schema-10 database.
            seed.CommandText = """
                ALTER TABLE expeditions DROP CONSTRAINT ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 10 WHERE version = 11;
                INSERT INTO campaign_procedure_revisions
                    (procedure_id, revision, owner_user_id, procedure_json, created_at)
                VALUES (@procedure, 1, 'owner', '{"source":"kept"}'::jsonb, now());
                INSERT INTO expeditions
                    (id, owner_user_id, name, context_json, state_json,
                     party_json, environment_json, effects_json, resources_json,
                     survival_json, journey_state_json, generated_resolutions_json,
                     procedure_json, remaining_watch_ticks, version,
                     created_at, updated_at)
                VALUES
                    (@v1, 'owner', 'old hex', '{}'::jsonb,
                     '{"kind":"Spatial","progress":{"value":0.5}}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 3, now(), now()),
                    (@v2, 'owner', 'cell', '{}'::jsonb,
                     '{"kind":"CellSpatial"}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 7, now(), now()),
                    (@nonspatial, 'owner', 'scene', '{}'::jsonb,
                     '{"kind":"NonSpatial"}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 2, now(), now());
                INSERT INTO expedition_events (expedition_id, sequence, kind, event_json)
                VALUES (@v1, 1, 'WatchStarted', '{}'::jsonb),
                       (@v2, 1, 'WatchStarted', '{}'::jsonb);
                """;
            seed.Parameters.AddWithValue("procedure", savedProcedure);
            seed.Parameters.AddWithValue("v1", v1);
            seed.Parameters.AddWithValue("v2", v2);
            seed.Parameters.AddWithValue("nonspatial", nonspatial);
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // restart is idempotent
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                       (SELECT count(*) FROM expeditions WHERE id = @v1),
                       (SELECT count(*) FROM expeditions WHERE id = @v2 AND version = 7),
                       (SELECT count(*) FROM expeditions WHERE id = @nonspatial AND version = 2),
                       (SELECT count(*) FROM expedition_events WHERE expedition_id = @v1),
                       (SELECT count(*) FROM expedition_events WHERE expedition_id = @v2),
                       (SELECT count(*) FROM campaign_procedure_revisions WHERE procedure_id = @procedure);
                """;
            check.Parameters.AddWithValue("procedure", savedProcedure);
            check.Parameters.AddWithValue("v1", v1);
            check.Parameters.AddWithValue("v2", v2);
            check.Parameters.AddWithValue("nonspatial", nonspatial);
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(11L, reader.GetInt64(0));
            Assert.Equal(0L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.Equal(0L, reader.GetInt64(4));
            Assert.Equal(1L, reader.GetInt64(5));
            Assert.Equal(1L, reader.GetInt64(6));
        }

        await using var forbidden = connection.CreateCommand();
        forbidden.CommandText = """
            INSERT INTO expeditions
                (id, owner_user_id, name, context_json, state_json,
                 party_json, environment_json, effects_json, resources_json,
                 survival_json, journey_state_json, generated_resolutions_json,
                 procedure_json, remaining_watch_ticks, version, created_at, updated_at)
            VALUES (gen_random_uuid(), 'owner', 'old', '{}'::jsonb,
                    '{"kind":"Spatial"}'::jsonb,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 1, now(), now());
            """;
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
        Assert.Equal("23514", rejected.SqlState);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":null}")]
    [InlineData("{\"kind\":\"Spatial\"}")]
    [InlineData("[]")]
    public async Task V2OnlyConstraintRejectsMissingNullLegacyAndNonObjectKinds(string snapshot)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO expeditions
                (id, owner_user_id, name, context_json, state_json,
                 party_json, environment_json, effects_json, resources_json,
                 survival_json, journey_state_json, generated_resolutions_json,
                 procedure_json, remaining_watch_ticks, version, created_at, updated_at)
            VALUES (gen_random_uuid(), 'owner', 'invalid snapshot', '{}'::jsonb, @state,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                    0, 1, now(), now());
            """;
        command.Parameters.AddWithValue("state", NpgsqlTypes.NpgsqlDbType.Jsonb, snapshot);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task UnknownRuntimeKindRollsBackEntireUpgradeAndPreservesOldData()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        await migrator.MigrateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                ALTER TABLE expeditions DROP CONSTRAINT ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 10 WHERE version = 11;
                INSERT INTO expeditions
                    (id, owner_user_id, name, context_json, state_json,
                     party_json, environment_json, effects_json, resources_json,
                     survival_json, journey_state_json, generated_resolutions_json,
                     procedure_json, remaining_watch_ticks, version, created_at, updated_at)
                SELECT gen_random_uuid(), 'owner', 'old', '{}'::jsonb,
                       jsonb_build_object('kind', value),
                       '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                       '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 1, now(), now()
                FROM (VALUES ('Spatial'), ('FutureKind')) as kinds(value);
                """;
            await seed.ExecuteNonQueryAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync());
        Assert.Contains("No tester records were changed", failure.Message);
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                   (SELECT count(*) FROM expeditions),
                   (SELECT count(*) FROM expeditions WHERE state_json->>'kind' = 'Spatial');
            """;
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(10L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
    }
}
