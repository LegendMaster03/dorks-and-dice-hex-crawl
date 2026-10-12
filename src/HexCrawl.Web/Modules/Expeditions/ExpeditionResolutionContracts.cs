using HexCrawl.Application;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

public sealed record ResolveProcedureInputsRequest(
    long ExpectedVersion,
    double? ExpectedDistance = null,
    bool SuppressesNavigationCheck = false,
    bool DeliberateDoubleBack = false,
    int? NavigationDifficultyClass = null,
    int NavigationModifier = 0,
    int? FailureVeerSteps = null,
    Guid? KeyedLocationId = null,
    string? TravelDistanceRule = null,
    int? BaseSpeedFeet = null,
    string? Terrain = null,
    string? Route = null,
    IReadOnlyList<string>? NavigationRiskFactors = null)
{
    public ProcedureResolutionHelperCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        ExpectedDistance = ExpectedDistance,
        SuppressesNavigationCheck = SuppressesNavigationCheck,
        DeliberateDoubleBack = DeliberateDoubleBack,
        NavigationDifficultyClass = NavigationDifficultyClass,
        NavigationModifier = NavigationModifier,
        FailureVeerSteps = FailureVeerSteps,
        KeyedLocationId = KeyedLocationId,
        TravelDistanceRule = TravelDistanceRule,
        BaseSpeedFeet = BaseSpeedFeet,
        Terrain = Terrain,
        Route = Route,
        NavigationRiskFactors = NavigationRiskFactors ?? []
    };
}

public sealed record ResolveCellProcedureInputsRequest(
    long ExpectedVersion,
    DistanceMeasure? ExpectedDistance = null,
    bool SuppressesNavigationCheck = false,
    bool DeliberateDoubleBack = false,
    int? NavigationDifficultyClass = null,
    int NavigationModifier = 0,
    double? FailureVeerDegrees = null,
    Guid? KeyedLocationId = null,
    string? TravelDistanceRule = null,
    int? BaseSpeedFeet = null,
    string? Terrain = null,
    string? Route = null,
    IReadOnlyList<string>? NavigationRiskFactors = null)
{
    public void Validate()
    {
        if (ExpectedVersion <= 0)
            throw new InvalidOperationException("Expected expedition version is required.");
        if (FailureVeerDegrees is { } veer
            && (!double.IsFinite(veer) || veer == 0 || Math.Abs(veer) > 180))
            throw new InvalidOperationException("Failure veer must be in finite nonzero degrees, at most 180.");
    }

    public CellProcedureResolutionHelperCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        ExpectedDistance = ExpectedDistance,
        SuppressesNavigationCheck = SuppressesNavigationCheck,
        DeliberateDoubleBack = DeliberateDoubleBack,
        NavigationDifficultyClass = NavigationDifficultyClass,
        NavigationModifier = NavigationModifier,
        FailureVeerDegrees = FailureVeerDegrees,
        KeyedLocationId = KeyedLocationId,
        TravelDistanceRule = TravelDistanceRule,
        BaseSpeedFeet = BaseSpeedFeet,
        Terrain = Terrain,
        Route = Route,
        NavigationRiskFactors = NavigationRiskFactors ?? []
    };
}
