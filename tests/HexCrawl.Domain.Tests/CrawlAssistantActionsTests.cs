using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class CrawlAssistantActionsTests
{
    [Fact]
    public void NonSpatialWatchSupportsPartialResumeAndCompletion()
    {
        var profile = TestProcedureProfiles.FixedDistance();
        var state = new NonSpatialSessionState { Id = Guid.NewGuid() };

        var partial = CrawlAssistantActions.RecordWatch(
            profile,
            state,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(1.5),
                new ResolutionProvenance(ResolutionSource.ProcedureDefault, "table clock"),
                "first segment"));

        Assert.Equal(TimeSpan.FromHours(1.5), partial.ElapsedTime);
        Assert.Equal(0, partial.CompletedWatches);
        Assert.NotNull(partial.ActiveWatch);
        Assert.Equal(1, partial.ActiveWatch.WatchNumber);
        Assert.Equal(profile.WatchLength, partial.ActiveWatch.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(1.5), partial.ActiveWatch.Elapsed);
        Assert.Equal(profile.WatchLength - TimeSpan.FromHours(1.5), partial.ActiveWatch.Remaining);
        Assert.Contains(partial.History, item => item.Kind == CrawlRuntimeEventKind.WatchStarted && item.Cell is null);
        Assert.Contains(partial.History, item => item.Kind == CrawlRuntimeEventKind.WatchTimeAdvanced && item.Cell is null);
        Assert.Contains(partial.History, item =>
            item.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && item.Message.Contains("watch-assistant=ProcedureDefault", StringComparison.Ordinal));

        var completed = CrawlAssistantActions.RecordWatch(
            profile,
            partial,
            new NonSpatialWatchAssistantInput(
                partial.ActiveWatch.Remaining,
                new ResolutionProvenance(ResolutionSource.ManualRoll, "manual clock"),
                "resume"));

        Assert.Equal(profile.WatchLength, completed.ElapsedTime);
        Assert.Equal(1, completed.CompletedWatches);
        Assert.Null(completed.ActiveWatch);
        Assert.Contains(completed.History, item =>
            item.Kind == CrawlRuntimeEventKind.WatchCompleted
            && item.WatchNumber == 1
            && item.Cell is null);
    }

    [Fact]
    public void NonSpatialWatchRejectsElapsedTimeBeyondRemainingWatch()
    {
        var profile = TestProcedureProfiles.FixedDistance();
        var state = new NonSpatialSessionState
        {
            Id = Guid.NewGuid(),
            ActiveWatch = new NonSpatialActiveWatchState(
                1,
                profile.WatchLength,
                profile.WatchLength - TimeSpan.FromMinutes(30))
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            CrawlAssistantActions.RecordWatch(
                profile,
                state,
                new NonSpatialWatchAssistantInput(
                    TimeSpan.FromHours(1),
                    ResolutionProvenance.ProcedureDefault)));

        Assert.Contains("exceeds the remaining", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonSpatialWatchRecordsDmOverrideWithoutInventingSpatialState()
    {
        var profile = TestProcedureProfiles.FixedDistance();
        var state = new NonSpatialSessionState { Id = Guid.NewGuid() };

        var result = CrawlAssistantActions.RecordWatch(
            profile,
            state,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(1),
                new ResolutionProvenance(ResolutionSource.DmOverride, "session ruling"),
                "advance the clock"));

        Assert.Equal(TimeSpan.FromHours(1), result.ElapsedTime);
        Assert.NotNull(result.ActiveWatch);
        Assert.All(result.History, item => Assert.Null(item.Cell));
        Assert.Contains(result.History, item =>
            item.Kind == CrawlRuntimeEventKind.DmOverrideApplied
            && item.Message.Contains("advance the clock", StringComparison.Ordinal));
        Assert.Contains(result.History, item =>
            item.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && item.Message.Contains("watch-assistant=DmOverride", StringComparison.Ordinal));
    }

    private static DistanceMeasure Miles(double value) => new(value, DistanceUnit.Miles);
}
