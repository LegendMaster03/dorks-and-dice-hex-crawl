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
        PeriodicWorldTiling world, PeriodicMotifInvestigation observation,
        MapRegistrationTransform? existingAlignment = null)
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
        // Raw contour uncertainty can help select a correspondence, but it
        // cannot authorize a visibly inaccurate FINAL overlay. Match the
        // independent source-projection ink neighborhood in analysis pixels.
        double placementThreshold = Math.Min(threshold, 3 / analysis.Scale);

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
                            // The three corners select a discrete correspondence,
                            // not the final scale/shear. Contour uncertainty of a
                            // few pixels must not be multiplied across every tile.
                            if (TryFitPeriodicTransform(world, candidate, transform, threshold,
                                    out var periodicTransform)
                                && TryVerify(world, candidate, periodicTransform, placementThreshold, out double error))
                            {
                                periodicTransform = CenterImageOnReference(
                                    world, observation, periodicTransform, existingAlignment);
                                if (!TryVerify(world, candidate, periodicTransform,
                                        placementThreshold, out error))
                                    continue;
                                // Equivalent placements can arise from several
                                // seeds. Keep distinct image orientations/phases.
                                if (!matches.Any(m => Equivalent(m.Transform, periodicTransform)))
                                    matches.Add((periodicTransform, error));
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

    private static MapRegistrationTransform CenterImageOnReference(
        PeriodicWorldTiling world, PeriodicMotifInvestigation observation,
        MapRegistrationTransform alignment, MapRegistrationTransform? existingAlignment)
    {
        if (observation.Source is not { Width: > 0, Height: > 0 }) return alignment;
        var center = new WorldPoint(observation.Source.Width / 2d, observation.Source.Height / 2d);
        WorldPoint target;
        if (existingAlignment is not null)
        {
            // Rechecking a registered map should retain its established
            // geographic position while allowing the measured orientation and
            // scale to be refreshed.
            target = existingAlignment.ToWorld(center);
        }
        else if (world.PlayableEnvelope() is { } bounds)
        {
            target = new WorldPoint((bounds.MinX + bounds.MaxX) / 2,
                (bounds.MinY + bounds.MaxY) / 2);
        }
        else
        {
            // An unbounded map has no finite extent to anchor against. Give a
            // newly detected image a predictable initial location around the
            // world origin while preserving the measured lattice phase.
            target = world.Origin;
        }

        var current = alignment.ToWorld(center);
        var displacement = ClosestLatticeDisplacement(
            target.X - current.X, target.Y - current.Y,
            world.Realization.TranslationU, world.Realization.TranslationV);
        var u = world.Realization.TranslationU;
        var v = world.Realization.TranslationV;
        return MapRegistrationTransform.Affine(
            alignment.M11, alignment.M12, alignment.M21, alignment.M22,
            alignment.M13 + displacement.U * u.X + displacement.V * v.X,
            alignment.M23 + displacement.U * u.Y + displacement.V * v.Y);
    }

    private static LatticeDisplacement ClosestLatticeDisplacement(
        double targetX, double targetY, TilingWorldPoint first, TilingWorldPoint second)
    {
        // Gauss-reduce the two-dimensional lattice first. Searching a small
        // neighborhood in this reduced basis gives the nearest whole-period
        // translation even when the original basis is skewed.
        var u = new WorldPoint(first.X, first.Y);
        var v = new WorldPoint(second.X, second.Y);
        long uU = 1, uV = 0, vU = 0, vV = 1;
        for (var iteration = 0; iteration < 128; iteration++)
        {
            if (Dot(v, v) < Dot(u, u))
            {
                (u, v) = (v, u);
                (uU, vU) = (vU, uU);
                (uV, vV) = (vV, uV);
            }
            var lengthSquared = Dot(u, u);
            if (!double.IsFinite(lengthSquared) || lengthSquared <= 1e-24) return default;
            var multiple = (long)Math.Round(Dot(u, v) / lengthSquared);
            if (multiple == 0) break;
            v = new WorldPoint(v.X - multiple * u.X, v.Y - multiple * u.Y);
            vU = checked(vU - multiple * uU);
            vV = checked(vV - multiple * uV);
        }

        var determinant = u.X * v.Y - u.Y * v.X;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-15) return default;
        var coordinateU = (targetX * v.Y - targetY * v.X) / determinant;
        var coordinateV = (u.X * targetY - u.Y * targetX) / determinant;
        if (!double.IsFinite(coordinateU) || !double.IsFinite(coordinateV)
            || Math.Abs(coordinateU) > 1e12 || Math.Abs(coordinateV) > 1e12)
            return default;

        var baseU = (long)Math.Floor(coordinateU);
        var baseV = (long)Math.Floor(coordinateV);
        var bestDistance = double.PositiveInfinity;
        LatticeDisplacement best = default;
        for (var i = -2; i <= 3; i++)
            for (var j = -2; j <= 3; j++)
            {
                var reducedU = baseU + i;
                var reducedV = baseV + j;
                var dx = reducedU * u.X + reducedV * v.X - targetX;
                var dy = reducedU * u.Y + reducedV * v.Y - targetY;
                var distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = new LatticeDisplacement(
                    checked(reducedU * uU + reducedV * vU),
                    checked(reducedU * uV + reducedV * vV));
            }
        return best;
    }

    private static double Dot(WorldPoint first, WorldPoint second) =>
        first.X * second.X + first.Y * second.Y;

    private static Phase19RasterRegistrationResult Unavailable(string message) =>
        new("unavailable", message, []);

    private static bool TryFitPeriodicTransform(
        PeriodicWorldTiling world, PeriodicMotifCandidate candidate,
        MapRegistrationTransform seed, double threshold,
        out MapRegistrationTransform fitted)
    {
        fitted = seed;
        var a = candidate.TranslationBasisSourcePixels[0];
        var b = candidate.TranslationBasisSourcePixels[1];
        if (!TryLatticeShift(world, TransformVector(seed, a), threshold, seed, out var u)
            || !TryLatticeShift(world, TransformVector(seed, b), threshold, seed, out var v)
            || Math.Abs((double)u.U * v.V - (double)u.V * v.U) != 1)
            return false;
        var wu = world.Realization.TranslationU;
        var wv = world.Realization.TranslationV;
        double ux = u.U * wu.X + u.V * wv.X, uy = u.U * wu.Y + u.V * wv.Y;
        double vx = v.U * wu.X + v.V * wv.X, vy = v.U * wu.Y + v.V * wv.Y;
        double det = a.X * b.Y - a.Y * b.X;
        if (!double.IsFinite(det) || Math.Abs(det) < 1e-12) return false;
        double m11 = (ux * b.Y - vx * a.Y) / det;
        double m12 = (vx * a.X - ux * b.X) / det;
        double m21 = (uy * b.Y - vy * a.Y) / det;
        double m22 = (vy * a.X - uy * b.X) / det;
        // With both periods fixed exactly, fit ONE phase over all corresponding
        // motif vertices. No local deformation or per-tile correction occurs.
        double offsetX = 0, offsetY = 0;
        int count = 0;
        foreach (var cell in candidate.MotifCells)
        {
            var polygon = cell.PolygonSourcePixels;
            var center = new WorldPoint(polygon.Average(p => p.X), polygon.Average(p => p.Y));
            WorldCellLookup lookup;
            try { lookup = world.Containing(seed.ToWorld(center), 256); }
            catch (ArgumentOutOfRangeException) { return false; }
            if (lookup.Status != WorldCellLookupStatus.Unique) return false;
            var target = world.Resolve(lookup.Candidates[0].Address).Polygon;
            if (target.Count != polygon.Count) return false;
            double best = double.PositiveInfinity;
            int bestStart = 0, bestDirection = 1;
            for (int start = 0; start < polygon.Count; start++)
                foreach (int direction in new[] { -1, 1 })
                {
                    double error = 0;
                    for (int i = 0; i < polygon.Count; i++)
                        error += Math.Pow(ErrorSourcePixels(seed,
                            seed.ToWorld(new(polygon[i].X, polygon[i].Y)),
                            target[Mod(start + direction * i, target.Count)]), 2);
                    if (error < best) { best = error; bestStart = start; bestDirection = direction; }
                }
            for (int i = 0; i < polygon.Count; i++)
            {
                var point = polygon[i];
                var to = target[Mod(bestStart + bestDirection * i, target.Count)];
                offsetX += to.X - m11 * point.X - m12 * point.Y;
                offsetY += to.Y - m21 * point.X - m22 * point.Y;
                count++;
            }
        }
        if (count == 0) return false;
        fitted = MapRegistrationTransform.Affine(m11, m12, m21, m22,
            offsetX / count, offsetY / count);
        return true;
    }

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
            existing.Origin, existing.Revision + 1)
        {
            PlayableBounds = existing.PlayableBounds,
            PhysicalTurnCostReference = existing.PhysicalTurnCostReference
        };
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
