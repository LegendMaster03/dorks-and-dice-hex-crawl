using HexCrawl.Application;

namespace HexCrawl.Infrastructure.Runtime;

public sealed class PseudoRandomProcedureResolutionSource : IProcedureResolutionRandomSource
{
    private readonly Random _random = new();
    private readonly object _sync = new();

    public int NextInt32(int minInclusive, int maxExclusive)
    {
        lock (_sync)
        {
            return _random.Next(minInclusive, maxExclusive);
        }
    }
}
