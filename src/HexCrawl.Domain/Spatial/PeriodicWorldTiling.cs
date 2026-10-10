using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>An address is meaningful only in the indicated stable tiling identity.</summary>
public readonly record struct WorldCellId(Guid TilingId, PeriodicCellAddress Address);

public readonly record struct WorldBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public void Validate()
    {
        if (!double.IsFinite(MinX) || !double.IsFinite(MinY)
            || !double.IsFinite(MaxX) || !double.IsFinite(MaxY)
            || MinX > MaxX || MinY > MaxY)
            throw new ArgumentException("World bounds must be finite and ordered.");
    }
}

public sealed record WorldCellGeometry(
    WorldCellId Id, IReadOnlyList<WorldPoint> Polygon, WorldPoint Center);

public sealed record WorldCellBoundary(
    WorldCellId From, int InterfaceIndex, int BoundarySideIndex,
    WorldCellId To, int ReciprocalInterfaceIndex, WorldPoint Start, WorldPoint End);

public enum WorldCellLookupStatus { NotFound, Unique, Ambiguous }

public sealed record WorldCellLookup(
    WorldCellLookupStatus Status, IReadOnlyList<WorldCellId> Candidates);

/// <summary>
/// Authoritative, image-independent periodic world geometry. The complete
/// finite topology/metric witness is verified on admission. The translation
/// basis and motif IDs are part of the persisted identity convention; replacing
/// them is a new tiling revision, never an implicit remapping of stored cells.
/// </summary>
public sealed record PeriodicWorldTiling(
    Guid Id,
    PeriodicTopologyWitness Topology,
    PeriodicMetricRealization Realization,
    WorldPoint Origin,
    int Revision = 1,
    DistanceMeasure? PhysicalDistancePerWorldUnit = null)
{
    public void Validate()
    {
        if (Id == Guid.Empty || Revision <= 0
            || !double.IsFinite(Origin.X) || !double.IsFinite(Origin.Y))
            throw new InvalidOperationException("A periodic world requires a stable identity, revision and finite origin.");
        ArgumentNullException.ThrowIfNull(Topology);
        ArgumentNullException.ThrowIfNull(Realization);
        Topology.ValidateAdjacency();
        PeriodicMetricWitnessValidator.Validate(Topology, Realization);
        if (PhysicalDistancePerWorldUnit is { } physical)
        {
            if (!double.IsFinite(physical.Value) || physical.Value <= 0
                || string.IsNullOrWhiteSpace(physical.Unit.Symbol)
                || physical.Unit.MetersPerUnit is { } meters && (!double.IsFinite(meters) || meters <= 0))
                throw new InvalidOperationException("Physical distance conversion must be finite, positive, and identify its physical unit.");
        }
    }

    /// <summary>
    /// Convert the distance between two points in the authoritative world/map
    /// coordinate system into the declared physical unit. A world may omit
    /// this optional physical calibration; in that case no physical distance
    /// can be inferred from the topology or the geometric unit label.
    /// </summary>
    public DistanceMeasure MeasurePhysicalDistance(WorldPoint from, WorldPoint to)
    {
        var calibration = PhysicalDistancePerWorldUnit ?? throw new NotSupportedException(
            "This world has no physical-distance calibration.");
        var distance = from.DistanceTo(to) * calibration.Value;
        if (!double.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(to), "Physical distance exceeds the finite range.");
        return new DistanceMeasure(distance, calibration.Unit);
    }

    private PeriodicMotifCell RequireCell(PeriodicCellAddress address)
    {
        address.Translation.ValidateWireRange();
        return Topology.MotifCells.FirstOrDefault(c => c.Id == address.MotifCellId)
            ?? throw new ArgumentOutOfRangeException(nameof(address), "Cell motif identity does not exist in this tiling.");
    }

    private WorldPoint Transform(TilingWorldPoint point, LatticeDisplacement translation)
    {
        // Perform motif/lattice arithmetic in local coordinates before adding
        // the pose once. Prematurely adding a large world origin destroys the
        // small offsets that distinguish adjacent cells.
        double x = Origin.X + (point.X
            + translation.U * Realization.TranslationU.X + translation.V * Realization.TranslationV.X);
        double y = Origin.Y + (point.Y
            + translation.U * Realization.TranslationU.Y + translation.V * Realization.TranslationV.Y);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(translation), "Translated world point is not finite.");
        return new WorldPoint(x, y);
    }

    public WorldCellGeometry Resolve(PeriodicCellAddress address)
    {
        RequireCell(address);
        var points = Realization.Polygons[address.MotifCellId]
            .Select(p => Transform(p, address.Translation)).ToArray();
        double twiceArea = 0, centroidX = 0, centroidY = 0;
        // Translate the calculation to a local origin to avoid catastrophic
        // cancellation when a small polygon is very far from world zero.
        var zero = points[0];
        foreach (var (a, b) in points.Select((p, i) => (p, points[(i + 1) % points.Length])))
        {
            double ax = a.X - zero.X, ay = a.Y - zero.Y;
            double bx = b.X - zero.X, by = b.Y - zero.Y;
            double cross = ax * by - bx * ay;
            twiceArea += cross;
            centroidX += (ax + bx) * cross;
            centroidY += (ay + by) * cross;
        }
        if (!double.IsFinite(twiceArea) || twiceArea <= 0)
            throw new InvalidOperationException("Cell polygon lost numerical precision at this translation.");
        var center = new WorldPoint(zero.X + centroidX / (3 * twiceArea),
            zero.Y + centroidY / (3 * twiceArea));
        return new WorldCellGeometry(new WorldCellId(Id, address), points, center);
    }

    public IReadOnlyList<WorldCellBoundary> Boundaries(PeriodicCellAddress address)
    {
        var motif = RequireCell(address);
        var polygon = Resolve(address).Polygon;
        return motif.Boundary.Select(edge => new WorldCellBoundary(
            new WorldCellId(Id, address),
            edge.Index, edge.BoundarySideIndex,
            new WorldCellId(Id, new PeriodicCellAddress(
                edge.TargetMotifCellId, address.Translation.Add(edge.TargetTranslation))),
            edge.ReciprocalInterfaceIndex,
            polygon[edge.Index], polygon[(edge.Index + 1) % polygon.Count])).ToArray();
    }

    public IReadOnlyList<WorldCellId> Neighbors(PeriodicCellAddress address) =>
        Boundaries(address).Select(edge => edge.To).Distinct().ToArray();

    public WorldCellBoundary Reciprocal(WorldCellBoundary boundary)
    {
        if (boundary.From.TilingId != Id || boundary.To.TilingId != Id)
            throw new ArgumentException("Boundary belongs to a different tiling.", nameof(boundary));
        var target = Boundaries(boundary.To.Address)[boundary.ReciprocalInterfaceIndex];
        if (target.To != boundary.From || target.ReciprocalInterfaceIndex != boundary.InterfaceIndex)
            throw new InvalidOperationException("Boundary reciprocity is inconsistent.");
        return target;
    }

    // Derive lattice-coordinate bounds from the inverse world-space basis.
    // Motif footprints are included independently, so irregular shapes and
    // motifs spanning the fundamental parallelogram remain discoverable.
    private (double U, double V) ProjectLocal(double x, double y)
    {
        var u = Realization.TranslationU; var v = Realization.TranslationV;
        double determinant = u.X * v.Y - u.Y * v.X;
        return ((x * v.Y - y * v.X) / determinant, (u.X * y - u.Y * x) / determinant);
    }

    private (double U, double V) Project(WorldPoint world) =>
        ProjectLocal(world.X - Origin.X, world.Y - Origin.Y);

    private static long CheckedCoordinate(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > PeriodicTopologyContractVersion.MaxWireTranslation)
            throw new ArgumentOutOfRangeException(nameof(value), "Region lies beyond safe lattice coordinates.");
        return checked((long)value);
    }

    private IReadOnlyList<PeriodicCellAddress> Candidates(WorldBounds region, long limit)
    {
        region.Validate();
        if (limit < 1 || limit > PeriodicTopologyContractVersion.MaxEnumeratedCells)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var projected = new[]
        {
            Project(new WorldPoint(region.MinX, region.MinY)),
            Project(new WorldPoint(region.MinX, region.MaxY)),
            Project(new WorldPoint(region.MaxX, region.MinY)),
            Project(new WorldPoint(region.MaxX, region.MaxY))
        };
        double minU = projected.Min(p => p.U), maxU = projected.Max(p => p.U);
        double minV = projected.Min(p => p.V), maxV = projected.Max(p => p.V);
        var basisU = Realization.TranslationU;
        var basisV = Realization.TranslationV;
        double determinant = basisU.X * basisV.Y - basisU.Y * basisV.X;
        // A world-coordinate point has already been rounded after addition of
        // its pose. Allow for that rounding when inverting the lattice. The
        // uncertainty is expressed in lattice units, so a fixed 1e-9 margin
        // cannot silently omit a neighboring microunit-scale polygon.
        const double machineEpsilon = 2.2204460492503131e-16;
        double worldMagnitude = Math.Max(1,
            new[] { Origin.X, Origin.Y, region.MinX, region.MinY, region.MaxX, region.MaxY }
                .Max(value => Math.Abs(value)));
        double coordinateError = 16 * machineEpsilon * worldMagnitude;
        double marginU = coordinateError * (Math.Abs(basisV.X) + Math.Abs(basisV.Y)) / determinant;
        double marginV = coordinateError * (Math.Abs(basisU.X) + Math.Abs(basisU.Y)) / determinant;
        var addresses = new List<PeriodicCellAddress>();
        foreach (var cell in Topology.MotifCells)
        {
            // Motif footprints are local coordinates. Adding and subtracting
            // the large world origin here would itself lose precision.
            var local = Realization.Polygons[cell.Id]
                .Select(p => ProjectLocal(p.X, p.Y)).ToArray();
            double roundingU = 2e-12 * Math.Max(1, new[] {
                Math.Abs(minU), Math.Abs(maxU), local.Max(p => Math.Abs(p.U)) }.Max());
            double roundingV = 2e-12 * Math.Max(1, new[] {
                Math.Abs(minV), Math.Abs(maxV), local.Max(p => Math.Abs(p.V)) }.Max());
            // Conservative candidate selection; the exact polygon predicate
            // determines final membership. Preserve the bounded enumeration.
            long firstU = CheckedCoordinate(Math.Ceiling(minU - local.Max(p => p.U) - marginU - roundingU));
            long lastU = CheckedCoordinate(Math.Floor(maxU - local.Min(p => p.U) + marginU + roundingU));
            long firstV = CheckedCoordinate(Math.Ceiling(minV - local.Max(p => p.V) - marginV - roundingV));
            long lastV = CheckedCoordinate(Math.Floor(maxV - local.Min(p => p.V) + marginV + roundingV));
            if (lastU < firstU || lastV < firstV) continue;
            long count;
            try { count = checked(checked(lastU - firstU + 1) * checked(lastV - firstV + 1)); }
            catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(region), "Region exceeds bounded enumeration."); }
            if (count > limit - addresses.Count)
                throw new ArgumentOutOfRangeException(nameof(limit), "Region exceeds bounded cell enumeration.");
            for (long u = firstU; u <= lastU; u++)
                for (long v = firstV; v <= lastV; v++)
                    addresses.Add(new(cell.Id, new LatticeDisplacement(u, v)));
        }
        return addresses.OrderBy(a => a.Translation.U)
            .ThenBy(a => a.Translation.V).ThenBy(a => a.MotifCellId, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<WorldCellGeometry> Intersecting(WorldBounds region, long limit = 10000)
    {
        var bounds = new WorldPoint[]
        {
            new(region.MinX, region.MinY), new(region.MaxX, region.MinY),
            new(region.MaxX, region.MaxY), new(region.MinX, region.MaxY)
        };
        return Candidates(region, limit).Select(Resolve)
            .Where(c => FeatureIntersection.PolygonsIntersect(c.Polygon, bounds)).ToArray();
    }

    /// <summary>A boundary point may belong to multiple cells; never guess which one.</summary>
    public WorldCellLookup Containing(WorldPoint point, long limit = 10000)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
        var bounds = new WorldBounds(point.X, point.Y, point.X, point.Y);
        var matches = Candidates(bounds, limit)
            .Where(a => FeatureIntersection.PointInPolygon(point, Resolve(a).Polygon))
            .Select(a => new WorldCellId(Id, a)).ToArray();
        return new WorldCellLookup(matches.Length switch
        {
            0 => WorldCellLookupStatus.NotFound,
            1 => WorldCellLookupStatus.Unique,
            _ => WorldCellLookupStatus.Ambiguous
        }, matches);
    }

    public IReadOnlyList<WorldCellGeometry> Nearby(WorldPoint point, double distance, long limit = 10000)
    {
        if (!double.IsFinite(distance) || distance < 0 || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(distance));
        var bounds = new WorldBounds(point.X - distance, point.Y - distance,
            point.X + distance, point.Y + distance);
        return Candidates(bounds, limit).Select(Resolve)
            .Where(c => FeatureIntersection.DistanceToPolygon(point, c.Polygon) <= distance).ToArray();
    }
}
