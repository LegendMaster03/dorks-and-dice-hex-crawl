using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>Wire contract v1, additive and not yet authoritative for stored worlds.</summary>
public static class PeriodicTopologyContractVersion
{
    public const int Current = 1;
    public const string Capability = "tiling.topology.periodic-translation-cover";
    public const long MaxWireTranslation = 9007199254740991L;
}

/// <summary>Translation coordinates are integer-valued and independent of pixel/world scale.</summary>
public readonly record struct LatticeDisplacement(long U, long V)
{
    public void ValidateWireRange()
    {
        if (U < -PeriodicTopologyContractVersion.MaxWireTranslation || U > PeriodicTopologyContractVersion.MaxWireTranslation
            || V < -PeriodicTopologyContractVersion.MaxWireTranslation || V > PeriodicTopologyContractVersion.MaxWireTranslation)
            throw new ArgumentOutOfRangeException(nameof(U), "Translation coordinate exceeds the exact JSON integer range.");
    }
    public LatticeDisplacement Opposite() => new(checked(-U), checked(-V));
    public LatticeDisplacement Add(LatticeDisplacement other)
    {
        var sum = new LatticeDisplacement(checked(U + other.U), checked(V + other.V));
        sum.ValidateWireRange();
        return sum;
    }
}
public readonly record struct PeriodicCellAddress(string MotifCellId, LatticeDisplacement Translation)
{
    public PeriodicCellAddress Translate(LatticeDisplacement shift) => new(MotifCellId, Translation.Add(shift));
    public override string ToString() => $"{MotifCellId}@{Translation.U},{Translation.V}";
}
public sealed record PeriodicEdgeInterface(int Index, int BoundarySideIndex, string TargetMotifCellId,
    LatticeDisplacement TargetTranslation, int ReciprocalInterfaceIndex);
public sealed record PeriodicMotifCell(string Id, IReadOnlyList<PeriodicEdgeInterface> Boundary);

/// <summary>
/// The translation motif is a finite covering *witness*. It is not inferred from a
/// canonical D-symbol by this DTO. Phase 17 must reject any unverified witness.
/// </summary>
public sealed record PeriodicTopologyWitness(
    int ContractVersion,
    string QuotientDsSymbol,
    string TranslationDsSymbol,
    IReadOnlyList<PeriodicMotifCell> MotifCells,
    string Provenance)
{
    /// <summary>Checks address uniqueness and reciprocal periodic adjacency only;
    /// does not certify that a supplied chamber cover was constructed from these edges.</summary>
    public void ValidateAdjacency()
    {
        if (ContractVersion != PeriodicTopologyContractVersion.Current)
            throw new InvalidOperationException("Unsupported periodic topology contract version.");
        if (string.IsNullOrWhiteSpace(Provenance))
            throw new InvalidOperationException("Topology witness provenance is required.");
        var quotient = DelaneyDressTopology.Inspect(QuotientDsSymbol, 2048);
        var cover = DelaneyDressTopology.Inspect(TranslationDsSymbol, 2048);
        if (quotient.Status != DelaneyDressStatus.Euclidean || cover.Status != DelaneyDressStatus.Euclidean
            || DelaneyDressTopology.ProjectChambers(cover.Symbol!, quotient.Symbol!) is null)
            throw new InvalidOperationException("Translation chamber graph does not cover the quotient D-symbol.");
        if (MotifCells.Count == 0 || MotifCells.Count > 256 || MotifCells.Any(c => string.IsNullOrWhiteSpace(c.Id)))
            throw new InvalidOperationException("Invalid finite motif cell count or identity.");
        var byId = MotifCells.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var cell in MotifCells)
        {
            if (cell.Boundary.Count < 3 || cell.Boundary.Count > 256)
                throw new InvalidOperationException("Invalid atomic boundary count.");
            for (int i = 0; i < cell.Boundary.Count; i++)
            {
                var edge = cell.Boundary[i];
                if (edge.Index != i || edge.BoundarySideIndex < 0 || !byId.TryGetValue(edge.TargetMotifCellId, out var next)
                    || edge.ReciprocalInterfaceIndex < 0 || edge.ReciprocalInterfaceIndex >= next.Boundary.Count)
                    throw new InvalidOperationException("Invalid periodic boundary target.");
                edge.TargetTranslation.ValidateWireRange();
                var opposite = next.Boundary[edge.ReciprocalInterfaceIndex];
                if (opposite.TargetMotifCellId != cell.Id || opposite.ReciprocalInterfaceIndex != i
                    || opposite.TargetTranslation != edge.TargetTranslation.Opposite())
                    throw new InvalidOperationException("Nonreciprocal periodic interface.");
            }
        }
    }

    public IReadOnlyList<PeriodicCellAddress> Enumerate(
        long minU, long maxU, long minV, long maxV, long limit = 10000)
    {
        new LatticeDisplacement(minU, minV).ValidateWireRange();
        new LatticeDisplacement(maxU, maxV).ValidateWireRange();
        if (minU > maxU || minV > maxV || limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit));
        // Subtract before adding to prevent long overflow from silently producing a short interval.
        var count = checked(checked(maxU - minU + 1) * checked(maxV - minV + 1) * MotifCells.Count);
        if (count > limit) throw new ArgumentOutOfRangeException(nameof(limit), "Region exceeds cell limit.");
        var addresses = new List<PeriodicCellAddress>((int)count);
        long countU = checked(maxU - minU + 1);
        long countV = checked(maxV - minV + 1);
        for (long du = 0; du < countU; du++)
            for (long dv = 0; dv < countV; dv++)
                foreach (var cell in MotifCells)
                    addresses.Add(new(cell.Id, new(checked(minU + du), checked(minV + dv))));
        return addresses;
    }
}

/// <summary>Chosen embedding and metric units, independent of the D-symbol.</summary>
public readonly record struct TilingWorldPoint(double X, double Y);
public sealed record PeriodicMetricRealization(
    string Units,
    TilingWorldPoint TranslationU,
    TilingWorldPoint TranslationV,
    IReadOnlyDictionary<string, IReadOnlyList<TilingWorldPoint>> Polygons,
    IReadOnlyList<TilingMetricConstraint> Constraints);

/// <summary>Pixel evidence is not an authoritative world topology or embedding.</summary>
public sealed record PeriodicRasterRegistration(
    string SourceAssetId,
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<double> WorldToPixelAffine,
    double? RmsResidualPixels,
    double? ObservationConfidence);

/// <summary>Wire status is a documented kebab-case token (not an enum ordinal).</summary>
public sealed record PeriodicCapabilityResult(string Status, string? Reason)
{
    public static readonly IReadOnlySet<string> SupportedStatuses = new HashSet<string>(StringComparer.Ordinal)
    { "valid", "invalid", "unresolved-geometry", "unsupported-limit", "inconclusive" };
}
public sealed record PeriodicTopologyWireEnvelope(
    int ContractVersion,
    IReadOnlyList<string> Capabilities,
    PeriodicTopologyWitness? Topology,
    PeriodicMetricRealization? Realization,
    PeriodicRasterRegistration? Registration,
    PeriodicCapabilityResult Outcome);
