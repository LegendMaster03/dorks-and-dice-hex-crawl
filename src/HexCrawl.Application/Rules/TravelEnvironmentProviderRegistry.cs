namespace HexCrawl.Application.Rules;

public sealed record TravelEnvironmentProviderSelection(
    ITravelEnvironmentProvider? Provider,
    string? Detail);

public sealed class TravelEnvironmentProviderRegistry
{
    private readonly IReadOnlyList<ITravelEnvironmentProvider> _providers;

    public TravelEnvironmentProviderRegistry(IEnumerable<ITravelEnvironmentProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();

        var duplicate = _providers
            .GroupBy(provider => provider.Metadata.ProviderKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Travel/environment provider key '{duplicate.Key}' is registered more than once.");
        }
    }

    public IReadOnlyList<TravelEnvironmentProviderMetadata> Providers =>
        _providers.Select(provider => provider.Metadata).ToArray();

    public TravelEnvironmentProviderSelection Select(string? providerKey = null)
    {
        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            var normalized = providerKey.Trim();
            var selected = _providers.SingleOrDefault(provider =>
                string.Equals(provider.Metadata.ProviderKey, normalized, StringComparison.Ordinal));
            return selected is null
                ? new TravelEnvironmentProviderSelection(
                    null,
                    $"Travel/environment provider '{normalized}' is not registered.")
                : new TravelEnvironmentProviderSelection(selected, null);
        }

        if (_providers.Count == 0)
        {
            return new TravelEnvironmentProviderSelection(
                null,
                "No travel/environment provider is configured.");
        }

        if (_providers.Count == 1)
        {
            return new TravelEnvironmentProviderSelection(_providers[0], null);
        }

        var defaults = _providers.Where(provider => provider.Metadata.IsDefault).ToArray();
        return defaults.Length switch
        {
            1 => new TravelEnvironmentProviderSelection(defaults[0], null),
            0 => new TravelEnvironmentProviderSelection(
                null,
                "Multiple travel/environment providers are registered and no default provider was selected."),
            _ => new TravelEnvironmentProviderSelection(
                null,
                "Multiple travel/environment providers are marked as the default provider.")
        };
    }
}
