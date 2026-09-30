using Eisai.Api.Common;
using Eisai.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Eisai.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult Success<T>(T data, string? message = null)
    {
        return Ok(ApiResponse<T>.Ok(data, message));
    }

    protected IActionResult FromResult<T>(ServiceResult<T> result)
    {
        var response = result.Succeeded
            ? ApiResponse<T>.From(result, result.Data)
            : ApiResponse<T>.From(result);

        return StatusCode(response.StatusCode, response);
    }

    protected IActionResult FromResult(ServiceResult result)
    {
        return StatusCode(result.Succeeded ? AppStatus.Success : result.StatusCode, ApiResponse<object?>.From(result));
    }
}
