using System.Text.Json;
using Eisai.Application.Exceptions;
using Eisai.Application.Features.Master;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Eisai.Api.Controllers;

[Route("api/masters")]
public sealed class MastersController : ApiControllerBase
{
    private readonly ISender _sender;

    public MastersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ListMasterDefinitionsQuery(), cancellationToken);
        return FromResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateMasterCommand command, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command, cancellationToken);
        return FromResult(result);
    }

    [HttpGet("{masterName}/definition")]
    public async Task<IActionResult> GetDefinition(string masterName, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetMasterDefinitionQuery(masterName), cancellationToken);
        return FromResult(result);
    }

    [HttpDelete("{masterName}/definition")]
    public async Task<IActionResult> DeleteDefinition(string masterName, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteMasterDefinitionCommand(masterName), cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{masterName}/columns")]
    public async Task<IActionResult> AddColumn(string masterName, MasterColumnInput column, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AddMasterColumnCommand
        {
            MasterName = masterName,
            Column = column
        }, cancellationToken);
        return FromResult(result);
    }

    [HttpPut("{masterName}/columns/{columnName}")]
    public async Task<IActionResult> AlterColumn(
        string masterName,
        string columnName,
        MasterColumnInput column,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AlterMasterColumnCommand
        {
            MasterName = masterName,
            ColumnName = columnName,
            Column = column
        }, cancellationToken);
        return FromResult(result);
    }

    [HttpDelete("{masterName}/columns/{columnName}")]
    public async Task<IActionResult> DropColumn(string masterName, string columnName, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DropMasterColumnCommand(masterName, columnName), cancellationToken);
        return FromResult(result);
    }

    [HttpPut("{masterName}/parent")]
    public async Task<IActionResult> SetParent(string masterName, MasterParentInput parent, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new SetMasterParentCommand
        {
            MasterName = masterName,
            Parent = parent
        }, cancellationToken);
        return FromResult(result);
    }

    [HttpPatch("{masterName}/parent")]
    public async Task<IActionResult> UpdateParent(string masterName, UpdateMasterParentCommand command, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UpdateMasterParentCommand
        {
            MasterName = masterName,
            Required = command.Required,
            OnParentDelete = command.OnParentDelete,
            DefaultParentId = command.DefaultParentId
        }, cancellationToken);
        return FromResult(result);
    }

    [HttpDelete("{masterName}/parent")]
    public async Task<IActionResult> RemoveParent(string masterName, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RemoveMasterParentCommand(masterName), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("{masterName}")]
    public async Task<IActionResult> ListRecords(string masterName, [FromQuery] long? parentId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ListMasterRecordsQuery(masterName, parentId), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("{masterName}/{id:long}")]
    public async Task<IActionResult> GetRecord(string masterName, long id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetMasterRecordQuery(masterName, id), cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{masterName}")]
    public async Task<IActionResult> Save(string masterName, [FromBody] Dictionary<string, JsonElement> body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SaveMasterRecordCommand(masterName, ReadValues(body)),
            cancellationToken);
        return FromResult(result);
    }

    [HttpDelete("{masterName}/{id:long}")]
    public async Task<IActionResult> DeleteRecord(string masterName, long id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteMasterRecordCommand(masterName, id), cancellationToken);
        return FromResult(result);
    }

    private static Dictionary<string, object?> ReadValues(Dictionary<string, JsonElement> body)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in body)
        {
            values[pair.Key] = pair.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => pair.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when pair.Value.TryGetInt64(out var number) => number,
                JsonValueKind.Number => pair.Value.GetDecimal(),
                _ => throw new BadRequestException($"'{pair.Key}' must be a string, number, boolean, or null.")
            };
        }

        return values;
    }
}
