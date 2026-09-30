namespace Eisai.Application.Exceptions;

public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
