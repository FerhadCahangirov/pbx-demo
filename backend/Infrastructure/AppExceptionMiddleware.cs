using System.Text.Json;
using CallControl.Api.Domain;

namespace CallControl.Api.Infrastructure;

public sealed class AppExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AppExceptionMiddleware> _logger;

    public AppExceptionMiddleware(RequestDelegate next, ILogger<AppExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.ErrorCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                name = ex.ErrorName,
                message = ex.Message,
                errorCode = ex.ErrorCode,
                traceId = context.TraceIdentifier
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                name = "Internal Server Error",
                message = "Unknown server error",
                errorCode = 500,
                traceId = context.TraceIdentifier
            }));
        }
    }
}
