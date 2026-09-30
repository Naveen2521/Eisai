using Eisai.Application.Common;
using MediatR;

namespace Eisai.Application.Features.Master;

public sealed class CreateMasterCommand : IRequest<ServiceResult<MasterDefinitionDto>>
{
    public string MasterName { get; init; } = string.Empty;

    public string UniqueIdPrefix { get; init; } = string.Empty;

    public string MenuHeading { get; init; } = string.Empty;

    public string MenuSubHeading { get; init; } = string.Empty;

    public MasterParentInput? Parent { get; init; }

    public List<MasterColumnInput> Columns { get; init; } = [];
}

public sealed class AddMasterColumnCommand : IRequest<ServiceResult<MasterDefinitionDto>>
{
    public string MasterName { get; init; } = string.Empty;

    public MasterColumnInput Column { get; init; } = new();
}

public sealed class AlterMasterColumnCommand : IRequest<ServiceResult<MasterDefinitionDto>>
{
    public string MasterName { get; init; } = string.Empty;

    public string ColumnName { get; init; } = string.Empty;

    public MasterColumnInput Column { get; init; } = new();
}

public sealed record DropMasterColumnCommand(string MasterName, string ColumnName)
    : IRequest<ServiceResult<MasterDefinitionDto>>;

public sealed class SetMasterParentCommand : IRequest<ServiceResult<MasterDefinitionDto>>
{
    public string MasterName { get; init; } = string.Empty;

    public MasterParentInput Parent { get; init; } = new();
}

public sealed class UpdateMasterParentCommand : IRequest<ServiceResult<MasterDefinitionDto>>
{
    public string MasterName { get; init; } = string.Empty;

    public bool Required { get; init; }

    public string OnParentDelete { get; init; } = "restrict";

    public long? DefaultParentId { get; init; }
}

public sealed record RemoveMasterParentCommand(string MasterName) : IRequest<ServiceResult<MasterDefinitionDto>>;

public sealed record DeleteMasterDefinitionCommand(string MasterName) : IRequest<ServiceResult>;

public sealed record GetMasterDefinitionQuery(string MasterName) : IRequest<ServiceResult<MasterDefinitionDto>>;

public sealed record ListMasterDefinitionsQuery : IRequest<ServiceResult<IReadOnlyList<MasterDefinitionDto>>>;

public sealed record ListMasterRecordsQuery(string MasterName, long? ParentId)
    : IRequest<ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>;

public sealed record GetMasterRecordQuery(string MasterName, long Id)
    : IRequest<ServiceResult<IReadOnlyDictionary<string, object?>>>;

public sealed record SaveMasterRecordCommand(string MasterName, IReadOnlyDictionary<string, object?> Values)
    : IRequest<ServiceResult<IReadOnlyDictionary<string, object?>>>;

public sealed record DeleteMasterRecordCommand(string MasterName, long Id) : IRequest<ServiceResult>;
