using System;
using System.Collections.Generic;
using System.Linq;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

/// <summary>A data-only vocabulary entry; never an allowlist or detector implementation.</summary>
public sealed record TilingGeometryQualifier(
    int Sides,
    bool EqualSideLengths = false,
    double? InteriorAngleDegrees = null,
    double Tolerance = 0.0001);
public sealed record TilingNameEntry(
    string CanonicalDsSymbol,
    string PreferredName,
    string? MathematicalName,
    IReadOnlyList<string> Aliases,
    string Description,
    string Provenance,
    string? LocalizationKey = null,
    TilingGeometryQualifier? Geometry = null,
    bool ShowInRulesetSelector = true,
    IReadOnlyList<string>? CategoryPath = null,
    string? PresetId = null,
    PeriodicTopologyWitness? RepresentativeTopology = null,
    PeriodicMetricRealization? RepresentativeRealization = null);
public sealed record TilingDisplayDescription(string Label, bool IsNamed, string CanonicalDsSymbol);

public enum TilingNotationSurface { Normal, JsonEditor, MissingSupportedNameError }
public static class TilingNotationPresentationPolicy
{
    /// <summary>Notation in normal workflows is prohibited, including unknown-pattern success messages.</summary>
    public static string? Display(
        string symbol, TilingNotationSurface surface, bool supported, bool hasReliableName) =>
        surface == TilingNotationSurface.JsonEditor
            || (surface == TilingNotationSurface.MissingSupportedNameError && supported && !hasReliableName)
                ? symbol : null;
}

/// <summary>Presentation-only name lookup. The structure remains usable if no name matches.</summary>
public sealed class TilingNomenclatureCatalog
{
    private readonly IReadOnlyDictionary<string, List<TilingNameEntry>> entries;

