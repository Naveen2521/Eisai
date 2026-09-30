using Eisai.Application.Common;

namespace Eisai.Application.Interfaces.Persistence;

public interface ICrudRepository<TEntity, TKey>
    where TEntity : class
{
    Task<ServiceResult<TEntity>> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<TEntity>> SaveAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<ServiceResult<TEntity>> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<ServiceResult<TEntity>> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteAsync(TKey id, CancellationToken cancellationToken = default);
}
