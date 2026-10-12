namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Single spatial executor. Travel, watch, course, and encounter operations
/// use qualified periodic cells and realized interfaces exclusively.
/// </summary>
public sealed partial class CrawlRuntimeEngine
{
    private const double Epsilon = 0.0000001d;
}
