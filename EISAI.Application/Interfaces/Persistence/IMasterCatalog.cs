using Eisai.Application.Common;
using Eisai.Application.Features.Master;

namespace Eisai.Application.Interfaces.Persistence;

public interface IMasterCatalog
{
    Task<ServiceResult<IReadOnlyList<MasterDefinitionDto>>> ListDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> GetDefinitionAsync(string masterName, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> CreateAsync(CreateMasterCommand command, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> AddColumnAsync(AddMasterColumnCommand command, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> AlterColumnAsync(AlterMasterColumnCommand command, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> DropColumnAsync(string masterName, string columnName, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> SetParentAsync(SetMasterParentCommand command, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> UpdateParentAsync(UpdateMasterParentCommand command, CancellationToken cancellationToken = default);

    Task<ServiceResult<MasterDefinitionDto>> RemoveParentAsync(string masterName, CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteDefinitionAsync(string masterName, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ListRecordsAsync(
        string masterName,
        long? parentId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyDictionary<string, object?>>> GetRecordAsync(
        string masterName,
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyDictionary<string, object?>>> SaveRecordAsync(
        string masterName,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteRecordAsync(string masterName, long id, CancellationToken cancellationToken = default);
}
