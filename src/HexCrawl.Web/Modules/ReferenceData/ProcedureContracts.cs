using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Api;

public sealed record DiceRollFormulaContract(int DiceCount, int DieSides, int Modifier)
{
    public static DiceRollFormulaContract From(DiceRollFormula formula) =>
        new(formula.DiceCount, formula.DieSides, formula.Modifier);
}

public sealed record TravelResolutionHelperProfileContract(
    DiceRollFormulaContract Roll,
    double DistanceFactorPerRollPoint)
{
    public static TravelResolutionHelperProfileContract From(TravelResolutionHelperProfile profile) =>
        new(DiceRollFormulaContract.From(profile.Roll), profile.DistanceFactorPerRollPoint);
}

public sealed record NavigationResolutionHelperProfileContract(DiceRollFormulaContract CheckRoll)
{
    public static NavigationResolutionHelperProfileContract From(NavigationResolutionHelperProfile profile) =>
        new(DiceRollFormulaContract.From(profile.CheckRoll));
}

public sealed record EncounterResolutionHelperProfileContract(
    DiceRollFormulaContract CheckRoll,
    IReadOnlyList<int> WanderingResults,
    IReadOnlyList<int> KeyedLocationResults,
    int TimingSlots)
{
    public static EncounterResolutionHelperProfileContract From(EncounterResolutionHelperProfile profile) =>
        new(
            DiceRollFormulaContract.From(profile.CheckRoll),
            profile.WanderingResults.Values,
            profile.KeyedLocationResults.Values,
            profile.TimingSlots);
}

public sealed record ProcedureResolutionHelperProfileContract(
    TravelResolutionHelperProfileContract? Travel,
    NavigationResolutionHelperProfileContract? Navigation,
    EncounterResolutionHelperProfileContract? Encounter)
{
    public static ProcedureResolutionHelperProfileContract From(ProcedureResolutionHelperProfile profile) =>
        new(
            profile.Travel is null ? null : TravelResolutionHelperProfileContract.From(profile.Travel),
            profile.Navigation is null ? null : NavigationResolutionHelperProfileContract.From(profile.Navigation),
            profile.Encounter is null ? null : EncounterResolutionHelperProfileContract.From(profile.Encounter));
}

public sealed record ProcedureRuntimeContract(
    double IntervalHours,
    TravelResolutionMode TravelResolution,
    ActualDistanceResolutionMode ActualDistanceResolution,
    EncounterCheckCadence EncounterCadence,
    bool UsesNavigationChecks,
    bool UsesPersistentVeer,
    bool TracksIntraHexProgress,
    bool? DirectionChangesCostProgress,
    bool? SupportsDeliberateDoubleBack,
    double? StartingExitProgressFactor,
    double? NearExitProgressFactor,
    double? FarExitProgressFactor,
    double? BackExitProgressFactor,
    double? DirectionChangeProgressCostFactor,
    ProcedureResolutionHelperProfileContract? ResolutionHelpers)
{
    // Version 1 retains exactly its old JSON contract. Version 2 explicitly
    // advertises the movement handler and omits inapplicable hex factors.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MovementMechanicVersion { get; init; }

    public static ProcedureRuntimeContract From(GenericProcedureRuntime runtime)
    {
        var hex = runtime.Movement.MechanicVersion == 1 ? runtime.HexProgress : null;
        return new ProcedureRuntimeContract(
            runtime.Time.IntervalDuration.TotalHours,
            runtime.Movement.TravelResolution,
            runtime.Movement.ActualDistanceResolution,
            runtime.Encounters.Cadence,
            runtime.Navigation.UsesNavigationChecks,
            runtime.Navigation.UsesPersistentVeer,
            runtime.Movement.TracksIntraHexProgress,
            hex?.DirectionChangesCostProgress,
            hex?.SupportsDeliberateDoubleBack,
            hex?.StartingExitProgressFactor,
            hex?.NearExitProgressFactor,
            hex?.FarExitProgressFactor,
            hex?.BackExitProgressFactor,
            hex?.DirectionChangeProgressCostFactor,
            runtime.ResolutionHelpers is null ? null : ProcedureResolutionHelperProfileContract.From(runtime.ResolutionHelpers))
        {
            MovementMechanicVersion = runtime.Movement.MechanicVersion == 1
                ? null : runtime.Movement.MechanicVersion
        };
    }
}

public sealed record FocusedIntervalPolicyContract(
    FocusedIntervalPolicySupport Support,
    double? IntervalHours,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static FocusedIntervalPolicyContract From(FocusedIntervalPolicy policy) => new(
        policy.Support,
        policy.IntervalDuration?.TotalHours,
        policy.MechanicKey,
        policy.MechanicVersion,
        policy.ExecutionHandler,
        policy.UnsupportedReason);
}

public sealed record ProcedureModuleContract(
    string ModuleKey,
    string ModuleName,
    string MechanicKey,
    int MechanicVersion,
    string ExecutionHandler,
    ProcedureAutomationLevel AutomationLevel,
    IReadOnlyDictionary<string, string> Parameters)
{
    public static ProcedureModuleContract From(MaterializedProcedureModule module) => new(
        module.Module.Key,
        module.Module.DisplayName,
        module.Mechanic.Key,
        module.Mechanic.Version,
        module.Mechanic.ExecutionHandler,
        module.Mechanic.AutomationLevel,
        module.Parameters);
}

public sealed record CampaignProcedureContract(
    Guid ProcedureId,
    int Revision,
    string Key,
    string Name,
    string SchemaVersion,
    string TilingDsSymbol,
    bool IsExecutable,
    ProcedureRuntimeContract? Runtime,
    FocusedIntervalPolicyContract FocusedIntervalPolicy,
    IReadOnlyList<ProcedureModuleContract> Modules)
{
    public static CampaignProcedureContract From(CampaignProcedure procedure)
    {
        procedure.Validate();
        GenericProcedureRuntime? runtime = null;
        try
        {
            runtime = GenericProcedureRuntime.Bind(procedure);
        }
        catch (InvalidOperationException)
        {
            // Representation remains available for recognized declarative/incomplete/future snapshots.
            // Runtime execution will still fail through the authoritative binding path when attempted.
        }

        return new CampaignProcedureContract(
            procedure.ProcedureId,
            procedure.Revision,
            procedure.Key,
            procedure.Name,
            procedure.SchemaVersion,
            procedure.TilingDsSymbol,
            runtime is not null,
            runtime is null ? null : ProcedureRuntimeContract.From(runtime),
            FocusedIntervalPolicyContract.From(FocusedIntervalPolicyResolver.Resolve(procedure)),
            procedure.Modules.Select(ProcedureModuleContract.From).ToArray());
    }
}

public sealed record ProcedurePresetContract(
    string PresetKey,
    string DisplayName,
    string Description,
    string Category,
    int PresetRevision,
    CampaignProcedureContract Procedure,
    string? Attribution,
    string? Disclaimer)
{
    public static ProcedurePresetContract From(CrawlProcedurePresetDefinition preset) => new(
        preset.PresetKey,
        preset.DisplayName,
        preset.Description,
        preset.Category,
        preset.PresetRevision,
        CampaignProcedureContract.From(preset.MaterializeGeneric().Procedure),
        preset.Attribution,
        preset.Disclaimer);
}
