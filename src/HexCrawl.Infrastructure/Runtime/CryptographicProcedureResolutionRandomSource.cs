using System.Security.Cryptography;
using HexCrawl.Application;

namespace HexCrawl.Infrastructure.Runtime;

public sealed class CryptographicProcedureResolutionRandomSource : IProcedureResolutionRandomSource
{
    public int NextInt32(int minInclusive, int maxExclusive) =>
        RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
}
