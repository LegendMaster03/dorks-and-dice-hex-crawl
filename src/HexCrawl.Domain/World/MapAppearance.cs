namespace HexCrawl.Domain.World;

/// <summary>
/// Presentation-only colors. Terrain and feature semantics remain authoritative
/// environment and spatial data, independent of these display preferences.
/// </summary>
public sealed record MapAppearance
{
    public IReadOnlyDictionary<string, string> TerrainColors { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> FeatureColors { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> LabelColors { get; init; } =
        new Dictionary<string, string>();

    public MapAppearance NormalizeAndValidate()
    {
        static IReadOnlyDictionary<string, string> Normalize(
            IReadOnlyDictionary<string, string> source, string label)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Count > 200)
                throw new ArgumentException($"{label} supports at most 200 categories.");
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                var key = pair.Key.Trim().ToLowerInvariant();
                var color = pair.Value?.Trim() ?? "";
                if (key.Length is < 1 or > 80 ||
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        key, @"^[\p{L}\p{N}_ .:/-]+$"))
                    throw new ArgumentException($"{label} has an invalid category name.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                    throw new ArgumentException($"{label} colors must use #RRGGBB format.");
                if (!result.TryAdd(key, color.ToUpperInvariant()))
                    throw new ArgumentException($"{label} has duplicate category names.");
            }
            return result;
        }
        return this with
        {
            TerrainColors = Normalize(TerrainColors, "Terrain palette"),
            FeatureColors = Normalize(FeatureColors, "Feature palette"),
            LabelColors = Normalize(LabelColors, "Label palette")
        };
    }
}
