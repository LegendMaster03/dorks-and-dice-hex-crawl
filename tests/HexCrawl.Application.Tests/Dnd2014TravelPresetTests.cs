using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application.Tests;

public sealed class Dnd2014TravelPresetTests
{
    [Fact]
    public void PresetMaterializesAsAnIndependentValidGenericProcedure()
    {
        var preset = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2014PresetKey);
        var materialized = preset.MaterializeGeneric();

        Assert.Equal("D&D 5e / 2014", preset.DisplayName);
        Assert.Equal(CrawlProcedureCatalog.Dnd2014PresetKey, materialized.Origin?.PresetKey);
        Assert.False(materialized.Procedure.EvaluateDependencies().HasErrors);
        materialized.Procedure.Validate();

        var counterpart = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).MaterializeGeneric();
        Assert.NotEqual(materialized.Procedure.Key, counterpart.Procedure.Key);
        Assert.NotSame(preset.Recipe, CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).Recipe);
        Assert.Equal(
            TimeSpan.FromHours(1).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Module(materialized.Procedure, GenericProcedureCatalog.TimeIntervalModule).Parameters["durationTicks"]);
    }

    [Fact]
    public void TravelPacesRetainIndependent2014HourlyAndDailyRates()
    {
        var procedure = Procedure();
        var movement = Module(procedure, GenericProcedureCatalog.MovementBudgetModule);
        var policy = MovementCompositionPolicyResolver.Resolve(procedure);

        Assert.Equal(MovementCompositionPolicySupport.Supported, policy.Support);
        Assert.Equal("speed-and-pace", policy.BudgetModel);
        Assert.Equal("fast;normal;slow", movement.Parameters["travelModeKeys"]);
        var rates = Assert.IsType<MovementPaceDistances>(policy.PaceDistances);
        Assert.Equal("mi", rates.DistanceUnit);
        Assert.Equal(4, rates.PerHour["fast"]);
        Assert.Equal(3, rates.PerHour["normal"]);
        Assert.Equal(2, rates.PerHour["slow"]);
        Assert.Equal(30, rates.PerDay["fast"]);
        Assert.Equal(24, rates.PerDay["normal"]);
        Assert.Equal(18, rates.PerDay["slow"]);
        Assert.Equal("passive-perception-minus-5", rates.Effects["fast"]);
        Assert.Equal("none", rates.Effects["normal"]);
        Assert.Equal("stealth-available", rates.Effects["slow"]);
        Assert.NotEqual(rates.PerHour["fast"] * 8, rates.PerDay["fast"]);
        Assert.NotEqual(rates.PerHour["slow"] * 8, rates.PerDay["slow"]);

        Assert.Null(MovementCompositionPolicyResolver.Resolve(
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey)
                .MaterializeGeneric().Procedure).PaceDistances);
        Assert.Equal("map<string>", movement.Module.ConfigurationSchema["paceRatesPerHour"].Type);
        Assert.Equal("map<string>", movement.Mechanic.ParameterSchema["paceRatesPerDay"].Type);
    }

    [Fact]
    public void Uses2014TerrainRatherThan2024TerrainLimits()
    {
        var old = Module(Procedure(), GenericProcedureCatalog.TerrainMovementModule);
        var newer = Module(
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).MaterializeGeneric().Procedure,
            GenericProcedureCatalog.TerrainMovementModule);

        Assert.Equal("multiplier", old.Parameters["adjustmentModel"]);
        Assert.Equal("normal=1;difficult=0.5", old.Parameters["terrainAdjustments"]);
        Assert.Equal("maximum-pace", newer.Parameters["adjustmentModel"]);
        Assert.Contains("mountain=slow", newer.Parameters["terrainAdjustments"], StringComparison.Ordinal);
    }

    [Fact]
    public void Captures2014ActivitiesNavigationForagingAndForcedMarchWithoutInventedEncounterCadence()
    {
        var procedure = Procedure();
        var activities = Module(procedure, GenericProcedureCatalog.PartyActivitiesModule);
        var navigation = Module(procedure, GenericProcedureCatalog.NavigationOutcomeModule);
        var forage = Module(procedure, GenericProcedureCatalog.ForagingModule);
        var resources = Module(procedure, GenericProcedureCatalog.ResourceConsumptionModule);
        var forced = Module(procedure, GenericProcedureCatalog.ForcedTravelModule);
        var effects = Module(procedure, GenericProcedureCatalog.PersistentEffectsModule);

        Assert.Equal("watch;navigate;map;track;forage;stealth", activities.Parameters["activityKeys"]);
        Assert.Equal("dm-called-survival-check", navigation.Parameters["checkTriggerModel"]);
        Assert.Equal("dm-adjudicated", navigation.Parameters["directionalErrorModel"]);
        Assert.Equal("wisdom-survival-check", forage.Parameters["resolutionModel"]);
        Assert.Equal("0", forage.Parameters["timeCost"]);
        Assert.Equal("food;water", resources.Parameters["resourceKinds"]);
        Assert.Equal("8", forced.Parameters["normalTravelLimit"]);
        Assert.Equal("escalating-constitution-save", forced.Parameters["checkModel"]);
        Assert.Equal("exhaustion", forced.Parameters["failureConsequence"]);
        Assert.Equal("exhaustion", effects.Parameters["effectKinds"]);
        Assert.DoesNotContain(procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.EncounterCadenceModule
            || module.Module.Key == GenericProcedureCatalog.EncounterScheduleModule);
    }

    [Theory]
    [InlineData("fast=NaN;normal=3;slow=2")]
    [InlineData("fast=0;normal=3;slow=2")]
    [InlineData("fast=4;normal=3")]
    [InlineData("fast=4;normal=3;slow=2;extra=1")]
    public void InvalidPaceRatesRejectMovementProjection(string hourlyRates)
    {
        var procedure = Procedure();
        var original = Module(procedure, GenericProcedureCatalog.MovementBudgetModule);
        var parameters = original.Parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        parameters["paceRatesPerHour"] = hourlyRates;
        var changed = procedure with
        {
            Modules = procedure.Modules.Select(module =>
                module.Module.Key == GenericProcedureCatalog.MovementBudgetModule
                    ? module with { Parameters = parameters }
                    : module).ToArray()
        };

        Assert.Equal(MovementCompositionPolicySupport.Unsupported,
            MovementCompositionPolicyResolver.Resolve(changed).Support);
    }

    private static CampaignProcedure Procedure() =>
        CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2014PresetKey).MaterializeGeneric().Procedure;

    private static MaterializedProcedureModule Module(CampaignProcedure procedure, string key) =>
        procedure.Modules.Single(module => module.Module.Key == key);
}
