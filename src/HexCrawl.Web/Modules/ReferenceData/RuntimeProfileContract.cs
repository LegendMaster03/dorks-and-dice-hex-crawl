using HexCrawl.Domain.Procedure;

namespace HexCrawl.Web.Api;

public sealed record DiceRollFormulaContract(int DiceCount, int DieSides, int Modifier)
{
    public static DiceRollFormulaContract From(DiceRollFormula formula) =>
        new(formula.DiceCount, formula.DieSides, formula.Modifier);

    public DiceRollFormula ToDomain() => new(DiceCount, DieSides, Modifier);
}

public sealed record TravelResolutionHelperProfileContract(
    DiceRollFormulaContract Roll,
    double DistanceFactorPerRollPoint)
{
    public static TravelResolutionHelperProfileContract From(TravelResolutionHelperProfile profile) =>
        new(DiceRollFormulaContract.From(profile.Roll), profile.DistanceFactorPerRollPoint);

    public TravelResolutionHelperProfile ToDomain() =>
        new(Roll.ToDomain(), DistanceFactorPerRollPoint);
}

public sealed record NavigationResolutionHelperProfileContract(DiceRollFormulaContract CheckRoll)
{
    public static NavigationResolutionHelperProfileContract From(NavigationResolutionHelperProfile profile) =>
        new(DiceRollFormulaContract.From(profile.CheckRoll));

    public NavigationResolutionHelperProfile ToDomain() => new(CheckRoll.ToDomain());
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

    public EncounterResolutionHelperProfile ToDomain() =>
        new(
            CheckRoll.ToDomain(),
            DiceRollResultSet.From(WanderingResults),
            DiceRollResultSet.From(KeyedLocationResults),
            TimingSlots);
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

    public ProcedureResolutionHelperProfile ToDomain() =>
        new(Travel?.ToDomain(), Navigation?.ToDomain(), Encounter?.ToDomain());
}

public sealed record RuntimeProfileContract(
    string Key,
    string Name,
    double WatchHours,
    TravelResolutionMode TravelResolution,
    ActualDistanceResolutionMode ActualDistanceResolution,
    EncounterCheckCadence EncounterCadence,
    bool UsesNavigationChecks,
    bool UsesPersistentVeer,
    bool TracksIntraHexProgress,
    bool DirectionChangesCostProgress,
    bool SupportsDeliberateDoubleBack,
    double StartingExitProgressFactor,
    double NearExitProgressFactor,
    double FarExitProgressFactor,
    double BackExitProgressFactor,
    double DirectionChangeProgressCostFactor,
    ProcedureResolutionHelperProfileContract? ResolutionHelpers = null)
{
    public static RuntimeProfileContract From(CrawlProcedureProfile profile) => new(
        profile.Key,
        profile.Name,
        profile.WatchLength.TotalHours,
        profile.TravelResolution,
        profile.ActualDistanceResolution,
        profile.EncounterCadence,
        profile.UsesNavigationChecks,
        profile.UsesPersistentVeer,
        profile.TracksIntraHexProgress,
        profile.DirectionChangesCostProgress,
        profile.SupportsDeliberateDoubleBack,
        profile.StartingExitProgressFactor,
        profile.NearExitProgressFactor,
        profile.FarExitProgressFactor,
        profile.BackExitProgressFactor,
        profile.DirectionChangeProgressCostFactor,
        profile.ResolutionHelpers is null ? null : ProcedureResolutionHelperProfileContract.From(profile.ResolutionHelpers));

    public CrawlProcedureProfile ToDomain() => new()
    {
        Key = Key,
        Name = Name,
        WatchLength = TimeSpan.FromHours(WatchHours),
        TravelResolution = TravelResolution,
        ActualDistanceResolution = ActualDistanceResolution,
        EncounterCadence = EncounterCadence,
        UsesNavigationChecks = UsesNavigationChecks,
        UsesPersistentVeer = UsesPersistentVeer,
        TracksIntraHexProgress = TracksIntraHexProgress,
        DirectionChangesCostProgress = DirectionChangesCostProgress,
        SupportsDeliberateDoubleBack = SupportsDeliberateDoubleBack,
        StartingExitProgressFactor = StartingExitProgressFactor,
        NearExitProgressFactor = NearExitProgressFactor,
        FarExitProgressFactor = FarExitProgressFactor,
        BackExitProgressFactor = BackExitProgressFactor,
        DirectionChangeProgressCostFactor = DirectionChangeProgressCostFactor,
        ResolutionHelpers = ResolutionHelpers?.ToDomain()
    };
}

