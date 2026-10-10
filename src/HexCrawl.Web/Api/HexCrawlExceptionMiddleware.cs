using HexCrawl.Application;
using HexCrawl.Application.Rules;

namespace HexCrawl.Web.Api;

public sealed class HexCrawlExceptionMiddleware(
    RequestDelegate next,
    ILogger<HexCrawlExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
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
        catch (OptionalProviderResolutionException exception)
        {
            await WriteProviderAsync(context, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, exception.Message);
        }
        catch (BadHttpRequestException exception)
        {
            // Invalid or unparseable JSON belongs to the client, not an
            // unexpected server error. Do not expose serializer internals.
            var status = exception.StatusCode is >= 400 and <= 499
                ? exception.StatusCode : StatusCodes.Status400BadRequest;
            await WriteAsync(context, status, "The request body is malformed or invalid.");
        }
        catch (ArgumentException exception)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled Hex Crawl request failure for {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);
            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Hex Crawl could not complete the request because of an unexpected application error.");
        }
    }

    private static async Task WriteProviderAsync(
        HttpContext context,
        OptionalProviderResolutionException exception)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException("The response has already started and the Hex Crawl error can not be written.");
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = exception.Message,
            provider = exception.Provider,
            mechanicKey = exception.MechanicKey,
            status = exception.Status,
            missingInputKeys = exception.MissingInputKeys
        }, context.RequestAborted);
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
