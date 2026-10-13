// One-time schema-11 authored-world conversion support.
using System;
using System.Collections.Generic;
using System.Linq;
// The archived axial format is never accepted by the runtime world model,
// endpoints, session context, or ordinary snapshot serialization.
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Infrastructure.Persistence;

internal readonly record struct ArchivedAxialCoordinate(int Q, int R)
{
    public override string ToString() => $"{Q},{R}";
}

internal enum ArchivedOrientation
{
    PointyTop,
    FlatTop
}

internal enum ArchivedCoordinateConvention
{
    AxialQr
}

internal sealed record ArchivedGridDefinition
{
    public required Guid Id { get; init; }
    public ArchivedOrientation Orientation { get; init; } = ArchivedOrientation.PointyTop;
    public ArchivedCoordinateConvention CoordinateConvention { get; init; } = ArchivedCoordinateConvention.AxialQr;
    public WorldPoint Origin { get; init; } = new(0, 0);
    public double RotationDegrees { get; init; }
    public double HexRadiusWorldUnits { get; init; } = 1;
    public required DistanceMeasure NeighborCenterDistance { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Grid identity must be stable and non-empty.");
        }

        if (HexRadiusWorldUnits <= 0 || double.IsNaN(HexRadiusWorldUnits) || double.IsInfinity(HexRadiusWorldUnits))
        {
            throw new InvalidOperationException("Hex radius must be finite and positive.");
        }

        if (CoordinateConvention != ArchivedCoordinateConvention.AxialQr)
        {
            throw new NotSupportedException($"Coordinate convention {CoordinateConvention} is not implemented.");
        }
    }
}

internal static class ArchivedHexGeometry
{
    private const double Sqrt3 = 1.7320508075688772935;

    public static WorldPoint HexToWorld(ArchivedGridDefinition grid, ArchivedAxialCoordinate hex)
    {
        grid.Validate();
        var radius = grid.HexRadiusWorldUnits;

        var local = grid.Orientation switch
        {
            ArchivedOrientation.PointyTop => new WorldPoint(
                radius * Sqrt3 * (hex.Q + (hex.R / 2d)),
                radius * 1.5d * hex.R),
            ArchivedOrientation.FlatTop => new WorldPoint(
                radius * 1.5d * hex.Q,
                radius * Sqrt3 * (hex.R + (hex.Q / 2d))),
            _ => throw new ArgumentOutOfRangeException(nameof(grid.Orientation))
        };

        return Rotate(local, grid.RotationDegrees) + grid.Origin;
    }

    public static IReadOnlyList<WorldPoint> Corners(ArchivedGridDefinition grid, ArchivedAxialCoordinate hex)
    {
        var center = HexToWorld(grid, hex);
        var startAngle = grid.Orientation == ArchivedOrientation.PointyTop ? -30d : 0d;
        var corners = new WorldPoint[6];

        for (var i = 0; i < 6; i++)
        {
            var angle = DegreesToRadians(startAngle + (60d * i) + grid.RotationDegrees);
            corners[i] = new WorldPoint(
                center.X + (grid.HexRadiusWorldUnits * Math.Cos(angle)),
                center.Y + (grid.HexRadiusWorldUnits * Math.Sin(angle)));
        }

        return corners;
    }

    private static WorldPoint Rotate(WorldPoint point, double degrees)
    {
        if (degrees == 0)
        {
            return point;
        }

        var radians = DegreesToRadians(degrees);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new WorldPoint((point.X * cos) - (point.Y * sin), (point.X * sin) + (point.Y * cos));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}

/// <summary>
/// Reconstructs archived grid geometry only inside the transactional
/// authored-world migration. No runtime path selects or executes this format.
/// </summary>
internal static class ArchivedGridCutoverGeometry
{
    public const string MotifId = "hex";
    public const string HexQuotient = "<1:1,1,1:6,3>";

    private static readonly LatticeDisplacement[] SideOffsets =
    [
        new(1, 0), new(0, 1), new(-1, 1),
        new(-1, 0), new(0, -1), new(1, -1)
    ];

    public static PeriodicCellAddress ToAddress(ArchivedAxialCoordinate hex) =>
        new(MotifId, new LatticeDisplacement(hex.Q, hex.R));

