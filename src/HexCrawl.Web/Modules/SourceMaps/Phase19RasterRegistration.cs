using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record Phase19RasterRegistrationOption(
    int Index, MapRegistrationTransform Alignment, double MaximumResidualSourcePixels);

public sealed record Phase19RasterRegistrationResult(
    string Status, string Explanation, IReadOnlyList<Phase19RasterRegistrationOption> Candidates);

/// <summary>
/// Checks experimental pixel observations against independently accepted world
/// polygons AND reciprocal atomic interfaces. No image detector, world mutation
/// or procedure authority is involved. A matching D-symbol is not enough:
/// metric fit, motif phase and primitive lattice correspondence must all agree.
/// </summary>
public static class Phase19RasterRegistration
{
    private sealed record MappedCell(
        WorldCellId Id, int StartVertex, int Direction, double MaximumResidual);

    public static Phase19RasterRegistrationResult Assess(
        PeriodicWorldTiling world, PeriodicMotifInvestigation observation)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation is not
            { Status: "consistent-candidate", Authoritative: false,
              Candidate: { } candidate,
              Evidence.MetricRegistration:
                  { Status: "registered", SourceProjection.Status: "supported" } metric,
              Analysis: { } analysis })
            return Unavailable("The image did not establish a registered repeating-cell motif.");
        if (candidate.MotifCells.Count == 0 || candidate.MotifCells.Count > 24
            || candidate.MotifCells.Count != world.Topology.MotifCells.Count)
            return Unavailable("This observed motif needs a different translation cover. Use manual placement until its correspondence can be verified.");

        var equivalence = DelaneyDressTopology.Compare(
            world.Topology.QuotientDsSymbol, candidate.DsSymbol,
            world.Topology.TranslationDsSymbol);
        if (equivalence.Status is not (
            TilingComparisonStatus.ExactIdentity or
            TilingComparisonStatus.ChamberIsomorphic or
            TilingComparisonStatus.ProvenEquivalent))
            return Unavailable("The observed cell structure is not proven equivalent to the saved world.");

        // All discrepancies are measured in ORIGINAL raster pixels, not world
        // units. Downsampling can increase the justified image-pixel envelope
        // without creating additional structural authority.
        double threshold = Math.Clamp(
            2 + 2 * metric.MaximumContourResidualSourcePixels, 3, 12 / analysis.Scale);
        if (!double.IsFinite(threshold) || threshold < 0)
            return Unavailable("The raster does not supply a usable accuracy envelope.");

        var observed = candidate.MotifCells
            .OrderBy(c => c.PolygonSourcePixels.Count)
            .ThenBy(c => c.ProvisionalId, StringComparer.Ordinal).ToArray();
        var anchor = observed[0];
        var matches = new List<(MapRegistrationTransform Transform, double Error)>();
        int attempts = 0;
        foreach (var target in world.Topology.MotifCells)
        {
            var targetPolygon = world.Resolve(new PeriodicCellAddress(
                target.Id, new LatticeDisplacement(0, 0))).Polygon;
            int n = anchor.PolygonSourcePixels.Count;
            if (targetPolygon.Count != n) continue;
            for (int start = 0; start < n; start++)
                foreach (int direction in new[] { -1, 1 })
                {
                    if (++attempts > 512)
                        return Unavailable("The motif admits too many candidate placements for bounded automatic validation. Use manual placement.");
                    var indices = Enumerable.Range(0, n)
                        .Select(i => Mod(start + direction * i, n)).ToArray();
                    // A subdivided polygon may have three consecutive
                    // collinear vertices: choose a noncollinear triple.
                    for (int second = 1; second < n - 1; second++)
                    {
                        bool accepted = false;
                        for (int third = second + 1; third < n; third++)
                        {
                            MapRegistrationTransform transform;
                            try
                            {
                                transform = AffineRegistrationSolver.Solve(
                                [
                                    new(new(anchor.PolygonSourcePixels[0].X,
                                            anchor.PolygonSourcePixels[0].Y), targetPolygon[indices[0]]),
                                    new(new(anchor.PolygonSourcePixels[second].X,
                                            anchor.PolygonSourcePixels[second].Y), targetPolygon[indices[second]]),
                                    new(new(anchor.PolygonSourcePixels[third].X,
                                            anchor.PolygonSourcePixels[third].Y), targetPolygon[indices[third]])
                                ]);
                            }
                            catch (ArgumentException) { continue; }
                            if (TryVerify(world, candidate, transform, threshold, out double error))
                            {
                                // Equivalent placements can arise from several
                                // seeds. Keep distinct image orientations/phases.
                                if (!matches.Any(m => Equivalent(m.Transform, transform)))
                                    matches.Add((transform, error));
                                accepted = true;
                            }
                            break;
                        }
                        if (accepted) break;
                    }
                }
        }
        if (matches.Count == 0)
            return Unavailable("The measured tile outlines, lattice phase or neighbor interfaces do not agree with the saved world. Manual placement remains available.");

        var candidates = matches.OrderBy(x => x.Error)
            .ThenBy(x => x.Transform.M11).ThenBy(x => x.Transform.M12)
            .ThenBy(x => x.Transform.M13).ThenBy(x => x.Transform.M21)
            .ThenBy(x => x.Transform.M22).ThenBy(x => x.Transform.M23)
            .Take(16).Select((x, index) => new Phase19RasterRegistrationOption(
                index, x.Transform, x.Error)).ToArray();
        return new("candidates",
            matches.Count == 1
                ? "One independently cross-checked image placement is available. Preview the overlay before accepting it."
                : $"{matches.Count} geometrically valid image placements were found. Choose one by visually checking the preview; the motif alone does not determine an absolute map orientation.",
            candidates);
    }

    private static Phase19RasterRegistrationResult Unavailable(string message) =>
        new("unavailable", message, []);

    private static bool TryVerify(
        PeriodicWorldTiling world, PeriodicMotifCandidate candidate,
        MapRegistrationTransform transform, double threshold, out double maximum)
    {
        maximum = 0;
        var sourceBasis = candidate.TranslationBasisSourcePixels;
        var u = TransformVector(transform, sourceBasis[0]);
        var v = TransformVector(transform, sourceBasis[1]);
        if (!TryLatticeShift(world, u, threshold, transform, out var latticeU)
            || !TryLatticeShift(world, v, threshold, transform, out var latticeV))
            return false;
        // The two coordinate frames may choose different primitive lattice
        // bases, but their determinant must be +/-1. Nonprimitive supercells
        // cannot silently be treated as one-to-one motif identities.
        if (Math.Abs((double)latticeU.U * latticeV.V - (double)latticeU.V * latticeV.U) != 1)
            return false;

        var mapping = new Dictionary<string, MappedCell>(StringComparer.Ordinal);
        var mappedIds = new HashSet<WorldCellId>();
        foreach (var observed in candidate.MotifCells)
        {
            var polygon = observed.PolygonSourcePixels;
            var center = new WorldPoint(
                polygon.Average(p => p.X), polygon.Average(p => p.Y));
            WorldCellLookup lookup;
            try { lookup = world.Containing(transform.ToWorld(center), 256); }
            catch (ArgumentOutOfRangeException) { return false; }
            if (lookup.Status != WorldCellLookupStatus.Unique) return false;
            var cellId = lookup.Candidates[0];
            if (!mappedIds.Add(cellId)) return false;
            var actual = world.Resolve(cellId.Address).Polygon;
            if (actual.Count != polygon.Count) return false;
            int bestStart = -1, bestDirection = 0;
            double bestError = double.PositiveInfinity;
            for (int start = 0; start < polygon.Count; start++)
                foreach (int direction in new[] { -1, 1 })
                {
                    double worst = 0;
                    for (int k = 0; k < polygon.Count; k++)
                    {
                        var image = new WorldPoint(polygon[k].X, polygon[k].Y);
                        double error = ErrorSourcePixels(transform,
                            transform.ToWorld(image), actual[Mod(start + direction * k, actual.Count)]);
                        worst = Math.Max(worst, error);
                    }
                    if (worst < bestError)
                    {
                        bestStart = start;
                        bestDirection = direction;
                        bestError = worst;
                    }
                }
            if (bestError > threshold || !double.IsFinite(bestError)) return false;
            maximum = Math.Max(maximum, bestError);
            mapping.Add(observed.ProvisionalId,
                new(cellId, bestStart, bestDirection, bestError));
        }

        // The observed edge graph must map into actual authoritative,
        // reciprocal world interfaces, including each translated neighbor.
        foreach (var observed in candidate.MotifCells)
        {
            var match = mapping[observed.ProvisionalId];
            var worldEdges = world.Boundaries(match.Id.Address);
            int sides = observed.PolygonSourcePixels.Count;
            foreach (var edge in observed.Boundaries)
            {
                if (!mapping.TryGetValue(edge.TargetProvisionalId, out var peer)
                    || edge.SideIndex < 0 || edge.SideIndex >= sides) return false;
                var nextIndex = Mod(match.StartVertex + match.Direction * (edge.SideIndex + 1), sides);
                var worldIndex = match.Direction == 1
                    ? Mod(match.StartVertex + edge.SideIndex, sides) : nextIndex;
                if (worldIndex >= worldEdges.Count) return false;
                var authoritative = worldEdges[worldIndex];
                var shift = new LatticeDisplacement(
                    edge.TranslationU * latticeU.U + edge.TranslationV * latticeV.U,
                    edge.TranslationU * latticeU.V + edge.TranslationV * latticeV.V);
                PeriodicCellAddress expectedTarget;
                try { expectedTarget = peer.Id.Address.Translate(shift); }
                catch (OverflowException) { return false; }
                if (authoritative.To.Address != expectedTarget
                    || authoritative.To.TilingId != peer.Id.TilingId) return false;
                var otherSize = candidate.MotifCells.Single(c =>
                    c.ProvisionalId == edge.TargetProvisionalId).PolygonSourcePixels.Count;
                var peerWorldIndex = peer.Direction == 1
                    ? Mod(peer.StartVertex + edge.TargetSideIndex, otherSize)
                    : Mod(peer.StartVertex + peer.Direction * (edge.TargetSideIndex + 1), otherSize);
                if (authoritative.ReciprocalInterfaceIndex != peerWorldIndex) return false;
            }
        }
        return true;
    }

    private static int Mod(int x, int n) => (x % n + n) % n;

    private static WorldPoint TransformVector(
        MapRegistrationTransform transform, MapAnalysisPoint vector) =>
        new(transform.M11 * vector.X + transform.M12 * vector.Y,
            transform.M21 * vector.X + transform.M22 * vector.Y);

    private static bool TryLatticeShift(
        PeriodicWorldTiling world, WorldPoint v, double pixelTolerance,
        MapRegistrationTransform transform, out LatticeDisplacement shift)
    {
        shift = default;
        var u = world.Realization.TranslationU;
        var w = world.Realization.TranslationV;
        var determinant = u.X * w.Y - u.Y * w.X;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-15) return false;
        double x = (v.X * w.Y - v.Y * w.X) / determinant;
        double y = (u.X * v.Y - u.Y * v.X) / determinant;
        if (!double.IsFinite(x) || !double.IsFinite(y)
            || Math.Abs(x) > 10000 || Math.Abs(y) > 10000) return false;
        long roundedX = (long)Math.Round(x), roundedY = (long)Math.Round(y);
        var closest = new WorldPoint(roundedX * u.X + roundedY * w.X,
            roundedX * u.Y + roundedY * w.Y);
        if (ErrorSourcePixels(transform, v, closest) > pixelTolerance) return false;
        shift = new(roundedX, roundedY);
        return true;
    }

    private static double ErrorSourcePixels(
        MapRegistrationTransform t, WorldPoint predicted, WorldPoint actual)
    {
        double determinant = t.M11 * t.M22 - t.M12 * t.M21;
        var dx = actual.X - predicted.X;
        var dy = actual.Y - predicted.Y;
        return Math.Sqrt(
            Math.Pow((t.M22 * dx - t.M12 * dy) / determinant, 2)
            + Math.Pow((t.M11 * dy - t.M21 * dx) / determinant, 2));
    }

    /// <summary>
    /// Constructs fresh, independently checked geometry from a qualified
    /// observed structure. The raster does not become the world's metric
    /// witness: the geometric constructor and validator supply authority.
    /// Registration against the raster must pass Assess separately.
    /// </summary>
    public static PeriodicWorldTiling? ConstructReplacement(
        PeriodicWorldTiling existing, PeriodicMotifInvestigation observed)
    {
        if (observed is not
            { Status: "consistent-candidate", Authoritative: false,
              Candidate: { } candidate,
              Evidence.MetricRegistration:
                  { Status: "registered", SourceProjection.Status: "supported" } }
            || existing.PhysicalDistancePerWorldUnit is not null
            || existing.Revision == int.MaxValue)
            return null;
        var oldBasis = existing.Realization.TranslationU;
        double period = Math.Sqrt(oldBasis.X * oldBasis.X + oldBasis.Y * oldBasis.Y);
        if (!double.IsFinite(period) || period <= 0) return null;
        var result = DelaneyDressHarmonicMetricRealization.Construct(
            candidate.DsSymbol, period, "world-unit");
        if (result.Status != "realized"
            || result.Topology is null || result.Realization is null)
            return null;
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), result.Topology, result.Realization,
            existing.Origin, existing.Revision + 1);
        try { tiling.Validate(); }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException)
        {
            return null;
        }
        return tiling;
    }

    public static bool Equivalent(MapRegistrationTransform a, MapRegistrationTransform b) =>
        new[] { (a.M11, b.M11), (a.M12, b.M12), (a.M13, b.M13),
            (a.M21, b.M21), (a.M22, b.M22), (a.M23, b.M23) }
        .All(pair => Math.Abs(pair.Item1 - pair.Item2)
            <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(pair.Item1), Math.Abs(pair.Item2))));
}
