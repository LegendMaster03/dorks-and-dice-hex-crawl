using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
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

    private static readonly string[] FullyExecutableProofPresetKeys =
    [
        "alexandrian-advanced",
        CrawlProcedureCatalog.MixedHouseRulePresetKey
    ];

    private static readonly string[] NativeRuntimeModuleKeys =
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
            ["alexandrian-advanced"] = NativeRuntimeModuleKeys,
            [CrawlProcedureCatalog.MixedHouseRulePresetKey] = NativeRuntimeModuleKeys
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
    public void OnlyAuditedFullyExecutableProofPresetsBindCompleteRuntime()
    {
        foreach (var presetKey in RequiredProofPresetKeys)
        {
            var procedure = CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;
            var shouldBind = FullyExecutableProofPresetKeys.Contains(presetKey, StringComparer.Ordinal);

            if (shouldBind)
            {
                _ = GenericProcedureRuntime.Bind(procedure);
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() => GenericProcedureRuntime.Bind(procedure));
                Assert.Contains("missing required execution handler", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void OneRingProofPreservesJourneyStructureWithoutFabricatedIntervalCore()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey)
            .MaterializeGeneric().Procedure;

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
    public void Dnd2024TerrainRepresentsConditionalArcticFastPaceStructurally()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey)
            .MaterializeGeneric().Procedure;
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
        Assert.Equal("map<string>", terrain.Module.ConfigurationSchema["terrainAdjustments"].Type);
        Assert.Equal("map<string>", terrain.Mechanic.ParameterSchema["terrainAdjustments"].Type);
    }

    [Fact]
    public void Phase3ModuleShellsDoNotInventBehaviorReadsOrBroadFallbackSources()
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
    public void GenericDefinitionKeysAndImplementationTypesRemainSystemNeutral()
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
    public void DeclarativePhase3MechanicsAreVersionedRecognizedAndNonAutomatic()
    {
        var declarative = GenericProcedureCatalog.Mechanics
            .Where(mechanic => mechanic.CompatibilityTags.Contains("phase-3", StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(declarative);
        Assert.All(declarative, mechanic =>
        {
            Assert.Equal(1, mechanic.Version);
            Assert.Equal(GenericProcedureExecutionHandlers.DeclarativeContract, mechanic.ExecutionHandler);
            Assert.Contains(mechanic.AutomationLevel, new[]
            {
                ProcedureAutomationLevel.Assisted,
                ProcedureAutomationLevel.Manual
            });
        });

        var mixed = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey)
            .MaterializeGeneric().Procedure;
        _ = GenericProcedureRuntime.Bind(mixed);
    }

    [Fact]
    public void AutomaticMechanicCanNotUseDeclarativeContractHandler()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey)
            .MaterializeGeneric().Procedure;
        var invalid = procedure with
        {
            Modules = procedure.Modules.Select(module =>
                module.Module.Key == GenericProcedureCatalog.MovementBudgetModule
                    ? module with
                    {
                        Mechanic = module.Mechanic with { AutomationLevel = ProcedureAutomationLevel.Automatic }
                    }
                    : module).ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(invalid.Validate);
        Assert.Contains("declarative", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Automatic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredProducerIsAnErrorWhenOnlySelectedModuleSourceIsPermitted()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.BxPresetKey)
            .MaterializeGeneric().Procedure;
        var constrained = procedure with
        {
            Modules = procedure.Modules.Select(module =>
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
                    : module).ToArray()
        };

        var report = constrained.EvaluateDependencies();
        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.MissingRequiredProducer
            && issue.ModuleKey == GenericProcedureCatalog.NavigationOutcomeModule
            && issue.InputKey == "navigation.check-result");
        Assert.Throws<InvalidOperationException>(constrained.Validate);
    }

    [Fact]
    public void UnresolvedMultiSourceInputExposesEveryPermittedResolutionSource()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.BxPresetKey)
            .MaterializeGeneric().Procedure;

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
    }

    [Fact]
    public void OseAliasUsesTheSameBxCreationRecipe()
    {
        var bx = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.BxPresetKey);
        var ose = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OseClassicPresetKey);

        Assert.NotEqual(bx.PresetKey, ose.PresetKey);
        Assert.Same(bx.Recipe, ose.Recipe);

        var id = Guid.Parse("0a9f9e8a-72f7-4c1d-9adb-9d2993e6db03");
        var bxProcedure = bx.MaterializeGeneric(id).Procedure;
        var oseProcedure = ose.MaterializeGeneric(id).Procedure;
        Assert.Equal(SnapshotSignature(bxProcedure), SnapshotSignature(oseProcedure));
    }

    [Fact]
    public void MixedHouseRuleCombinesIndependentGenericFamiliesAndRuntimeIgnoresPresetIdentity()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.MixedHouseRulePresetKey)
            .MaterializeGeneric().Procedure;

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
        var original = GenericProcedureRuntime.Bind(procedure);
        var rebound = GenericProcedureRuntime.Bind(detached);
        Assert.Equal(original.Time, rebound.Time);
        Assert.Equal(original.Movement, rebound.Movement);
        Assert.Equal(original.Navigation, rebound.Navigation);
    }

    [Fact]
    public async Task GenericOnlyProcedurePersistsNormallyAndFailsClearlyOnlyWhenExecutionIsAttempted()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey)
            .MaterializeGeneric().Procedure;
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "One Ring proof",
            new NonSpatialSessionState { Id = Guid.NewGuid() },
            new NonSpatialCrawlSessionContext("Journey"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "alice",
            1,
            now,
            now);

        var created = await store.CreateExpeditionAsync(expedition);
        var loaded = await store.GetExpeditionAsync(created.Id, "alice");

        Assert.NotNull(loaded);
        Assert.Equal(procedure, loaded!.CampaignProcedure);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GenericProcedureRuntime.Bind(loaded.CampaignProcedure));
        Assert.Contains("missing required execution handler", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinnedProcedureSnapshotSurvivesRestartAndOriginRemovalUnchanged()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Pinned proof",
                "alexandrian-advanced",
                new NonSpatialCrawlSessionContext("Clock")));
        var pinned = started.CampaignProcedure;

        var save = await store.SaveExpeditionAsync(started with { ProcedureOrigin = null }, started.Version);
        Assert.Equal(SaveOutcome.Saved, save.Outcome);
        var loaded = await store.GetExpeditionAsync(started.Id, "alice");

        Assert.NotNull(loaded);
        Assert.Null(loaded!.ProcedureOrigin);
        Assert.Equal(pinned, loaded.CampaignProcedure);
        _ = GenericProcedureRuntime.Bind(loaded.CampaignProcedure);
    }

    private static bool ContainsToken(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string SnapshotSignature(CampaignProcedure procedure) =>
        string.Join("|", procedure.Modules.Select(module =>
            $"{module.Module.Key}:{module.Mechanic.Key}:{module.Mechanic.Version}:" +
            string.Join(",", module.Parameters.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => $"{value.Key}={value.Value}"))));
}
