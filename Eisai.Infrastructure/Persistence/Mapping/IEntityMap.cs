namespace Eisai.Infrastructure.Persistence.Mapping;

public interface IEntityMap<TEntity>
    where TEntity : class
{
    string EntityName { get; }

    string KeyName { get; }

    IReadOnlySet<string> IgnoredOnInsert { get; }

    IReadOnlySet<string> IgnoredOnUpdate { get; }

    IReadOnlySet<string> IgnoredOnSave { get; }

    string ResolveProcedure(string action);
}
