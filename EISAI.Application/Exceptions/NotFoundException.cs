using Eisai.Application.Common;

namespace Eisai.Application.Exceptions;

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message)
        : base(AppStatus.NotFound, message)
    {
    }
}
