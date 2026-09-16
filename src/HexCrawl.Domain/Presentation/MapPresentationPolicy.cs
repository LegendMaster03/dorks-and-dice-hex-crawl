namespace HexCrawl.Domain.Presentation;

public enum GridVisibility
{
    Hidden,
    Visible
}

public enum TerrainPresentationMode
{
    HiddenUntilKnown,
    AlwaysVisible,
    Manual
}

public enum PresentationAutomationMode
{
    Automatic,
    DmControlled
}

public sealed record MapPresentationPolicy
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public GridVisibility PlayerGrid { get; init; } = GridVisibility.Visible;
    public TerrainPresentationMode TerrainMode { get; init; } = TerrainPresentationMode.HiddenUntilKnown;
    public PresentationAutomationMode AutomationMode { get; init; } = PresentationAutomationMode.Automatic;
    public bool MarkEnteredHexKnown { get; init; } = true;
    public IReadOnlyList<string> InitiallyKnownFeatureCategories { get; init; } = [];
    public IReadOnlyList<string> InitiallyKnownLocationCategories { get; init; } = [];
    public bool AllowPlayerAnnotations { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("A map presentation policy requires a key and name.");
        }

        ValidateCategories(InitiallyKnownFeatureCategories, nameof(InitiallyKnownFeatureCategories));
        ValidateCategories(InitiallyKnownLocationCategories, nameof(InitiallyKnownLocationCategories));

        if (AutomationMode == PresentationAutomationMode.DmControlled
            && (MarkEnteredHexKnown
                || InitiallyKnownFeatureCategories.Count > 0
                || InitiallyKnownLocationCategories.Count > 0))
        {
            throw new InvalidOperationException("DM-controlled presentation can not contain automatic knowledge changes.");
        }
    }

    public bool IsInitiallyKnownFeatureCategory(string category) =>
        ContainsCategory(InitiallyKnownFeatureCategories, category);

    public bool IsInitiallyKnownLocationCategory(string category) =>
        ContainsCategory(InitiallyKnownLocationCategories, category);

    public static MapPresentationPolicy TraditionalHiddenHexcrawl() => new()
    {
        Key = "traditional-hidden-hexcrawl",
        Name = "Traditional Hidden Hexcrawl",
        PlayerGrid = GridVisibility.Hidden,
        TerrainMode = TerrainPresentationMode.Manual,
        AutomationMode = PresentationAutomationMode.Automatic,
        MarkEnteredHexKnown = false,
        AllowPlayerAnnotations = true
    };

    public static MapPresentationPolicy ExplorationMap() => new()
    {
        Key = "exploration-map",
        Name = "Exploration Map",
        PlayerGrid = GridVisibility.Visible,
        TerrainMode = TerrainPresentationMode.HiddenUntilKnown,
        AutomationMode = PresentationAutomationMode.Automatic,
        MarkEnteredHexKnown = true,
        InitiallyKnownFeatureCategories = ["road"],
        InitiallyKnownLocationCategories = ["settlement", "city", "town", "village"],
        AllowPlayerAnnotations = true
    };

    public static MapPresentationPolicy OpenRegionalMap() => new()
    {
        Key = "open-regional-map",
        Name = "Open Regional Map",
        PlayerGrid = GridVisibility.Visible,
        TerrainMode = TerrainPresentationMode.AlwaysVisible,
        AutomationMode = PresentationAutomationMode.Automatic,
        MarkEnteredHexKnown = true,
        InitiallyKnownFeatureCategories = ["road", "trail", "river", "border", "coast", "mountain", "landmark"],
        InitiallyKnownLocationCategories = ["settlement", "city", "town", "village", "landmark"],
        AllowPlayerAnnotations = true
    };

    public static MapPresentationPolicy DmControlled() => new()
    {
        Key = "dm-controlled",
        Name = "DM-Controlled",
        PlayerGrid = GridVisibility.Hidden,
        TerrainMode = TerrainPresentationMode.Manual,
        AutomationMode = PresentationAutomationMode.DmControlled,
        MarkEnteredHexKnown = false,
        AllowPlayerAnnotations = true
    };

    private static bool ContainsCategory(IReadOnlyList<string> categories, string category) =>
        categories.Any(candidate => string.Equals(candidate, category, StringComparison.OrdinalIgnoreCase));

    private static void ValidateCategories(IReadOnlyList<string> categories, string name)
    {
        if (categories.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"{name} can not contain an empty category.");
        }
    }
}
