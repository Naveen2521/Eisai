namespace Eisai.Application.Common;

public static class AppStatus
{
    public const int Success = 200;

    public const int Created = 201;

    public const int BadRequest = 400;

    public const int Unauthorized = 401;

    public const int Forbidden = 403;

    public const int NotFound = 404;

    public const int Conflict = 409;

    public const int ValidationError = 422;

    public const int InternalServerError = 500;

    public const int ServiceUnavailable = 503;

    public static string GetStatusMessage(int statusCode)
    {
        return statusCode switch
        {
            Success => "OK",
            Created => "Created",
            BadRequest => "Bad Request",
            Unauthorized => "Unauthorized",
            Forbidden => "Forbidden",
            NotFound => "Not Found",
            Conflict => "Conflict",
            ValidationError => "Unprocessable Entity",
            InternalServerError => "Internal Server Error",
            ServiceUnavailable => "Service Unavailable",
            _ => "Error"
        };
    }

    public static string GetDefaultMessage(int statusCode)
    {
        return statusCode switch
        {
            Success => "Request completed successfully.",
            Created => "Created successfully.",
            BadRequest => "Invalid request.",
            Unauthorized => "Authentication is required.",
            Forbidden => "You do not have permission to perform this action.",
            NotFound => "The requested record was not found.",
            Conflict => "The record already exists.",
            ValidationError => "Validation failed.",
            InternalServerError => "An unexpected error occurred.",
            ServiceUnavailable => "Service is currently unavailable.",
            _ => "Request failed."
        };
    }
}
