using System.Text.Json;
using HexCrawl.Web.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexCrawl.IntegrationTests;

public sealed class HexCrawlExceptionMiddlewareTests
{
    [Fact]
    public async Task UnexpectedExceptionReturnsGeneric500WithoutLeakingDetails()
    {
        const string privateDetail = "database password was accidentally included here";
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test-unexpected-error";
        context.Response.Body = new MemoryStream();

        var middleware = new HexCrawlExceptionMiddleware(
            _ => throw new Exception(privateDetail),
            NullLogger<HexCrawlExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Equal(
            "Hex Crawl could not complete the request because of an unexpected application error.",
            error);
        Assert.DoesNotContain(privateDetail, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnownValidationExceptionPreservesTypedClientStatus()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new HexCrawlExceptionMiddleware(
            _ => throw new ArgumentException("Invalid encounter handoff input."),
            NullLogger<HexCrawlExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("Invalid encounter handoff input.", document.RootElement.GetProperty("error").GetString());
    }
}
