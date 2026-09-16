using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Application.Hosting;
using HexCrawl.Application.Persistence;
using HexCrawl.Infrastructure.Assets;
using HexCrawl.Infrastructure.Hosting;
using HexCrawl.Infrastructure.Persistence;
using HexCrawl.Web.Api;
using HexCrawl.Web.Authentication;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var connectionString = builder.Configuration.GetConnectionString("HexCrawl");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = "Data Source=hex-crawl.db";
}

var mapImportOptions = builder.Configuration.GetSection(MapImportOptions.SectionName).Get<MapImportOptions>() ?? new MapImportOptions();
mapImportOptions.Validate();
var requestBodyCeiling = checked(mapImportOptions.MaxFileBytes + (4L * 1024 * 1024));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = requestBodyCeiling);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = requestBodyCeiling);
builder.Services.Configure<MapImportOptions>(builder.Configuration.GetSection(MapImportOptions.SectionName));

var assetRoot = builder.Configuration["MapAssets:RootPath"];
if (string.IsNullOrWhiteSpace(assetRoot))
{
    assetRoot = Path.Combine(builder.Environment.ContentRootPath, "data", "assets");
}

var toolHostBaseUrl = builder.Configuration["ToolHost:BaseUrl"];
Uri? toolHostBaseUri = null;
if (!string.IsNullOrWhiteSpace(toolHostBaseUrl))
{
    if (!Uri.TryCreate(toolHostBaseUrl, UriKind.Absolute, out toolHostBaseUri)
        || (toolHostBaseUri.Scheme != Uri.UriSchemeHttp
            && toolHostBaseUri.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException("ToolHost:BaseUrl must be an absolute HTTP or HTTPS URL.");
    }
}

builder.Services.AddSingleton<IHexCrawlStore>(_ => new SqliteHexCrawlStore(connectionString));
builder.Services.AddSingleton<IMapAssetStore>(_ => new FilesystemMapAssetStore(assetRoot));
builder.Services.AddScoped<HexCrawlService>();
builder.Services.AddScoped<SourceMapApplicationService>();
builder.Services
    .AddHttpClient<IToolHostAuthenticationClient, DorksAndDiceToolHostAuthenticationClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(3);
        client.BaseAddress = toolHostBaseUri;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
builder.Services.AddHealthChecks();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<IHexCrawlStore>().InitializeAsync();
}

app.UseMiddleware<HexCrawlExceptionMiddleware>();
app.UseMiddleware<HostedToolAuthenticationMiddleware>();
app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapGet("/ready", () => Results.Ok(new
{
    status = "ready",
    database = "sqlite",
    persistence = "initialized",
    mapAssets = "filesystem"
}));

app.MapGet("/api", () => Results.Ok(new
{
    service = "Hex Crawl API",
    version = "0.4-dev",
    status = "source-map-import",
    endpointFamilies = new[] { "overworlds", "features", "locations", "source-maps", "expeditions", "runtime" }
}));

PersistentApiEndpoints.Map(app);
SourceMapApiEndpoints.Map(app);

app.MapGet("/", () => Shell());
app.MapFallback((HttpContext context) =>
    context.Request.Path.StartsWithSegments("/api")
        ? Results.NotFound()
        : Shell());

app.Run();

static IResult Shell() => Results.Content(
    """
    <!doctype html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Dorks & Dice Hex Crawl</title>
    </head>
    <body>
        <main id="tool-root" data-tool-base-path="/" data-tool-route="/"></main>
        <script type="module" src="/app.js"></script>
    </body>
    </html>
    """,
    "text/html; charset=utf-8");

public partial class Program;
