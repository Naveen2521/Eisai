using Eisai.Application.Common;

namespace Eisai.Application.Exceptions;

public sealed class ServiceUnavailableException : AppException
{
    public ServiceUnavailableException(string message)
        : base(AppStatus.ServiceUnavailable, message)
    {
    }
}
