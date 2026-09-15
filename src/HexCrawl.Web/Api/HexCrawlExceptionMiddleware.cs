using HexCrawl.Application;

namespace HexCrawl.Web.Api;

public sealed class HexCrawlExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (HexCrawlNotFoundException exception)
        {
            await WriteAsync(context, StatusCodes.Status404NotFound, exception.Message);
        }
        catch (HexCrawlConcurrencyException exception)
        {
            await WriteAsync(context, StatusCodes.Status409Conflict, exception.Message);
        }
        catch (HexCrawlConflictException exception)
        {
            await WriteAsync(context, StatusCodes.Status409Conflict, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, exception.Message);
        }
        catch (ArgumentException exception)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException("The response has already started and the Hex Crawl error can not be written.");
        }
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = message }, context.RequestAborted);
    }
}
