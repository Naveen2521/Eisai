using Eisai.Application.Common;

namespace Eisai.Application.Exceptions;

public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message)
        : base(AppStatus.Unauthorized, message)
    {
    }
}
