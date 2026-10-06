using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureReferenceServiceTests
{
    [Fact]
    public void ReferenceUsesMaterializedSnapshotMetadataInsteadOfCurrentCatalog()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).MaterializeGeneric();
        var terrain = materialized.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TerrainMovementModule);
        const string storedDisplayName = "Pinned terrain behavior name";
        const string storedDescription = "Pinned description from the saved procedure revision.";
        var stored = terrain with
        {
            Mechanic = terrain.Mechanic with
            {
                DisplayName = storedDisplayName,
                Description = storedDescription
            },
            Parameters = new Dictionary<string, string>(terrain.Parameters, StringComparer.Ordinal)
            {
                ["futureStoredParameter"] = "preserve-me"
            }
        };
        var procedure = materialized.Procedure with
        {
            Modules = materialized.Procedure.Modules
                .Select(module => module.Module.Key == terrain.Module.Key ? stored : module)
                .ToArray()
        };

        var reference = ProcedureReferenceService.Generate(procedure, null);
        var documented = Modules(reference).Single(module => module.ModuleKey == terrain.Module.Key);

        Assert.Equal(storedDisplayName, documented.Mechanic.DisplayName);
        Assert.Equal(storedDescription, documented.Mechanic.Description);
        var unknown = Assert.Single(documented.Parameters, parameter => parameter.Key == "futureStoredParameter");
        Assert.True(unknown.IsUnknown);
        Assert.Equal("preserve-me", unknown.RawValue);
        Assert.NotEqual(
            GenericProcedureCatalog.ResolveMechanic(terrain.Mechanic.Key).DisplayName,
            documented.Mechanic.DisplayName);
    }

    [Fact]
    public void Dnd2024ReferencePreservesConditionalArcticTerrainState()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).MaterializeGeneric();

        var reference = ProcedureReferenceService.Generate(materialized.Procedure, materialized.Origin);
        var terrain = Modules(reference).Single(module =>
            module.ModuleKey == GenericProcedureCatalog.TerrainMovementModule);
        var parameter = Assert.Single(terrain.Parameters, value => value.Key == "terrainAdjustments");

        Assert.Equal("map<string>", parameter.Type);
        Assert.Contains(parameter.MapEntries, value =>
            value.Key == "arctic" && value.Value == "fast-if-appropriately-equipped");
        Assert.DoesNotContain(parameter.MapEntries, value =>
            value.Key == "arctic" && value.Value == "fast");
    }

    [Fact]
    public void OneRingReferenceDoesNotFabricateRepeatingInterval()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey).MaterializeGeneric();

        var reference = ProcedureReferenceService.Generate(materialized.Procedure, materialized.Origin);
        var modules = Modules(reference);

        Assert.DoesNotContain(modules, module =>
            module.ModuleKey == GenericProcedureCatalog.TimeIntervalModule);
        Assert.DoesNotContain(
            modules.SelectMany(module => module.RequiredInputs),
            input => input.Key == "time.interval-duration");
        Assert.Contains(modules, module =>
            module.ModuleKey == GenericProcedureCatalog.MovementBudgetModule
            && module.Mechanic.Key == GenericProcedureCatalog.JourneyProgressBudgetMechanic);
    }

    [Fact]
    public void StructuralAndUnsupportedMechanicsRemainDocumentable()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey).MaterializeGeneric();
        var structural = materialized.Procedure.Modules.First(module =>
            module.Mechanic.ExecutionHandler == GenericProcedureExecutionHandlers.DeclarativeContract);
        var unsupported = structural with
        {
            Mechanic = structural.Mechanic with
            {
                DisplayName = "Future stored structural behavior",
                Description = "Stored future behavior description",
                ExecutionHandler = "future.reference.handler",
                Version = 42
            },
            Parameters = new Dictionary<string, string>(structural.Parameters, StringComparer.Ordinal)
            {
                ["futureFlag"] = "retained"
            }
        };
        var procedure = materialized.Procedure with
        {
            Modules = materialized.Procedure.Modules
                .Select(module => module.Module.Key == structural.Module.Key ? unsupported : module)
                .ToArray()
        };

        var reference = ProcedureReferenceService.Generate(procedure);
        var documented = Modules(reference).Single(module => module.ModuleKey == structural.Module.Key);

        Assert.False(reference.IsExecutable);
        Assert.Equal(ProcedureReferenceExecutionSupport.Unsupported, documented.Mechanic.ExecutionSupport);
        Assert.Equal("Future stored structural behavior", documented.Mechanic.DisplayName);
        Assert.Equal("Stored future behavior description", documented.Mechanic.Description);
        Assert.Equal("future.reference.handler", documented.Mechanic.ExecutionHandler);
        Assert.Equal(42, documented.Mechanic.Version);
        Assert.Contains(documented.Parameters, value =>
            value.Key == "futureFlag" && value.RawValue == "retained");
    }

    [Fact]
    public void ParameterPresentationSupportsCurrentGenericShapesWithoutChangingRawValues()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).MaterializeGeneric();
        var selected = materialized.Procedure.Modules.First();
        var definitions = new Dictionary<string, ProcedureParameterDefinition>(StringComparer.Ordinal)
        {
            ["text"] = new("string"),
            ["choice"] = new("enum"),
            ["enabled"] = new("boolean"),
            ["count"] = new("integer"),
            ["ratio"] = new("number"),
            ["keys"] = new("key-list"),
            ["mapping"] = new("map<string>"),
            ["durationTicks"] = new("integer", Description: "Duration in ticks.")
        };
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = "stored text",
            ["choice"] = "option-b",
            ["enabled"] = "true",
            ["count"] = "3",
            ["ratio"] = "1.5",
            ["keys"] = "travel;navigate;forage",
            ["mapping"] = "arctic=fast-if-appropriately-equipped;forest=slow",
            ["durationTicks"] = TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture),
            ["unknown"] = "future-value"
        };
        var changed = selected with
        {
            Mechanic = selected.Mechanic with { ParameterSchema = definitions },
            Parameters = parameters
        };
        var procedure = materialized.Procedure with { Modules = [changed] };

        var module = Assert.Single(ProcedureReferenceService.Generate(procedure).Sections).Modules.Single();

        Assert.Equal("Yes", Parameter(module, "enabled").DisplayValue);
        Assert.Equal("3", Parameter(module, "count").DisplayValue);
        Assert.Equal("1.5", Parameter(module, "ratio").DisplayValue);
        Assert.Equal(new[] { "travel", "navigate", "forage" }, Parameter(module, "keys").ListValues);
        Assert.Contains(Parameter(module, "mapping").MapEntries, entry =>
            entry.Key == "arctic" && entry.Value == "fast-if-appropriately-equipped");
        Assert.Equal("4 hours", Parameter(module, "durationTicks").DisplayValue);
        Assert.Equal(
            TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture),
            Parameter(module, "durationTicks").RawValue);
        Assert.True(Parameter(module, "unknown").IsUnknown);
    }

    [Fact]
    public void InputsExposeCompleteAllowedSourceSetAndDependencyKindsRemainDistinct()
    {
        var procedure = ProcedureComposerFixture();
        var reference = ProcedureReferenceService.Generate(procedure);
        var navigation = Modules(reference).Single(module =>
            module.ModuleKey == GenericProcedureCatalog.NavigationOutcomeModule);
        var input = Assert.Single(navigation.RequiredInputs, value => value.Key == "navigation.check-result");
        var sourceKeys = input.AllowedSources.Select(value => value.Key).ToArray();

        Assert.Contains("SelectedModule", sourceKeys);
        Assert.Contains("Dm", sourceKeys);
        Assert.Contains("OptionalProvider", sourceKeys);
        Assert.Contains("ExternalState", sourceKeys);
        Assert.Contains(navigation.Diagnostics, diagnostic =>
            diagnostic.Kind == ProcedureDependencyIssueKind.UnresolvedInput);

        var oneRing = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey)
            .MaterializeGeneric().Procedure;
        var movement = oneRing.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementBudgetModule);
        var intervalMechanic = GenericProcedureCatalog.ResolveMechanic(GenericProcedureCatalog.MovementBudgetMechanic);
        var changed = movement with
        {
            Mechanic = intervalMechanic,
            Parameters = new Dictionary<string, string>
            {
                ["budgetModel"] = "fixed-per-interval",
                ["baseBudget"] = "1",
                ["budgetUnit"] = "interval",
                ["limitingScope"] = "party"
            }
        };
        var broken = oneRing with
        {
            Modules = oneRing.Modules
                .Select(module => module.Module.Key == movement.Module.Key ? changed : module)
                .ToArray()
        };
        var brokenReference = ProcedureReferenceService.Generate(broken);
        var brokenMovement = Modules(brokenReference).Single(module => module.ModuleKey == movement.Module.Key);
        Assert.Contains(brokenMovement.Diagnostics, diagnostic =>
            diagnostic.Kind == ProcedureDependencyIssueKind.MissingRequiredProducer
            && diagnostic.InputKey == "time.interval-duration");
    }

    [Fact]
    public async Task SavedRevisionsGenerateIndependentExactReferencesAndCustomProcedureNeedsNoOrigin()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var references = new ProcedureReferenceService(procedures);

        var custom = await composer.CreateAsync(
            "alice",
            null,
            [new ProcedureModuleSelection(GenericProcedureCatalog.TimeIntervalModule, true)],
            []);
        var customReference = await references.GetAsync("alice", custom.ProcedureId, custom.Revision);
        Assert.Null(customReference.Origin);
        Assert.Equal(custom.Procedure.Modules.Count, Modules(customReference).Count);

        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);
        var withOrigin = ProcedureReferenceService.Generate(created.Procedure, created.ProcedureOrigin);
        var withoutOrigin = ProcedureReferenceService.Generate(created.Procedure, null);
        Assert.Equal(BehaviorFingerprint(withOrigin), BehaviorFingerprint(withoutOrigin));

        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        await composer.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            1,
            [new CampaignProcedureOverride(
                "six-hour-reference",
                GenericProcedureCatalog.TimeIntervalModule,
                null,
                null,
                new Dictionary<string, string> { ["durationTicks"] = sixHours },
                "Use longer travel intervals.")]);

        var first = await references.GetAsync("alice", created.ProcedureId, 1);
        var second = await references.GetAsync("alice", created.ProcedureId, 2);
        var firstTime = Modules(first).Single(module => module.ModuleKey == GenericProcedureCatalog.TimeIntervalModule);
        var secondTime = Modules(second).Single(module => module.ModuleKey == GenericProcedureCatalog.TimeIntervalModule);

        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal("4 hours", Parameter(firstTime, "durationTicks").DisplayValue);
        Assert.Equal("6 hours", Parameter(secondTime, "durationTicks").DisplayValue);
        Assert.NotEqual(Parameter(firstTime, "durationTicks").RawValue, Parameter(secondTime, "durationTicks").RawValue);
        Assert.False(firstTime.IsModified);
        Assert.True(secondTime.IsModified);
        Assert.Contains(secondTime.ModificationNotes, note => note == "Use longer travel intervals.");
    }

    private static CampaignProcedure ProcedureComposerFixture()
    {
        var module = GenericProcedureCatalog.ResolveModule(GenericProcedureCatalog.NavigationOutcomeModule);
        var mechanic = GenericProcedureCatalog.ResolveMechanic(GenericProcedureCatalog.NavigationOutcomePolicyMechanic);
        var procedure = new CampaignProcedure
        {
            ProcedureId = Guid.NewGuid(),
            Revision = 1,
            Key = "reference-input-sources",
            Name = "Reference input sources",
            Modules =
            [
                new MaterializedProcedureModule(
                    module,
                    mechanic,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["checkTriggerModel"] = "manual-or-procedure",
                        ["failureStateModel"] = "lost-state",
                        ["directionalErrorModel"] = "manual-off-course",
                        ["recognitionModel"] = "manual",
                        ["reorientationModel"] = "manual"
                    })
            ],
            Overrides = []
        };
        procedure.Validate();
        return procedure;
    }

    private static string BehaviorFingerprint(ProcedureReference reference) =>
        string.Join(
            "|",
            Modules(reference).Select(module =>
                $"{module.ModuleKey}:{module.DisplayName}:{module.Mechanic.Key}:{module.Mechanic.DisplayName}:"
                + string.Join(",", module.Parameters.Select(parameter => $"{parameter.Key}={parameter.RawValue}"))));

    private static IReadOnlyList<ProcedureReferenceModule> Modules(ProcedureReference reference) =>
        reference.Sections.SelectMany(section => section.Modules).ToArray();

    private static ProcedureReferenceParameter Parameter(ProcedureReferenceModule module, string key) =>
        module.Parameters.Single(value => value.Key == key);
}
