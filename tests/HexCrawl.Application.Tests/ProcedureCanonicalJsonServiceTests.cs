using System.Text.Json.Nodes;
using HexCrawl.Domain.Procedure;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureCanonicalJsonServiceTests
{
    [Fact]
    public async Task CanonicalDraftUsesSameCampaignProcedureAndPreservesOriginAcrossRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var canonical = new ProcedureCanonicalJsonService(procedures, composer);

        var json = await canonical.CreateDraftJsonAsync(
            "alice",
            CrawlProcedureCatalog.Dnd2024PresetKey,
            null,
            null,
            []);
        var validation = canonical.Validate(json);

        Assert.True(validation.IsValid, validation.Error);
        Assert.NotNull(validation.Procedure);
        Assert.Equal(1, validation.Procedure!.Revision);

        var created = await canonical.CreateAsync(
            "alice",
            json,
            CrawlProcedureCatalog.Dnd2024PresetKey);
        Assert.Equal(validation.Procedure.ProcedureId, created.ProcedureId);
        Assert.Equal(CrawlProcedureCatalog.Dnd2024PresetKey, created.ProcedureOrigin?.PresetKey);

        var edited = created.Procedure with { Name = "Canonical house procedure" };
        var revised = await canonical.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            canonical.Serialize(edited));
        var first = await procedures.GetAsync("alice", created.ProcedureId, 1);

        Assert.Equal(2, revised.Revision);
        Assert.Equal("Canonical house procedure", revised.Procedure.Name);
        Assert.Equal(CrawlProcedureCatalog.Dnd2024PresetKey, revised.ProcedureOrigin?.PresetKey);
        Assert.Equal(created.Procedure.Name, first.Procedure.Name);
    }

    [Fact]
    public async Task CanonicalNoOpIsRejectedAfterJsonRoundTrip()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var canonical = new ProcedureCanonicalJsonService(procedures, composer);
        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);
        var roundTripped = canonical.Validate(canonical.Serialize(created.Procedure));

        Assert.True(roundTripped.IsValid, roundTripped.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            procedures.CreateCanonicalRevisionAsync(
                "alice",
                created.ProcedureId,
                created.Revision,
                roundTripped.Procedure!));

        Assert.Single(await procedures.ListRevisionsAsync("alice", created.ProcedureId));
    }

    [Fact]
    public async Task CanonicalRevisionRejectsIdentityAndConcurrencyChanges()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var canonical = new ProcedureCanonicalJsonService(procedures, composer);
        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);

        var wrongIdentity = created.Procedure with
        {
            ProcedureId = Guid.NewGuid(),
            Name = "Wrong identity"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            canonical.CreateRevisionAsync(
                "alice",
                created.ProcedureId,
                created.Revision,
                canonical.Serialize(wrongIdentity)));

        var revised = await canonical.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            canonical.Serialize(created.Procedure with { Name = "Revision two" }));
        Assert.Equal(2, revised.Revision);

        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
            canonical.CreateRevisionAsync(
                "alice",
                created.ProcedureId,
                1,
                canonical.Serialize(created.Procedure with { Name = "Stale edit" })));
    }

    [Fact]
    public async Task LegacyV1CanonicalJsonUpgradesToV11WithCurrentHexTiling()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var canonical = new ProcedureCanonicalJsonService(procedures, composer);
        var current = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;

        var implicitLegacy = JsonNode.Parse(canonical.Serialize(current))!.AsObject();
        implicitLegacy.Remove("schemaVersion");
        implicitLegacy.Remove("tilingGjhNotation");
        var implicitResult = canonical.Validate(implicitLegacy.ToJsonString());

        Assert.True(implicitResult.IsValid, implicitResult.Error);
        Assert.Equal(CampaignProcedureSchema.CurrentVersion, implicitResult.Procedure!.SchemaVersion);
        Assert.Equal(CampaignProcedureSchema.CurrentHexTilingGjhNotation, implicitResult.Procedure.TilingGjhNotation);

        var explicitLegacy = JsonNode.Parse(canonical.Serialize(current))!.AsObject();
        explicitLegacy["schemaVersion"] = CampaignProcedureSchema.LegacyVersion;
        explicitLegacy.Remove("tilingGjhNotation");
        var explicitResult = canonical.Validate(explicitLegacy.ToJsonString());

        Assert.True(explicitResult.IsValid, explicitResult.Error);
        Assert.Equal(CampaignProcedureSchema.CurrentVersion, explicitResult.Procedure!.SchemaVersion);
        Assert.Equal(CampaignProcedureSchema.CurrentHexTilingGjhNotation, explicitResult.Procedure.TilingGjhNotation);

        var unsupported = current with { TilingGjhNotation = "4/m45/r(h1)" };
        var unsupportedResult = canonical.Validate(canonical.Serialize(unsupported));
        Assert.False(unsupportedResult.IsValid);
        Assert.Contains("not supported", unsupportedResult.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidationReportsMalformedJsonWithoutCreatingAuthority()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var canonical = new ProcedureCanonicalJsonService(procedures, composer);

        var validation = canonical.Validate("{\n  \"procedureId\":");

        Assert.False(validation.IsValid);
        Assert.Null(validation.Procedure);
        Assert.NotNull(validation.Error);
        Assert.NotNull(validation.LineNumber);
        Assert.NotNull(validation.BytePositionInLine);
    }
}
