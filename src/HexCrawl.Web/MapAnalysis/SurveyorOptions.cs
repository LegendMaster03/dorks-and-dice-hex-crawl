namespace HexCrawl.Web.MapAnalysis;

public sealed class SurveyorOptions
{
    public const string SectionName = "Surveyor";

    public string BaseUrl { get; set; } = string.Empty;
    public string ServiceToken { get; set; } = string.Empty;
    public int RequestTimeoutMilliseconds { get; set; } = 35_000;

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(ServiceToken);

    public TimeSpan RequestTimeout => TimeSpan.FromMilliseconds(
        Math.Clamp(RequestTimeoutMilliseconds, 1_000, 300_000));
}