    public TilingNomenclatureCatalog(IEnumerable<TilingNameEntry> source)
    {
        var grouped = new Dictionary<string, List<TilingNameEntry>>(StringComparer.Ordinal);
        var presetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in source)
        {
            var parsed = DelaneyDressTopology.Inspect(entry.CanonicalDsSymbol);
            if (parsed.Status != DelaneyDressStatus.Euclidean
                || parsed.Symbol!.Canonical != entry.CanonicalDsSymbol
                || string.IsNullOrWhiteSpace(entry.PreferredName)
                || string.IsNullOrWhiteSpace(entry.Provenance))
                throw new ArgumentException("Invalid tiling nomenclature entry.", nameof(source));
            if (entry.CategoryPath is { } path
                && (path.Count == 0 || path.Any(part => string.IsNullOrWhiteSpace(part))))
                throw new ArgumentException("Category paths must contain nonempty labels.", nameof(source));
            if (entry.PresetId is { } presetId
                && (string.IsNullOrWhiteSpace(presetId) || !presetIds.Add(presetId)))
                throw new ArgumentException("Catalog preset IDs must be nonempty and unique.", nameof(source));
            // A named example may supply explicit geometry; it is still just
            // data for the same generic periodic validator and runtime.
            if ((entry.RepresentativeTopology is null) != (entry.RepresentativeRealization is null))
                throw new ArgumentException("A representative requires both topology and geometry.", nameof(source));
            if (entry.RepresentativeTopology is { } topology)
            {
                if (topology.QuotientDsSymbol != entry.CanonicalDsSymbol)
                    throw new ArgumentException("Representative topology does not match its catalog identity.", nameof(source));
                PeriodicMetricWitnessValidator.Validate(topology, entry.RepresentativeRealization!);
            }
            if (!grouped.TryGetValue(entry.CanonicalDsSymbol, out var names))
                grouped[entry.CanonicalDsSymbol] = names = [];
            names.Add(entry);
        }
        entries = grouped;
    }

    /// <summary>Enumerates presentation vocabulary; this is not an allowlist of valid tilings.</summary>
    public IReadOnlyList<TilingNameEntry> ListEntries(bool rulesetSelectorOnly = false) =>
        entries.Values.SelectMany(group => group)
            .Where(entry => !rulesetSelectorOnly || entry.ShowInRulesetSelector)
            .OrderBy(entry => entry.PreferredName, StringComparer.Ordinal)
            .ThenBy(entry => entry.CanonicalDsSymbol, StringComparer.Ordinal)
            .ToArray();

    public TilingDisplayDescription? Describe(
        string dsSymbol,
        PeriodicMetricRealization? realization = null)
    {
        var inspected = DelaneyDressTopology.Inspect(dsSymbol);
        if (inspected.Status != DelaneyDressStatus.Euclidean) return null;
        var canonical = inspected.Symbol!.Canonical;
        var applicable = entries.TryGetValue(canonical, out var names)
            ? names.Where(name => name.Geometry is null || (realization is not null
                && realization.Polygons.Count > 0
                && realization.Polygons.Values.All(poly => Matches(poly, name.Geometry)))).ToArray()
            : [];
        if (applicable.Length == 1)
            return new(applicable[0].PreferredName, true, canonical);
        return new(Fallback(realization), false, canonical);
    }

    /// <summary>
    /// Presentation counts geometric corners, not atomic adjacency splits.
    /// A straight side may contain additional verified interfaces from T-junctions.
    /// No change to the authoritative topology or metric polygon is made.
    /// </summary>
    private static IReadOnlyList<TilingWorldPoint> GeometricCorners(
        IReadOnlyList<TilingWorldPoint> polygon)
    {
        if (polygon.Count < 3) return polygon;
        var result = new List<TilingWorldPoint>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            var prev = polygon[(i + polygon.Count - 1) % polygon.Count];
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            double ax = current.X - prev.X, ay = current.Y - prev.Y;
            double bx = next.X - current.X, by = next.Y - current.Y;
            double lenA = Math.Sqrt(ax * ax + ay * ay), lenB = Math.Sqrt(bx * bx + by * by);
            if (lenA > 0 && lenB > 0 && ax * bx + ay * by > 0
                && Math.Abs(ax * by - ay * bx) <= 1e-8 * lenA * lenB)
                continue;
            result.Add(current);
        }
        return result;
    }

    private static bool Matches(IReadOnlyList<TilingWorldPoint> polygon, TilingGeometryQualifier required)
    {
        if (required.Tolerance <= 0 || !double.IsFinite(required.Tolerance)) return false;
        var corners = GeometricCorners(polygon);
        if (corners.Count != required.Sides) return false;
        var vectors = Enumerable.Range(0, corners.Count).Select(i => new TilingWorldPoint(
            corners[(i + 1) % corners.Count].X - corners[i].X,
            corners[(i + 1) % corners.Count].Y - corners[i].Y)).ToArray();
        static double Length(TilingWorldPoint value) => Math.Sqrt(value.X * value.X + value.Y * value.Y);
        var lengths = vectors.Select(Length).ToArray();
        if (lengths.Any(l => l <= 0 || !double.IsFinite(l))) return false;
        if (required.EqualSideLengths
            && lengths.Any(l => Math.Abs(l - lengths[0]) > required.Tolerance * Math.Max(1, lengths[0])))
            return false;
        if (required.InteriorAngleDegrees.HasValue)
        {
            for (int i = 0; i < vectors.Length; i++)
            {
                var a = vectors[i]; var b = vectors[(i + 1) % vectors.Length];
                var dot = (-a.X * b.X - a.Y * b.Y) / (lengths[i] * lengths[(i + 1) % vectors.Length]);
                var angle = Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI;
                if (Math.Abs(angle - required.InteriorAngleDegrees.Value) > required.Tolerance * 180)
                    return false;
            }
        }
        return true;
    }

    private static string Fallback(PeriodicMetricRealization? realization)
    {
        if (realization is null || realization.Polygons.Count == 0)
            return "a repeating pattern of tiles with unspecified geometry";
        var sides = realization.Polygons.Values.Select(p => GeometricCorners(p).Count).Distinct().OrderBy(x => x)
            .Select(n => n switch
            {
                3 => "triangles",
                4 => "four-sided cells",
                5 => "five-sided cells",
                6 => "six-sided cells",
                _ => $"{n}-sided cells"
            });
        return "a repeating pattern of " + string.Join(" and ", sides);
    }
}
