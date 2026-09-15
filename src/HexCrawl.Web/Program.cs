using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Demo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));

app.MapGet("/api", () => Results.Ok(new
{
    service = "Hex Crawl API",
    version = "0.1-dev",
    status = "spatial-foundation"
}));

app.MapGet("/api/demo/world", (string? orientation, double? scale, string? unit) =>
{
    var parsedOrientation = string.Equals(orientation, "flat", StringComparison.OrdinalIgnoreCase)
        ? HexOrientation.FlatTop
        : HexOrientation.PointyTop;

    var distance = scale is > 0 and <= 10000 ? scale.Value : 12d;
    var distanceUnit = string.Equals(unit, "km", StringComparison.OrdinalIgnoreCase)
        ? DistanceUnit.Kilometers
        : DistanceUnit.Miles;
    return Results.Ok(DemoWorldResponse.From(DemoWorldFactory.Create(parsedOrientation, distance, distanceUnit)));
});

app.MapGet("/", () => Results.Content(
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
    "text/html; charset=utf-8"));

app.Run();

public partial class Program;