    /// <summary>Verify that a separately stored archived metric agrees with its archived grid definition.</summary>
    public static bool Matches(PeriodicWorldTiling authoritative, ArchivedGridDefinition grid)
    {
        if (authoritative.Id != grid.Id || authoritative.Revision < 1)
            return false;
        var projection = Create(grid);
        if (authoritative.Topology.QuotientDsSymbol != projection.Topology.QuotientDsSymbol
            || authoritative.Topology.TranslationDsSymbol != projection.Topology.TranslationDsSymbol
            || authoritative.Topology.MotifCells.Count != projection.Topology.MotifCells.Count
            || authoritative.Realization.Units != projection.Realization.Units
            || authoritative.PhysicalDistancePerWorldUnit != projection.PhysicalDistancePerWorldUnit)
            return false;
        var source = projection.Topology.MotifCells[0];
        var actual = authoritative.Topology.MotifCells[0];
        if (source.Id != actual.Id || source.Boundary.Count != actual.Boundary.Count
            || source.Boundary.Where((edge, index) => edge != actual.Boundary[index]).Any())
            return false;

        // Compare pose, lattice basis and local corners independently.
        // A relative tolerance based on absolute world position would allow
        // substantial geometry drift at large map origins.
        double tolerance = Math.Max(1e-12, grid.HexRadiusWorldUnits * 1e-10);
        static bool Close(TilingWorldPoint a, TilingWorldPoint b, double epsilon) =>
            Math.Abs(a.X - b.X) <= epsilon && Math.Abs(a.Y - b.Y) <= epsilon;
        if (authoritative.Origin != projection.Origin
            || !Close(authoritative.Realization.TranslationU, projection.Realization.TranslationU, tolerance)
            || !Close(authoritative.Realization.TranslationV, projection.Realization.TranslationV, tolerance))
            return false;
        if (!authoritative.Realization.Polygons.TryGetValue(MotifId, out var polygon)
            || !projection.Realization.Polygons.TryGetValue(MotifId, out var expected)
            || polygon.Count != expected.Count)
            return false;
        for (int i = 0; i < polygon.Count; i++)
            if (!Close(polygon[i], expected[i], tolerance))
                return false;
        return true;
    }

    public static PeriodicWorldTiling Create(ArchivedGridDefinition grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.Validate();
        if (!double.IsFinite(grid.RotationDegrees)
            || !double.IsFinite(grid.Origin.X) || !double.IsFinite(grid.Origin.Y))
            throw new InvalidOperationException("Legacy hex pose is not finite.");

        // The chamber incidence below is reconstructed from one six-sided
        // oriented face and its opposite boundary pairings. It is not a
        // separate hex geometry validator: Phase 16 validates both witnesses.
        const string translationFaceIncidence =
            "<12:2 4 6 8 10 12,12 3 5 7 9 11,8 7 10 9 12 11:6,3 3>";
        var inspected = DelaneyDressTopology.Inspect(translationFaceIncidence);
        if (inspected.Status != DelaneyDressStatus.Euclidean)
            throw new InvalidOperationException("Legacy hex translation cover is invalid.");

        var interfaces = Enumerable.Range(0, 6).Select(side => new PeriodicEdgeInterface(
            side, side, MotifId, SideOffsets[side], (side + 3) % 6)).ToArray();
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current,
            HexQuotient, inspected.Symbol!.Canonical,
            [new PeriodicMotifCell(MotifId, interfaces)],
            "legacy-axial-qr:phase-17");
        // Build local metric geometry before adding the world pose. Calculating
        // global vertices and subtracting a large origin loses significant
        // digits and can invalidate otherwise legitimate rotated hex grids.
        // The original origin remains the authoritative separate world pose.
        var localGrid = grid with { Origin = new WorldPoint(0, 0) };
        var translationU = ArchivedHexGeometry.HexToWorld(localGrid, new ArchivedAxialCoordinate(1, 0));
        var translationV = ArchivedHexGeometry.HexToWorld(localGrid, new ArchivedAxialCoordinate(0, 1));
        var corners = ArchivedHexGeometry.Corners(localGrid, new ArchivedAxialCoordinate(0, 0))
            .Select(p => new TilingWorldPoint(p.X, p.Y)).ToArray();
        // Coordinates must remain in the original world/map space. The
        // neighbor-center distance is an independent physical calibration;
        // labeling these unscaled polygon coordinates as miles or kilometers
        // would silently corrupt Phase 18 movement distances.
        var physicalPerWorldUnit = new DistanceMeasure(
            grid.NeighborCenterDistance.Value / (Math.Sqrt(3) * grid.HexRadiusWorldUnits),
            grid.NeighborCenterDistance.Unit);
        var metric = new PeriodicMetricRealization(
            "world-unit",
            new TilingWorldPoint(translationU.X, translationU.Y),
            new TilingWorldPoint(translationV.X, translationV.Y),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>(StringComparer.Ordinal)
            { [MotifId] = corners },
            []);
        var world = new PeriodicWorldTiling(grid.Id, topology, metric, grid.Origin,
            PhysicalDistancePerWorldUnit: physicalPerWorldUnit);
        world.Validate();
        return world;
    }
}