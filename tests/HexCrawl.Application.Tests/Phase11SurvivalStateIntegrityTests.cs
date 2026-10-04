using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase11SurvivalStateIntegrityTests
{
    private static readonly ExpeditionConsequenceProvenance Dm = new(
        ExpeditionConsequenceSourceKind.Dm,
        "phase-11-integrity-test");

    [Fact]
    public void ExposureOccurrenceIdentityIsUniqueAcrossProgressBuckets()
    {
        var occurrenceId = Guid.NewGuid();
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var state = new ExpeditionSurvivalState
        {
            Exposure =
            [
                new ExpeditionExposureProgress
                {
                    Id = Guid.NewGuid(),
                    ExposureKey = "cold",
                    Target = target,
                    Amount = 1,
                    Unit = "hours",
                    SourceOccurrenceIds = [occurrenceId],
                    Provenance = [Dm]
                },
                new ExpeditionExposureProgress
                {
                    Id = Guid.NewGuid(),
                    ExposureKey = "altitude",
                    Target = target,
                    Amount = 1,
                    Unit = "hours",
                    SourceOccurrenceIds = [occurrenceId],
                    Provenance = [Dm]
                }
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(() => state.Validate(CrawlPartySheet.Empty));
        Assert.Contains("unique across survival state", error.Message, StringComparison.Ordinal);
    }
}
