using Eisai.Application.Common;
using System.Text.Json.Serialization;


namespace Eisai.Api.Common;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }

    public int StatusCode { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    public static ApiResponse<T> Ok(T? data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            StatusCode = AppStatus.Success,
            StatusMessage = AppStatus.GetStatusMessage(AppStatus.Success),
            Message = string.IsNullOrWhiteSpace(message)
                ? AppStatus.GetDefaultMessage(AppStatus.Success)
                : message,
            Data = data
        };
    }

    public static ApiResponse<T> From(ServiceResult result, T? data = default)
    {
        var statusCode = result.StatusCode > 0
            ? result.StatusCode
            : result.Succeeded ? AppStatus.Success : AppStatus.InternalServerError;

        return new ApiResponse<T>
        {
            Success = result.Succeeded,
            StatusCode = statusCode,
            StatusMessage = AppStatus.GetStatusMessage(statusCode),
            Message = string.IsNullOrWhiteSpace(result.Message)
                ? AppStatus.GetDefaultMessage(statusCode)
                : result.Message,
            Data = data,
            Errors = result.Errors
        };
    }
}
