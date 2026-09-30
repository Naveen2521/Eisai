using Eisai.Application.Common;

namespace Eisai.Application.Exceptions;

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message)
        : base(AppStatus.Forbidden, message)
    {
    }
}
