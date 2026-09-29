using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase3ProofMatrixTests
{
    private static readonly string[] RequiredProofPresetKeys =
    [
        CrawlProcedureCatalog.BxPresetKey,
        CrawlProcedureCatalog.Adnd2ePresetKey,
        CrawlProcedureCatalog.Dnd35PresetKey,
        CrawlProcedureCatalog.Dnd2024PresetKey,
        CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey,
        CrawlProcedureCatalog.ForbiddenLandsPresetKey,
        CrawlProcedureCatalog.WorldsWithoutNumberPresetKey,
        CrawlProcedureCatalog.OneRing2ePresetKey,
        "alexandrian-advanced",
        CrawlProcedureCatalog.MixedHouseRulePresetKey
    ];

    private static readonly string[] FullyNativeProofPresetKeys =
    [
        "alexandrian-advanced",
        CrawlProcedureCatalog.MixedHouseRulePresetKey
    ];

    private static readonly string[] CompatibilityModuleKeys =
    [
        GenericProcedureCatalog.TimeIntervalModule,
        GenericProcedureCatalog.MovementResolutionModule,
        GenericProcedureCatalog.HexProgressModule,
        GenericProcedureCatalog.NavigationModule,
        GenericProcedureCatalog.EncounterCadenceModule,
        GenericProcedureCatalog.ResolutionHelpersModule
    ];

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedPhase2Modules =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [CrawlProcedureCatalog.BxPresetKey] =
                [GenericProcedureCatalog.TimeIntervalModule, GenericProcedureCatalog.EncounterCadenceModule],
            [CrawlProcedureCatalog.Adnd2ePresetKey] = [GenericProcedureCatalog.TimeIntervalModule],
            [CrawlProcedureCatalog.Dnd35PresetKey] = [GenericProcedureCatalog.TimeIntervalModule],
            [CrawlProcedureCatalog.Dnd2024PresetKey] = [GenericProcedureCatalog.TimeIntervalModule],
            [CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey] = [GenericProcedureCatalog.TimeIntervalModule],
            [CrawlProcedureCatalog.ForbiddenLandsPresetKey] = [GenericProcedureCatalog.TimeIntervalModule],
            [CrawlProcedureCatalog.WorldsWithoutNumberPresetKey] = [GenericProcedureCatalog.TimeIntervalModule],
            [CrawlProcedureCatalog.OneRing2ePresetKey] = [],
            ["alexandrian-advanced"] = CompatibilityModuleKeys,
            [CrawlProcedureCatalog.MixedHouseRulePresetKey] = CompatibilityModuleKeys
        };

    public static IEnumerable<object[]> RequiredProofPresets() =>
        RequiredProofPresetKeys.Select(key => new object[] { key });

    [Theory]
    [MemberData(nameof(RequiredProofPresets))]
    public void RequiredProofPresetResolvesMaterializesAndHasNoDependencyErrors(string presetKey)
    {
        var preset = CrawlProcedureCatalog.Resolve(presetKey);
        var materialized = preset.MaterializeGeneric();

        preset.Validate();
        materialized.Procedure.Validate();
        materialized.CompatibilityProfile?.Validate();

        var report = materialized.Procedure.EvaluateDependencies();
        Assert.False(report.HasErrors, string.Join(Environment.NewLine, report.Issues.Select(issue => issue.Message)));
        Assert.Equal(preset.PresetKey, materialized.Origin?.PresetKey);
        Assert.Equal(preset.PresetRevision, materialized.Origin?.PresetRevision);
        Assert.DoesNotContain(materialized.Procedure.Modules, module =>
            module.Mechanic.ExecutionHandler.Contains(preset.PresetKey, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(RequiredProofPresets))]
    public void ProofPresetContainsOnlyAuditedPhase2NativeModules(string presetKey)
    {
        var procedure = CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;
        var actual = procedure.Modules
            .Where(module => module.Mechanic.ExecutionHandler.StartsWith("crawl-profile.", StringComparison.Ordinal))
            .Select(module => module.Module.Key)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expected = ExpectedPhase2Modules[presetKey]
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OnlyAuditedFullyNativeProofPresetsProjectAndBindCompleteRuntime()
    {
        foreach (var presetKey in RequiredProofPresetKeys)
        {
            var materialized = CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric();
            var shouldBind = FullyNativeProofPresetKeys.Contains(presetKey, StringComparer.Ordinal);

            Assert.Equal(shouldBind, materialized.CompatibilityProfile is not null);
            Assert.Equal(
                shouldBind,
                CampaignProcedureCompatibilityProjector.TryProject(materialized.Procedure, out _));

            if (shouldBind)
            {
                _ = GenericProcedureRuntime.Bind(materialized.Procedure);
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() =>
                    GenericProcedureRuntime.Bind(materialized.Procedure));
                Assert.Contains("missing required execution handler", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void OneRingProofHasNoFabricatedIntervalCoreOrCompatibilityProfile()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey)
            .MaterializeGeneric();
        var procedure = materialized.Procedure;

        Assert.Null(materialized.CompatibilityProfile);
        Assert.DoesNotContain(procedure.Modules, module =>
            module.Mechanic.ExecutionHandler.StartsWith("crawl-profile.", StringComparison.Ordinal));
        Assert.DoesNotContain(procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);

        var movement = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementBudgetModule);
        Assert.Equal(GenericProcedureCatalog.JourneyProgressBudgetMechanic, movement.Mechanic.Key);
        Assert.DoesNotContain("time.interval-duration", movement.Mechanic.InputContract);

        var activities = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.PartyActivitiesModule);
        Assert.Equal(GenericProcedureCatalog.JourneyRoleActivityPolicyMechanic, activities.Mechanic.Key);
        Assert.DoesNotContain("time.interval-duration", activities.Mechanic.InputContract);
        Assert.DoesNotContain("movement.budget", activities.Mechanic.InputContract);

        var process = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);
        Assert.Contains("participant.activity-state", process.Mechanic.InputContract);
        Assert.Contains("movement.terrain-adjustment", process.Mechanic.InputContract);
        Assert.Contains("journey.progress", process.Mechanic.OutputContract);

        var events = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.JourneyEventsModule);
        Assert.Equal(GenericProcedureCatalog.ProgressTriggeredJourneyEventPolicyMechanic, events.Mechanic.Key);
        Assert.Contains("journey.progress", events.Mechanic.InputContract);
        Assert.Contains("effects.transient", events.Mechanic.OutputContract);

        var effects = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.PersistentEffectsModule);
        Assert.Equal(["effects.transient"], effects.Mechanic.InputContract);
        Assert.DoesNotContain("resource.consumed", effects.Mechanic.InputContract);

        var report = procedure.EvaluateDependencies();
        Assert.False(report.HasErrors);
        Assert.DoesNotContain(report.Issues, issue =>
            string.Equals(issue.InputKey, "time.interval-duration", StringComparison.Ordinal));
    }

    [Fact]
    public void Dnd2024TerrainMapsTerrainTagsToMaximumPaceStates()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey)
            .MaterializeGeneric()
            .Procedure;
        var terrain = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TerrainMovementModule);

        Assert.Equal("maximum-pace", terrain.Parameters["adjustmentModel"]);
        var mapping = terrain.Parameters["terrainAdjustments"];
        Assert.Contains("arctic=fast-if-appropriately-equipped", mapping, StringComparison.Ordinal);
        Assert.DoesNotContain("arctic=fast;", mapping, StringComparison.Ordinal);
        Assert.Contains("grassland=fast", mapping, StringComparison.Ordinal);
        Assert.Contains("mountain=slow", mapping, StringComparison.Ordinal);
        Assert.Contains("swamp=slow", mapping, StringComparison.Ordinal);
        Assert.Contains("forest=normal", mapping, StringComparison.Ordinal);
        Assert.Contains("waterborne=special", mapping, StringComparison.Ordinal);
        Assert.DoesNotContain("fast=1", mapping, StringComparison.Ordinal);
        Assert.Equal("map<string>", terrain.Module.ConfigurationSchema["terrainAdjustments"].Type);
        Assert.Equal("map<string>", terrain.Mechanic.ParameterSchema["terrainAdjustments"].Type);
    }

    [Fact]
    public void Phase3ModuleShellsDoNotCreateBehaviorReadsOutsideSelectedMechanicContracts()
    {
        var phase3Modules = GenericProcedureCatalog.Modules
            .Where(module => module.PresentationMetadata.TryGetValue("phase", out var phase) && phase == "3")
            .ToArray();

        Assert.NotEmpty(phase3Modules);
        Assert.All(phase3Modules, module => Assert.Empty(module.Reads));
        Assert.All(
            GenericProcedureCatalog.Mechanics.Where(mechanic =>
                mechanic.CompatibilityTags.Contains("phase-3", StringComparer.Ordinal)),
            mechanic => Assert.Empty(mechanic.ExternalInputSources));
    }

    [Fact]
    public void RequiredProofPresetsUseOnlySystemNeutralGenericDefinitionKeysAndTypeNames()
    {
        string[] forbiddenTokens =
        [
            "bx", "adnd", "dnd", "pathfinder", "forbidden", "worlds-without-number",
            "one-ring", "alexandrian", "old-school-essentials"
        ];

        foreach (var module in GenericProcedureCatalog.Modules)
        {
            Assert.DoesNotContain(forbiddenTokens, token => ContainsToken(module.Key, token));
            Assert.DoesNotContain(forbiddenTokens, token => ContainsToken(module.Category, token));
        }

        foreach (var mechanic in GenericProcedureCatalog.Mechanics)
        {
            Assert.DoesNotContain(forbiddenTokens, token => ContainsToken(mechanic.Key, token));
            Assert.DoesNotContain(forbiddenTokens, token => ContainsToken(mechanic.ExecutionHandler, token));
        }

        var implementationTypes = typeof(GenericProcedureRuntime).Assembly.GetTypes()
            .Concat(typeof(CrawlProcedureCatalog).Assembly.GetTypes())
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(implementationTypes, typeName =>
            forbiddenTokens.Any(token => ContainsToken(typeName, token)));
    }

    [Fact]
    public void DeclarativePhase3MechanicsHaveExplicitVersionedHandlerSupportAndRemainNonExecutableContracts()
    {
        var declarative = GenericProcedureCatalog.Mechanics
            .Where(mechanic => mechanic.CompatibilityTags.Contains("phase-3", StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(declarative);
        Assert.All(declarative, mechanic =>
        {
            Assert.Equal(1, mechanic.Version);
            Assert.Equal(GenericProcedureExecutionHandlers.DeclarativeContract, mechanic.ExecutionHandler);
            Assert.Contains(mechanic.AutomationLevel, new[] { ProcedureAutomationLevel.Assisted, ProcedureAutomationLevel.Manual });
        });

        var mixed = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey)
            .MaterializeGeneric()
            .Procedure;
        _ = GenericProcedureRuntime.Bind(mixed);
    }

    [Fact]
    public void AutomaticMechanicCanNotUseDeclarativeContractHandler()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey)
            .MaterializeGeneric()
            .Procedure;
        var invalidModules = procedure.Modules.Select(module =>
            module.Module.Key == GenericProcedureCatalog.MovementBudgetModule
                ? module with
                {
                    Mechanic = module.Mechanic with { AutomationLevel = ProcedureAutomationLevel.Automatic }
                }
                : module).ToArray();
        var invalid = procedure with { Modules = invalidModules };

        var exception = Assert.Throws<InvalidOperationException>(invalid.Validate);
        Assert.Contains("declarative", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Automatic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredProducerIsAnErrorWhenOnlySelectedModuleSourceIsPermitted()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.BxPresetKey)
            .MaterializeGeneric()
            .Procedure;
        var constrainedModules = procedure.Modules.Select(module =>
            module.Module.Key == GenericProcedureCatalog.NavigationOutcomeModule
                ? module with
                {
                    Mechanic = module.Mechanic with
                    {
                        InputRequirements =
                        [
                            new ProcedureInputRequirement(
                                "navigation.check-result",
                                ProcedureInputSource.SelectedModule)
                        ]
                    }
                }
                : module).ToArray();
        var constrained = procedure with { Modules = constrainedModules };

        var report = constrained.EvaluateDependencies();
        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.MissingRequiredProducer
            && issue.ModuleKey == GenericProcedureCatalog.NavigationOutcomeModule
            && issue.InputKey == "navigation.check-result");
        Assert.Throws<InvalidOperationException>(constrained.Validate);
    }

    [Fact]
    public void UnresolvedInputDiagnosticExposesEveryPermittedResolutionSource()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.BxPresetKey)
            .MaterializeGeneric()
            .Procedure;

        var report = procedure.EvaluateDependencies();
        var issue = Assert.Single(report.Issues, value =>
            value.Kind == ProcedureDependencyIssueKind.UnresolvedInput
            && value.InputKey == "navigation.check-result");

        Assert.False(report.HasErrors);
        Assert.Equal(
            ProcedureInputSource.SelectedModule
            | ProcedureInputSource.Dm
            | ProcedureInputSource.OptionalProvider
            | ProcedureInputSource.ExternalState,
            issue.AllowedInputSources);
        Assert.Contains("selected-module producer", issue.Message, StringComparison.Ordinal);
        Assert.Contains("DM/manual input", issue.Message, StringComparison.Ordinal);
        Assert.Contains("optional provider", issue.Message, StringComparison.Ordinal);
        Assert.Contains("external/runtime state", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("must be resolved by DM", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OseClassicAliasesBxBehaviorWithoutCreatingASecondGenericImplementation()
    {
        var bx = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.BxPresetKey);
        var ose = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OseClassicPresetKey);

        Assert.NotEqual(bx.PresetKey, ose.PresetKey);
        Assert.Same(bx.Recipe, ose.Recipe);

        var bxProcedure = bx.MaterializeGeneric(procedureId: Guid.Parse("0a9f9e8a-72f7-4c1d-9adb-9d2993e6db03")).Procedure;
        var oseProcedure = ose.MaterializeGeneric(procedureId: bxProcedure.ProcedureId).Procedure;

        Assert.Equal(bxProcedure.Key, oseProcedure.Key);
        Assert.Equal(bxProcedure.Name, oseProcedure.Name);
        Assert.Equal(
            SnapshotSignature(bxProcedure),
            SnapshotSignature(oseProcedure));
    }

    [Fact]
    public void MixedHouseRuleCombinesIndependentGenericFamiliesAndExecutesNativeCoreWithoutPresetIdentity()
    {
        var preset = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey);
        var materialized = preset.MaterializeGeneric();
        var procedure = materialized.Procedure;

        string[] requiredStructuralModules =
        [
            GenericProcedureCatalog.MovementBudgetModule,
            GenericProcedureCatalog.TerrainMovementModule,
            GenericProcedureCatalog.PartyActivitiesModule,
            GenericProcedureCatalog.ResourceConsumptionModule,
            GenericProcedureCatalog.ForagingModule,
            GenericProcedureCatalog.CampingModule,
            GenericProcedureCatalog.ForcedTravelModule,
            GenericProcedureCatalog.PersistentEffectsModule,
            GenericProcedureCatalog.JourneyEventsModule
        ];

        Assert.All(requiredStructuralModules, moduleKey =>
            Assert.Contains(procedure.Modules, selected => selected.Module.Key == moduleKey));

        var detached = procedure with
        {
            Key = "detached-mixed-procedure",
            Name = "Detached mixed procedure"
        };
        var engine = new CrawlRuntimeEngine();
        var result = engine.Advance(
            new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)),
            detached,
            SpatialState(),
            new WatchTravelPlan(new HexDirection(0), TravelModeSelection.Normal, NavigationAidSelection.None, false, true),
            new WatchAdvanceInputs(TravelDistanceResolver.Fixed(new DistanceMeasure(2, DistanceUnit.Miles))));

        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(4), result.Expedition.ElapsedTravelTime);
    }

    [Fact]
    public void PresetRevisionChangesDoNotMutateExistingMaterializationAndRequireExplicitNewMaterialization()
    {
        var originalPreset = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey);
        var original = originalPreset.MaterializeGeneric();
        var originalTime = original.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        var revisedSelections = originalPreset.Recipe.ModuleSelections
            .Select(selection => selection.ModuleKey == GenericProcedureCatalog.TimeIntervalModule
                ? selection with
                {
                    Parameters = selection.Parameters.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Key == "durationTicks"
                            ? TimeSpan.FromHours(2).Ticks.ToString(CultureInfo.InvariantCulture)
                            : pair.Value,
                        StringComparer.Ordinal)
                }
                : selection)
            .ToArray();
        var revisedPreset = originalPreset with
        {
            PresetRevision = checked(originalPreset.PresetRevision + 1),
            Recipe = originalPreset.Recipe with { ModuleSelections = revisedSelections }
        };

        var revised = revisedPreset.MaterializeGeneric();
        var originalDuration = originalTime.Parameters["durationTicks"];
        var revisedDuration = revised.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule).Parameters["durationTicks"];

        Assert.Equal(TimeSpan.FromHours(1).Ticks.ToString(CultureInfo.InvariantCulture), originalDuration);
        Assert.Equal(TimeSpan.FromHours(2).Ticks.ToString(CultureInfo.InvariantCulture), revisedDuration);
        Assert.Equal(originalPreset.PresetRevision, original.Origin?.PresetRevision);
        Assert.Equal(revisedPreset.PresetRevision, revised.Origin?.PresetRevision);
        Assert.Equal(1, original.Procedure.Revision);
        Assert.Equal(1, revised.Procedure.Revision);
    }

    [Fact]
    public async Task RequiredProofSnapshotsRoundTripPostgresAndRestartWithoutInventingCompatibilityProfiles()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var sessions = new CrawlSessionService(store);
        var created = new List<(string PresetKey, StoredExpedition Expedition)>();

        foreach (var presetKey in RequiredProofPresetKeys)
        {
            var expedition = await sessions.StartAsync(
                "phase3",
                new StartStandaloneCrawlSessionCommand(
                    $"Phase 3 {presetKey}",
                    presetKey,
                    new NonSpatialCrawlSessionContext("Phase 3 persistence proof")));
            created.Add((presetKey, expedition));

            var shouldProject = FullyNativeProofPresetKeys.Contains(presetKey, StringComparer.Ordinal);
            Assert.Equal(shouldProject, expedition.CompatibilityProfile is not null);
        }

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();

        foreach (var (presetKey, expected) in created)
        {
            var loaded = await restarted.GetExpeditionAsync(expected.Id, "phase3");
            Assert.NotNull(loaded);
            Assert.NotNull(loaded!.CampaignProcedure);
            Assert.Equal(
                SnapshotSignature(expected.CampaignProcedure!),
                SnapshotSignature(loaded.CampaignProcedure!));

            var shouldProject = FullyNativeProofPresetKeys.Contains(presetKey, StringComparer.Ordinal);
            Assert.Equal(shouldProject, loaded.CompatibilityProfile is not null);

            foreach (var selected in expected.CampaignProcedure!.Modules.Where(module =>
                         module.Mechanic.ExecutionHandler == GenericProcedureExecutionHandlers.DeclarativeContract))
            {
                var reloaded = loaded.CampaignProcedure!.Modules.Single(module =>
                    module.Module.Key == selected.Module.Key);
                Assert.Equal(selected.Mechanic.AutomationLevel, reloaded.Mechanic.AutomationLevel);
                Assert.Equal(selected.Mechanic.Version, reloaded.Mechanic.Version);
                Assert.Equal(
                    selected.Mechanic.InputRequirements ?? [],
                    reloaded.Mechanic.InputRequirements ?? []);
                Assert.Equal(
                    selected.Mechanic.ExternalInputSources.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    reloaded.Mechanic.ExternalInputSources.OrderBy(pair => pair.Key, StringComparer.Ordinal));
                Assert.Equal(
                    selected.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    reloaded.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal));
            }
        }
    }

    [Fact]
    public async Task ExpeditionKeepsExactPinnedProcedureRevisionWhenCampaignProcedureAdvances()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var sessions = new CrawlSessionService(store);
        var started = await sessions.StartAsync(
            "phase3",
            new StartStandaloneCrawlSessionCommand(
                "Pinned revision",
                CrawlProcedureCatalog.MixedHouseRulePresetKey,
                new NonSpatialCrawlSessionContext("Pinned revision proof")));
        var pinned = Assert.IsType<CampaignProcedure>(started.CampaignProcedure);

        var revised = CampaignProcedureMaterializer.CreateRevision(
            pinned,
            [
                new CampaignProcedureOverride(
                    "longer-watch",
                    GenericProcedureCatalog.TimeIntervalModule,
                    null,
                    null,
                    new Dictionary<string, string>
                    {
                        ["durationTicks"] = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture)
                    })
            ]);

        Assert.Equal(2, revised.Revision);
        var reloaded = await store.GetExpeditionAsync(started.Id, "phase3");
        Assert.NotNull(reloaded?.CampaignProcedure);
        Assert.Equal(pinned.ProcedureId, reloaded!.CampaignProcedure!.ProcedureId);
        Assert.Equal(1, reloaded.CampaignProcedure.Revision);
        Assert.Equal(
            pinned.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule).Parameters["durationTicks"],
            reloaded.CampaignProcedure.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule).Parameters["durationTicks"]);
    }

    [Fact]
    public void MaterializedSnapshotExecutesAfterOriginPresetIsUnavailableWhenItHasACompleteNativeCore()
    {
        var ephemeralPreset = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey) with
        {
            PresetKey = "temporary-phase3-proof",
            DisplayName = "Temporary Phase 3 proof"
        };
        var materialized = ephemeralPreset.MaterializeGeneric();

        Assert.DoesNotContain(CrawlProcedureCatalog.Catalog, preset => preset.PresetKey == ephemeralPreset.PresetKey);
        _ = GenericProcedureRuntime.Bind(materialized.Procedure);

        var withoutOrigin = materialized with { Origin = null };
        _ = GenericProcedureRuntime.Bind(withoutOrigin.Procedure);
    }

    private static bool ContainsToken(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SnapshotSignature(CampaignProcedure procedure) =>
        procedure.Modules
            .OrderBy(module => module.Module.Key, StringComparer.Ordinal)
            .Select(module =>
                $"{module.Module.Key}|{module.Mechanic.Key}|{module.Mechanic.Version}|{module.Mechanic.ExecutionHandler}|{module.Mechanic.AutomationLevel}|inputs={string.Join(",", (module.Mechanic.InputRequirements ?? []).OrderBy(value => value.InputKey, StringComparer.Ordinal).Select(value => $"{value.InputKey}:{value.AllowedSources}"))}|external={string.Join(",", module.Mechanic.ExternalInputSources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}"))}|{string.Join(";", module.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))}")
            .ToArray();

    private static ExpeditionState SpatialState() => new()
    {
        Id = Guid.NewGuid(),
        Position = new WorldPoint(0, 0),
        PositionPrecision = WorldPositionPrecision.HexAnchor,
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        Navigation = new NavigationRuntimeState(false, 0),
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };
}
