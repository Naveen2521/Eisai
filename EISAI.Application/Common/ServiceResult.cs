namespace Eisai.Application.Common;

public class ServiceResult
{
    public bool Succeeded { get; init; }

    public string? Message { get; init; }

    public int StatusCode { get; init; }

    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    public static ServiceResult Ok()
    {
        return new ServiceResult
        {
            Succeeded = true,
            StatusCode = AppStatus.Success,
            Message = AppStatus.GetDefaultMessage(AppStatus.Success)
        };
    }

    public static ServiceResult Created(string message)
    {
        return new ServiceResult
        {
            Succeeded = true,
            StatusCode = AppStatus.Created,
            Message = message
        };
    }

    public static ServiceResult BadRequest(string message)
    {
        return Fail(AppStatus.BadRequest, message);
    }

    public static ServiceResult Unauthorized(string message)
    {
        return Fail(AppStatus.Unauthorized, message);
    }

    public static ServiceResult Forbidden(string message)
    {
        return Fail(AppStatus.Forbidden, message);
    }

    public static ServiceResult NotFound(string message)
    {
        return Fail(AppStatus.NotFound, message);
    }

    public static ServiceResult Conflict(string message)
    {
        return Fail(AppStatus.Conflict, message);
    }

    public static ServiceResult Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        return Fail(AppStatus.ValidationError, "Validation failed.", errors);
    }

    public static ServiceResult ServiceUnavailable(string message)
    {
        return Fail(AppStatus.ServiceUnavailable, message);
    }

    public static ServiceResult Fail(
        int statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        return new ServiceResult
        {
            Succeeded = false,
            StatusCode = statusCode,
            Message = message,
            Errors = errors
        };
    }
}

public sealed class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data)
    {
        return new ServiceResult<T>
        {
            Succeeded = true,
            StatusCode = AppStatus.Success,
            Message = AppStatus.GetDefaultMessage(AppStatus.Success),
            Data = data
        };
    }

    public static ServiceResult<T> Created(T data, string message)
    {
        return new ServiceResult<T>
        {
            Succeeded = true,
            StatusCode = AppStatus.Created,
            Message = message,
            Data = data
        };
    }

    public static new ServiceResult<T> BadRequest(string message)
    {
        return Fail(AppStatus.BadRequest, message);
    }

    public static new ServiceResult<T> Unauthorized(string message)
    {
        return Fail(AppStatus.Unauthorized, message);
    }

    public static new ServiceResult<T> Forbidden(string message)
    {
        return Fail(AppStatus.Forbidden, message);
    }

    public static new ServiceResult<T> NotFound(string message)
    {
        return Fail(AppStatus.NotFound, message);
    }

    public static new ServiceResult<T> Conflict(string message)
    {
        return Fail(AppStatus.Conflict, message);
    }

    public static new ServiceResult<T> Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        return Fail(AppStatus.ValidationError, "Validation failed.", errors);
    }

    public static new ServiceResult<T> ServiceUnavailable(string message)
    {
        return Fail(AppStatus.ServiceUnavailable, message);
    }

    public static new ServiceResult<T> Fail(
        int statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        return new ServiceResult<T>
        {
            Succeeded = false,
            StatusCode = statusCode,
            Message = message,
            Errors = errors
        };
    }

    public ServiceResult<TOutput> Map<TOutput>(Func<T, TOutput> map)
    {
        if (!Succeeded || Data is null)
        {
            return ServiceResult<TOutput>.Fail(StatusCode, Message ?? "Request failed.", Errors);
        }

        return ServiceResult<TOutput>.Ok(map(Data));
    }
}
