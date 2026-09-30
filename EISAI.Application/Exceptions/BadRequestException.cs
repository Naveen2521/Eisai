using Eisai.Application.Common;

namespace Eisai.Application.Exceptions;

public sealed class BadRequestException : AppException
{
    public BadRequestException(string message)
        : base(AppStatus.BadRequest, message)
    {
    }
}
