using HexCrawl.Application;

namespace HexCrawl.Web.MapAnalysis;

/// <summary>
/// An independent plausibility check for an experimental raster polygon motif.
/// Unlike PeriodicMetricWitnessValidator this permits pixel-contour uncertainty
/// and does not confer authoritative geometric/world-space certification.
/// </summary>
internal static class ObservedRasterMotifGeometryValidator
{
    private static MapAnalysisPoint Difference(MapAnalysisPoint p, MapAnalysisPoint q) =>
        new(p.X - q.X, p.Y - q.Y);
    private static double Cross(MapAnalysisPoint a, MapAnalysisPoint b) =>
        a.X * b.Y - a.Y * b.X;
    private static double Distance(MapAnalysisPoint a, MapAnalysisPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static double Area(IReadOnlyList<MapAnalysisPoint> polygon)
    {
        double twice = 0;
        for (int i = 0; i < polygon.Count; i++)
            twice += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        return twice / 2;
    }

    private static bool OnSegment(MapAnalysisPoint p, MapAnalysisPoint a, MapAnalysisPoint b)
    {
        var ab = Difference(b, a);
        var ap = Difference(p, a);
        double segmentLength = Distance(a, b);
        if (segmentLength <= 0 || Math.Abs(Cross(ab, ap)) > 1e-6 * segmentLength)
            return false;
        double t = (ap.X * ab.X + ap.Y * ab.Y) / (segmentLength * segmentLength);
        return t >= -1e-7 && t <= 1 + 1e-7;
    }

    private static bool SegmentsIntersect(
        MapAnalysisPoint a, MapAnalysisPoint b, MapAnalysisPoint c, MapAnalysisPoint d)
    {
        double abC = Cross(Difference(b, a), Difference(c, a));
        double abD = Cross(Difference(b, a), Difference(d, a));
        double cdA = Cross(Difference(d, c), Difference(a, c));
        double cdB = Cross(Difference(d, c), Difference(b, c));
        const double epsilon = 1e-7;
        return (abC > epsilon && abD < -epsilon || abC < -epsilon && abD > epsilon)
            && (cdA > epsilon && cdB < -epsilon || cdA < -epsilon && cdB > epsilon)
            || OnSegment(a, c, d) || OnSegment(b, c, d)
            || OnSegment(c, a, b) || OnSegment(d, a, b);
    }

    public static void Validate(
        PeriodicMotifCandidate candidate,
        PeriodicMotifEvidence evidence,
        MapAnalysisRaster analysis)
    {
        var basis = candidate.TranslationBasisSourcePixels;
        double signedFundamentalArea = Cross(basis[0], basis[1]);
        double fundamentalArea = Math.Abs(signedFundamentalArea);
        if (!double.IsFinite(fundamentalArea) || fundamentalArea < 4)
            throw new MapAnalysisProtocolException("Surveyor's provisional pixel lattice is degenerate.");

        // The original-image contour has independent stroke width and
        // decimation uncertainty. Do not require mathematically identical
        // sides, but also do not silently accept arbitrary polygon offsets.
        // Bound tolerances in analysis pixels, then map to the original
        // source-pixel coordinates. A fixed source-pixel ceiling would
        // reject legitimate strongly downsampled, but correctly registered,
        // Surveyor observations.
        double contourTolerance = Math.Clamp(
            4 + 2 * evidence.MaximumRigidVertexResidualSourcePixels * analysis.Scale,
            4, 24) / analysis.Scale;
        double totalArea = 0;
        double totalPerimeter = 0;
        foreach (var cell in candidate.MotifCells)
        {
            var polygon = cell.PolygonSourcePixels;
            double signedArea = Area(polygon);
            if (!double.IsFinite(signedArea) || signedArea <= 4)
                throw new MapAnalysisProtocolException(
                    "Surveyor returned a degenerate or reversed provisional pixel polygon.");
            totalArea += signedArea;
            for (int i = 0; i < polygon.Count; i++)
            {
                var next = polygon[(i + 1) % polygon.Count];
                var edgeLength = Distance(polygon[i], next);
                if (!double.IsFinite(edgeLength) || edgeLength < 1)
                    throw new MapAnalysisProtocolException(
                        "Surveyor returned a zero-length provisional polygon side.");
                totalPerimeter += edgeLength;
                for (int j = i + 1; j < polygon.Count; j++)
                {
                    if (j == i + 1 || i == 0 && j == polygon.Count - 1) continue;
                    if (SegmentsIntersect(polygon[i], next, polygon[j],
                        polygon[(j + 1) % polygon.Count]))
                        throw new MapAnalysisProtocolException(
                            "Surveyor returned a self-intersecting provisional pixel polygon.");
                }
            }
        }
        // The white connected components stop at the ink rather than at the
        // unknown mathematical line centers. Account for the total sampled
        // stroke perimeter, but exclude grossly wrong fundamental-domain
        // area claims even when the D-symbol itself is valid.
        if (Math.Abs(totalArea - fundamentalArea) >
            Math.Max(0.35 * fundamentalArea, contourTolerance * totalPerimeter))
            throw new MapAnalysisProtocolException(
                "Surveyor's provisional motif area is inconsistent with its translation lattice.");

        var cells = candidate.MotifCells.ToDictionary(c => c.ProvisionalId, StringComparer.Ordinal);
        foreach (var cell in candidate.MotifCells)
        {
            var polygon = cell.PolygonSourcePixels;
            foreach (var edge in cell.Boundaries)
            {
                var peer = cells[edge.TargetProvisionalId];
                var other = peer.PolygonSourcePixels;
                var offset = new MapAnalysisPoint(
                    edge.TranslationU * basis[0].X + edge.TranslationV * basis[1].X,
                    edge.TranslationU * basis[0].Y + edge.TranslationV * basis[1].Y);
                var opposingFrom = new MapAnalysisPoint(
                    other[(edge.TargetSideIndex + 1) % other.Count].X + offset.X,
                    other[(edge.TargetSideIndex + 1) % other.Count].Y + offset.Y);
                var opposingTo = new MapAnalysisPoint(
                    other[edge.TargetSideIndex].X + offset.X,
                    other[edge.TargetSideIndex].Y + offset.Y);
                if (Distance(polygon[edge.SideIndex], opposingFrom) > contourTolerance
                    || Distance(polygon[(edge.SideIndex + 1) % polygon.Count], opposingTo) > contourTolerance)
                    throw new MapAnalysisProtocolException(
                        "Surveyor's provisional pixel boundaries are inconsistent with the translation motif.");
            }
        }
    }
}
