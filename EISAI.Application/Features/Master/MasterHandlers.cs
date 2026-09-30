using Eisai.Application.Common;
using Eisai.Application.Interfaces.Persistence;
using MediatR;

namespace Eisai.Application.Features.Master;

public sealed class CreateMasterCommandHandler : IRequestHandler<CreateMasterCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public CreateMasterCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(CreateMasterCommand request, CancellationToken cancellationToken)
    {
        return _catalog.CreateAsync(request, cancellationToken);
    }
}

public sealed class AddMasterColumnCommandHandler : IRequestHandler<AddMasterColumnCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public AddMasterColumnCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(AddMasterColumnCommand request, CancellationToken cancellationToken)
    {
        return _catalog.AddColumnAsync(request, cancellationToken);
    }
}

public sealed class AlterMasterColumnCommandHandler : IRequestHandler<AlterMasterColumnCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public AlterMasterColumnCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(AlterMasterColumnCommand request, CancellationToken cancellationToken)
    {
        return _catalog.AlterColumnAsync(request, cancellationToken);
    }
}

public sealed class DropMasterColumnCommandHandler : IRequestHandler<DropMasterColumnCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public DropMasterColumnCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(DropMasterColumnCommand request, CancellationToken cancellationToken)
    {
        return _catalog.DropColumnAsync(request.MasterName, request.ColumnName, cancellationToken);
    }
}

public sealed class SetMasterParentCommandHandler : IRequestHandler<SetMasterParentCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public SetMasterParentCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(SetMasterParentCommand request, CancellationToken cancellationToken)
    {
        return _catalog.SetParentAsync(request, cancellationToken);
    }
}

public sealed class UpdateMasterParentCommandHandler : IRequestHandler<UpdateMasterParentCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public UpdateMasterParentCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(UpdateMasterParentCommand request, CancellationToken cancellationToken)
    {
        return _catalog.UpdateParentAsync(request, cancellationToken);
    }
}

public sealed class RemoveMasterParentCommandHandler : IRequestHandler<RemoveMasterParentCommand, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public RemoveMasterParentCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(RemoveMasterParentCommand request, CancellationToken cancellationToken)
    {
        return _catalog.RemoveParentAsync(request.MasterName, cancellationToken);
    }
}

public sealed class DeleteMasterDefinitionCommandHandler : IRequestHandler<DeleteMasterDefinitionCommand, ServiceResult>
{
    private readonly IMasterCatalog _catalog;

    public DeleteMasterDefinitionCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult> Handle(DeleteMasterDefinitionCommand request, CancellationToken cancellationToken)
    {
        return _catalog.DeleteDefinitionAsync(request.MasterName, cancellationToken);
    }
}

public sealed class GetMasterDefinitionQueryHandler : IRequestHandler<GetMasterDefinitionQuery, ServiceResult<MasterDefinitionDto>>
{
    private readonly IMasterCatalog _catalog;

    public GetMasterDefinitionQueryHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<MasterDefinitionDto>> Handle(GetMasterDefinitionQuery request, CancellationToken cancellationToken)
    {
        return _catalog.GetDefinitionAsync(request.MasterName, cancellationToken);
    }
}

public sealed class ListMasterDefinitionsQueryHandler : IRequestHandler<ListMasterDefinitionsQuery, ServiceResult<IReadOnlyList<MasterDefinitionDto>>>
{
    private readonly IMasterCatalog _catalog;

    public ListMasterDefinitionsQueryHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<IReadOnlyList<MasterDefinitionDto>>> Handle(ListMasterDefinitionsQuery request, CancellationToken cancellationToken)
    {
        return _catalog.ListDefinitionsAsync(cancellationToken);
    }
}

public sealed class ListMasterRecordsQueryHandler
    : IRequestHandler<ListMasterRecordsQuery, ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>
{
    private readonly IMasterCatalog _catalog;

    public ListMasterRecordsQueryHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> Handle(
        ListMasterRecordsQuery request,
        CancellationToken cancellationToken)
    {
        return _catalog.ListRecordsAsync(request.MasterName, request.ParentId, cancellationToken);
    }
}

public sealed class GetMasterRecordQueryHandler
    : IRequestHandler<GetMasterRecordQuery, ServiceResult<IReadOnlyDictionary<string, object?>>>
{
    private readonly IMasterCatalog _catalog;

    public GetMasterRecordQueryHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<IReadOnlyDictionary<string, object?>>> Handle(GetMasterRecordQuery request, CancellationToken cancellationToken)
    {
        return _catalog.GetRecordAsync(request.MasterName, request.Id, cancellationToken);
    }
}

public sealed class SaveMasterRecordCommandHandler
    : IRequestHandler<SaveMasterRecordCommand, ServiceResult<IReadOnlyDictionary<string, object?>>>
{
    private readonly IMasterCatalog _catalog;

    public SaveMasterRecordCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult<IReadOnlyDictionary<string, object?>>> Handle(SaveMasterRecordCommand request, CancellationToken cancellationToken)
    {
        return _catalog.SaveRecordAsync(request.MasterName, request.Values, cancellationToken);
    }
}

public sealed class DeleteMasterRecordCommandHandler : IRequestHandler<DeleteMasterRecordCommand, ServiceResult>
{
    private readonly IMasterCatalog _catalog;

    public DeleteMasterRecordCommandHandler(IMasterCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ServiceResult> Handle(DeleteMasterRecordCommand request, CancellationToken cancellationToken)
    {
        return _catalog.DeleteRecordAsync(request.MasterName, request.Id, cancellationToken);
    }
}
