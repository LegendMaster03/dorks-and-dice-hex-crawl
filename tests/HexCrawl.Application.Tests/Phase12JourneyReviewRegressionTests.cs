using System.Reflection;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase12JourneyReviewRegressionTests
{
    [Fact]
    public void CompletedWatchDoesNotCreateJourneyBookkeepingWhenNoJourneyCapabilityApplies()
    {
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var id = Guid.NewGuid();
        var before = new NonSpatialSessionState
        {
            Id = id,
            ElapsedTime = TimeSpan.Zero,
            CompletedWatches = 0
        };
        var after = before with
        {
            ElapsedTime = TimeSpan.FromHours(4),
            CompletedWatches = 1
        };
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "simple",
            before,
            new NonSpatialCrawlSessionContext("review"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "owner",
            1,
            now,
            now);
        var state = new ExpeditionJourneyState();

        var observed = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition,
            before,
            after,
            state);

        Assert.Same(state, observed);
        Assert.Empty(observed.ObservedRuntimeOccurrenceIds);
        Assert.Empty(observed.EventOccurrences);
        Assert.Empty(observed.History);
    }

    [Fact]
    public void PostgreSqlPersistenceRejectsIntegerEnumPayloads()
    {
        var field = typeof(PostgresHexCrawlStore).GetField(
            "JsonOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var options = Assert.IsType<JsonSerializerOptions>(field?.GetValue(null));

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JourneyEventStatus>("1", options));
        Assert.Equal("\"Resolved\"", JsonSerializer.Serialize(JourneyEventStatus.Resolved, options));
    }
}
