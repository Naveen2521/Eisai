using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eisai.Api.Common;
using Eisai.Application.Common;
using Eisai.Application.Exceptions;
using FluentValidation;

namespace Eisai.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private const string CorrelationHeader = "X-Correlation-ID";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = GetCorrelationId(context);
        var (statusCode, message, errors) = MapException(exception);

        if (statusCode >= AppStatus.InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception. CorrelationId: {CorrelationId}",
                correlationId);
        }
        else
        {
            _logger.LogWarning(
                "Request failed with status {StatusCode}. CorrelationId: {CorrelationId}. Message: {Message}",
                statusCode,
                correlationId,
                message);
        }

        if (context.Response.HasStarted)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = ApiResponse<object>.From(errors is null
            ? ServiceResult.Fail(statusCode, message)
            : ServiceResult.Fail(statusCode, message, new Dictionary<string, string[]>(errors)));

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Response.Headers.TryGetValue(CorrelationHeader, out var responseId)
            && !string.IsNullOrWhiteSpace(responseId))
        {
            return responseId.ToString();
        }

        if (context.Request.Headers.TryGetValue(CorrelationHeader, out var requestId)
            && !string.IsNullOrWhiteSpace(requestId))
        {
            return requestId.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static (int StatusCode, string Message, IDictionary<string, string[]>? Errors) MapException(Exception exception)
    {
        return exception switch
        {
            JsonException jsonException => (
                AppStatus.BadRequest,
                jsonException.Message,
                null),
            ValidationException validationException => (
                AppStatus.ValidationError,
                AppStatus.GetDefaultMessage(AppStatus.ValidationError),
                validationException.Errors
                    .GroupBy(error => ToCamelCase(error.PropertyName))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).Distinct().ToArray())),
            AppException appException => (
                appException.StatusCode,
                appException.Message,
                null),
            _ => (
                AppStatus.InternalServerError,
                AppStatus.GetDefaultMessage(AppStatus.InternalServerError),
                null)
        };
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
