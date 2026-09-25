using HexCrawl.Application;

namespace HexCrawl.Web.Api;

public sealed record ResolveProcedureInputsRequest(
    long ExpectedVersion,
    double? ExpectedDistance = null,
    bool SuppressesNavigationCheck = false,
    bool DeliberateDoubleBack = false,
    int? NavigationDifficultyClass = null,
    int NavigationModifier = 0,
    int? FailureVeerSteps = null,
    Guid? KeyedLocationId = null)
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
        KeyedLocationId = KeyedLocationId
    };
}
