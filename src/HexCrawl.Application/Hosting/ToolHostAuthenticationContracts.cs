namespace HexCrawl.Application.Hosting;

public static class ToolHostAuthenticationHeaders
{
    public const string Ticket = "X-Dorks-Tool-Auth-Ticket";
    public const string IntrospectionPath = "X-Dorks-Tool-Auth-Introspection-Path";
    public const string DelegationCapability = "X-Dorks-Tool-Delegation-Capability";
    public const string DelegationPath = "X-Dorks-Tool-Delegation-Path";
}

public sealed record ToolHostUserContext(string Id, string DisplayName);

public sealed record ToolHostCampaignContext(Guid Id, string Name, string Role);

public sealed record ToolHostAuthenticationContext(
    int ContractVersion,
    string ToolSlug,
    string SiteMode,
    ToolHostUserContext User,
    IReadOnlyList<string> GlobalRoles,
    IReadOnlyList<ToolHostCampaignContext> Campaigns)
{
    public string? DelegationCapability { get; init; }
    public string? DelegationPath { get; init; }
}

public interface IToolHostAuthenticationClient
{
    Task<ToolHostAuthenticationContext?> RedeemAsync(
        string ticket,
        string introspectionPath,
        CancellationToken cancellationToken = default);
}
