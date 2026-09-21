using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

/// <summary>
/// Compatibility facade for Hex Crawl application operations. Feature-owned members are
/// physically separated into partial files so callers can migrate independently over time.
/// </summary>
public sealed partial class HexCrawlService
{
    private readonly IHexCrawlStore _store;

    public HexCrawlService(IHexCrawlStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    private async Task<StoredOverworld> SaveWorldAsync(
        StoredOverworld world,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await _store.SaveOverworldAsync(world, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The overworld was changed by another request. Reload it before saving again."),
            _ => throw new HexCrawlNotFoundException("Overworld was not found.")
        };
    }

    private async Task<StoredExpedition> SaveExpeditionAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await _store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The expedition was changed by another request. Reload it before advancing again."),
            _ => throw new HexCrawlNotFoundException("Expedition was not found.")
        };
    }

    private static string RequireUser(string value) => RequiredText(value, "User identity");

    private static string RequiredText(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"{label} is required.");

    private static void RequireVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new HexCrawlConcurrencyException("The resource version is stale. Reload it before saving again.");
        }
    }

    private static void ValidatePoint(WorldPoint point, string label)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentException($"{label} must contain finite coordinates.");
        }
    }

    private static void ValidatePoints(IReadOnlyList<WorldPoint>? points, string label)
    {
        if (points is null)
        {
            return;
        }
        foreach (var point in points)
        {
            ValidatePoint(point, label);
        }
    }
}

public sealed class HexCrawlNotFoundException(string message) : Exception(message);
public sealed class HexCrawlConcurrencyException(string message) : Exception(message);
public sealed class HexCrawlConflictException(string message) : Exception(message);
