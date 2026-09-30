using Microsoft.Extensions.Primitives;

namespace Eisai.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(
                HeaderName,
                out StringValues existingCorrelationId) &&
            !StringValues.IsNullOrEmpty(existingCorrelationId))
        {
            return existingCorrelationId.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }
}