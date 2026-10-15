using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
using System.Text.Json;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedurePresetIdentityTests
{
    [Fact]
    public void CatalogRegistersAllIndividualPresetsInStableOrder()
    {
        string[] expected =
        [
            "alexandrian-advanced",
            "simple-fixed-distance",
            "simple-hex-step",
            CrawlProcedureCatalog.WastelandWandererPresetKey,
            CrawlProcedureCatalog.WastelandWarfarePresetKey,
            CrawlProcedureCatalog.BxPresetKey,
            CrawlProcedureCatalog.OseClassicPresetKey,
            CrawlProcedureCatalog.Adnd2ePresetKey,
            CrawlProcedureCatalog.Dnd35PresetKey,
            CrawlProcedureCatalog.Dnd2014PresetKey,
            CrawlProcedureCatalog.Dnd2024PresetKey,
            CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey,
            CrawlProcedureCatalog.ForbiddenLandsPresetKey,
            CrawlProcedureCatalog.WorldsWithoutNumberPresetKey,
            CrawlProcedureCatalog.OneRing2ePresetKey
        ];

        var actual = CrawlProcedureCatalog.Catalog.Select(preset => preset.PresetKey).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());

        foreach (var preset in CrawlProcedureCatalog.Catalog)
        {
            preset.Validate();
            var square = preset.PresetKey is
                CrawlProcedureCatalog.WastelandWandererPresetKey or CrawlProcedureCatalog.WastelandWarfarePresetKey;
            Assert.Equal(square ? "<1:1,1,1:4,4>" : CampaignProcedureSchema.CurrentHexTilingDsSymbol,
                preset.Recipe.TilingDsSymbol);
            var materialized = preset.MaterializeGeneric();
            materialized.Procedure.Validate();
            Assert.Equal(preset.PresetKey, materialized.Origin?.PresetKey);
        }
    }

    [Fact]
    public void FalloutSquarePresetsCarryOptionalMapLimitsWithoutChangingOtherPresets()
    {
        foreach (var (key, expectedSize) in new[]
        {
            (CrawlProcedureCatalog.WastelandWandererPresetKey, 5),
            (CrawlProcedureCatalog.WastelandWarfarePresetKey, 9)
        })
        {
            var procedure = CrawlProcedureCatalog.Resolve(key).MaterializeGeneric().Procedure;
            Assert.Equal("<1:1,1,1:4,4>", procedure.TilingDsSymbol);
            Assert.Equal(new PeriodicMapBounds(expectedSize, expectedSize,
                expectedSize == 9 ? -4 : -2, expectedSize == 9 ? -4 : -2), procedure.PlayableBounds);
            var copy = JsonSerializer.Deserialize<CampaignProcedure>(JsonSerializer.Serialize(procedure));
            Assert.NotNull(copy);
            copy.Validate();
            Assert.Equal(procedure.PlayableBounds, copy.PlayableBounds);
        }
        Assert.Null(CrawlProcedureCatalog.Resolve("simple-hex-step")
            .MaterializeGeneric().Procedure.PlayableBounds);
    }

    [Fact]
    public void SquarePresetsConfigureDistinctGenericExplorationFlows()
    {
        var wanderer = GenericProcedureRuntime.Bind(
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.WastelandWandererPresetKey)
                .MaterializeGeneric().Procedure).ExplorationFlow;
        var warfare = GenericProcedureRuntime.Bind(
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.WastelandWarfarePresetKey)
                .MaterializeGeneric().Procedure).ExplorationFlow;
        Assert.NotNull(wanderer);
        Assert.NotNull(warfare);
        Assert.Equal(["travel", "encounter", "take-action", "journal"],
            wanderer.StageKeys);
        Assert.True(wanderer.RequiresEncounterAfterTravel());
        Assert.Equal(EncounterCheckCadence.PerWatch, GenericProcedureRuntime.Bind(
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.WastelandWandererPresetKey)
                .MaterializeGeneric().Procedure).Encounters.Cadence);
        Assert.Equal(["explore", "rest"], warfare.AvailableActions(false, true));
        Assert.Equal(["travel"], warfare.AvailableActions(true, false));
        Assert.False(warfare.RequiresEncounterAfterTravel());
    }

    [Fact]
    public void SquarePresetRulesUseTheSameGenericRuntimeAfterIdentityAndGeometryChanges()
    {
        foreach (var key in new[]
        {
            CrawlProcedureCatalog.WastelandWandererPresetKey,
            CrawlProcedureCatalog.WastelandWarfarePresetKey
        })
        {
            var original = CrawlProcedureCatalog.Resolve(key).MaterializeGeneric().Procedure;
            var changed = original with
            {
                Key = "custom-table-procedure",
                Name = "Unrelated homebrew campaign",
                TilingDsSymbol = CampaignProcedureSchema.CurrentHexTilingDsSymbol,
                PlayableBounds = new PeriodicMapBounds(3, 7)
            };
            changed.Validate();
            var expected = GenericProcedureRuntime.Bind(original);
            var actual = GenericProcedureRuntime.Bind(changed);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(expected.Movement, actual.Movement);
            Assert.Equal(expected.TurnPolicy, actual.TurnPolicy);
            Assert.Equal(expected.Navigation, actual.Navigation);
            Assert.Equal(expected.Encounters, actual.Encounters);
            Assert.Equal(expected.ResolutionHelpers, actual.ResolutionHelpers);
            Assert.Equal(expected.ExplorationFlow?.StageKeys,
                actual.ExplorationFlow?.StageKeys);
            Assert.Equal(expected.ExplorationFlow?.DiscoveryTrigger,
                actual.ExplorationFlow?.DiscoveryTrigger);
            Assert.Equal(expected.ExplorationFlow?.EncounterTrigger,
                actual.ExplorationFlow?.EncounterTrigger);
            Assert.Equal(expected.ExplorationFlow?.FirstVisitAction,
                actual.ExplorationFlow?.FirstVisitAction);
            Assert.Equal(expected.ExplorationFlow?.RepeatVisitAction,
                actual.ExplorationFlow?.RepeatVisitAction);
            Assert.Equal(expected.ExplorationFlow?.RestSiteAction,
                actual.ExplorationFlow?.RestSiteAction);
        }
    }

    [Fact]
    public void NamedSystemsDoNotAppearInGenericRuntimeImplementation()
    {
        // The preset catalog registers implementations. All game-specific
        // values and terminology otherwise belong to the data recipes under
        // Presets/, not the shared domain/application/web execution pipeline.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(
            root.FullName, "src", "HexCrawl.Application", "Expeditions",
            "CrawlProcedureCatalog.cs")))
            root = root.Parent;
        Assert.NotNull(root);

        var namedSystemKeys = CrawlProcedureCatalog.Catalog
            .Where(preset => preset.Category != "Generic starting points")
            .Select(preset => preset.PresetKey)
            .ToArray();

        foreach (var project in new[]
        {
            "HexCrawl.Domain", "HexCrawl.Application", "HexCrawl.Web", "HexCrawl.Infrastructure"
        })
        {
            var projectRoot = Path.Combine(root!.FullName, "src", project);
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs",
                SearchOption.AllDirectories))
            {
                if (file.Contains(
                        $"{Path.DirectorySeparatorChar}Presets{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    || Path.GetFileName(file) == "CrawlProcedureCatalog.cs")
                    continue;

                var source = File.ReadAllText(file);
                foreach (var key in namedSystemKeys)
                {
                    // Short keys such as "bx" collide with ordinary local
                    // variable names in geometric algorithms. Require a
                    // quoted preset-key literal in that case.
                    var token = key.Length < 5 ? $"\"{key}\"" : key;
                    Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
                }
                Assert.DoesNotContain("fallout-wasteland-", source,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("alexandrian", source,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("WastelandWandererPreset", source,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("WastelandWarfarePreset", source,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("Wasteland Warfare", source,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Wasteland Wanderer", source,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        // The browser likewise consumes generic procedure and cell contracts;
        // no named-game movement logic belongs in the client.
        var clientRoot = Path.Combine(root!.FullName, "src",
            "HexCrawl.Web", "Client", "src");
        foreach (var file in Directory.EnumerateFiles(clientRoot, "*.ts",
            SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (var key in namedSystemKeys)
            {
                var token = key.Length < 5 ? $"\"{key}\"" : key;
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
            }
            Assert.DoesNotContain("fallout-wasteland-", source,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alexandrian", source,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Wasteland Warfare", source,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Wasteland Wanderer", source,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ChoosingPresetMaterializesIndependentCampaignOwnedSnapshots()
    {
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");

        var first = preset.MaterializeGeneric();
        var second = preset.MaterializeGeneric();

        Assert.NotEqual(first.Procedure.ProcedureId, second.Procedure.ProcedureId);
        Assert.Equal(first.Procedure.Key, second.Procedure.Key);
        Assert.Equal(first.Procedure.Name, second.Procedure.Name);
        Assert.Equal(first.Procedure.Modules, second.Procedure.Modules);
        Assert.Equal("simple-fixed-distance", preset.Origin.PresetKey);
        Assert.Equal(preset.DisplayName, preset.Origin.PresetDisplayName);
        Assert.Equal(preset.PresetRevision, preset.Origin.PresetRevision);
    }

    [Fact]
    public void GenericStartingPointsAreExplicitProductionChoicesAndMixedProofIsNot()
    {
        var generic = CrawlProcedureCatalog.Catalog
            .Where(preset => preset.Category == "Generic starting points")
            .Select(preset => preset.PresetKey)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["simple-fixed-distance", "simple-hex-step"], generic);
        Assert.Equal("Generic starting points", CrawlProcedureCatalog.Resolve("simple-fixed-distance").Category);
        Assert.Equal("Generic starting points", CrawlProcedureCatalog.Resolve("simple-hex-step").Category);
        Assert.DoesNotContain(CrawlProcedureCatalog.Catalog, preset =>
            preset.PresetKey == "mixed-house-rule");
    }

    [Fact]
    public void CampaignOwnedIdentityDoesNotRewritePresetOrigin()
    {
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");
        var materialized = preset.MaterializeGeneric();
        var campaignOwned = materialized.Procedure with
        {
            Key = "campaign-owned-procedure",
            Name = "Campaign-owned procedure"
        };

        campaignOwned.Validate();

        Assert.Equal("campaign-owned-procedure", campaignOwned.Key);
        Assert.Equal("Campaign-owned procedure", campaignOwned.Name);
        Assert.Equal("simple-fixed-distance", materialized.Origin?.PresetKey);
    }

    [Fact]
    public async Task RemovingOriginMetadataDoesNotChangePersistedProcedureExecutionState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new ExpeditionStartService(store, new HexCrawlService(store), new CampaignProcedureService(store));

        var started = await service.StartNonSpatialAsync("alice", "Origin independence", new ProcedureStartSelection(PresetKey: "simple-fixed-distance"), new NonSpatialCrawlSessionContext("Procedure clock"));
        var pinned = started.CampaignProcedure;
        var runtimeBefore = GenericProcedureRuntime.Bind(pinned);

        Assert.Equal("simple-fixed-distance", started.ProcedureOrigin?.PresetKey);

        var save = await store.SaveExpeditionAsync(
            started with { ProcedureOrigin = null },
            started.Version);
        Assert.Equal(SaveOutcome.Saved, save.Outcome);

        var loaded = await store.GetExpeditionAsync(started.Id, "alice");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.ProcedureOrigin);
        Assert.Equal(pinned, loaded.CampaignProcedure);
        AssertRuntimeEquivalent(runtimeBefore, GenericProcedureRuntime.Bind(loaded.CampaignProcedure));
    }

    private static void AssertRuntimeEquivalent(GenericProcedureRuntime expected, GenericProcedureRuntime actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Movement, actual.Movement);
        Assert.Equal(expected.TurnPolicy, actual.TurnPolicy);
        Assert.Equal(expected.Navigation, actual.Navigation);
        Assert.Equal(expected.Encounters, actual.Encounters);
        Assert.Equal(expected.ResolutionHelpers, actual.ResolutionHelpers);
    }
}
