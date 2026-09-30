using Eisai.Application.Common;

namespace Eisai.Application.Exceptions;

public sealed class ConflictException : AppException
{
    public ConflictException(string message)
        : base(AppStatus.Conflict, message)
    {
    }
}
