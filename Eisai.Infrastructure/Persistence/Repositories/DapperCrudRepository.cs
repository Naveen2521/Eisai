using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using Eisai.Application.Common;
using Eisai.Application.Interfaces.Persistence;
using Eisai.Infrastructure.Persistence.Connection;
using Eisai.Infrastructure.Persistence.Mapping;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Eisai.Infrastructure.Persistence.Repositories;

public sealed class DapperCrudRepository<TEntity, TKey> : ICrudRepository<TEntity, TKey>
    where TEntity : class
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeProcedure = new(
        "^dbo\\.sp_[A-Za-z][A-Za-z0-9_]{0,100}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IEntityMap<TEntity> _map;

    public DapperCrudRepository(IDbConnectionFactory connectionFactory, IEntityMap<TEntity> map)
    {
        _connectionFactory = connectionFactory;
        _map = map;

        if (!SafeIdentifier.IsMatch(_map.EntityName) || !SafeIdentifier.IsMatch(_map.KeyName))
        {
            throw new InvalidOperationException($"Entity map '{typeof(TEntity).Name}' has an invalid SQL identifier.");
        }

        foreach (var action in new[] { "GetById", "GetAll", "Save", "Delete" })
        {
            if (!SafeProcedure.IsMatch(_map.ResolveProcedure(action)))
            {
                throw new InvalidOperationException($"Entity map '{typeof(TEntity).Name}' has an invalid procedure for {action}.");
            }
        }
    }

    public async Task<ServiceResult<TEntity>> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add(_map.KeyName, id);

        var found = await QuerySingleOrDefaultAsync(connection, "GetById", parameters, cancellationToken);
        if (found.Conflict is not null)
        {
            return ServiceResult<TEntity>.Conflict(found.Conflict);
        }

        return found.Entity is null
            ? ServiceResult<TEntity>.NotFound($"{_map.EntityName} was not found.")
            : ServiceResult<TEntity>.Ok(found.Entity);
    }

    public async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<TEntity>(
            new CommandDefinition(
                Procedure("GetAll"),
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<ServiceResult<TEntity>> SaveAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var saved = await QuerySingleOrDefaultAsync(
            connection,
            "Save",
            ToParameters(entity, includeKey: true, _map.IgnoredOnSave),
            cancellationToken);

        if (saved.Conflict is not null)
        {
            return ServiceResult<TEntity>.Conflict(saved.Conflict);
        }

        if (saved.Entity is null)
        {
            return ServiceResult<TEntity>.NotFound($"{_map.EntityName} was not found.");
        }

        return ServiceResult<TEntity>.Ok(saved.Entity);
    }

    public async Task<ServiceResult<TEntity>> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var created = await QuerySingleOrDefaultAsync(
            connection,
            "Insert",
            ToParameters(entity, includeKey: false, _map.IgnoredOnInsert),
            cancellationToken);

        if (created.Conflict is not null)
        {
            return ServiceResult<TEntity>.Conflict(created.Conflict);
        }

        if (created.Entity is null)
        {
            throw new InvalidOperationException($"{_map.EntityName} was not returned by {Procedure("Insert")}.");
        }

        return ServiceResult<TEntity>.Ok(created.Entity);
    }

    public async Task<ServiceResult<TEntity>> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var updated = await QuerySingleOrDefaultAsync(
            connection,
            "Update",
            ToParameters(entity, includeKey: true, _map.IgnoredOnUpdate),
            cancellationToken);

        if (updated.Conflict is not null)
        {
            return ServiceResult<TEntity>.Conflict(updated.Conflict);
        }

        return updated.Entity is null
            ? ServiceResult<TEntity>.NotFound($"{_map.EntityName} was not found.")
            : ServiceResult<TEntity>.Ok(updated.Entity);
    }

    public async Task<ServiceResult> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add(_map.KeyName, id);

        try
        {
            var affected = await connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    Procedure("Delete"),
                    parameters,
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: cancellationToken));

            return affected > 0
                ? ServiceResult.Ok()
                : ServiceResult.NotFound($"{_map.EntityName} was not found.");
        }
        catch (SqlException exception) when (IsConflict(exception))
        {
            return ServiceResult.Conflict(ConflictMessage(exception));
        }
    }

    private async Task<(TEntity? Entity, string? Conflict)> QuerySingleOrDefaultAsync(
        IDbConnection connection,
        string action,
        DynamicParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await connection.QuerySingleOrDefaultAsync<TEntity>(
                new CommandDefinition(
                    Procedure(action),
                    parameters,
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: cancellationToken));

            return (entity, null);
        }
        catch (SqlException exception) when (IsConflict(exception))
        {
            return (default, ConflictMessage(exception));
        }
    }

    private DynamicParameters ToParameters(TEntity entity, bool includeKey, IReadOnlySet<string> ignored)
    {
        var parameters = new DynamicParameters();

        foreach (var property in typeof(TEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || ignored.Contains(property.Name))
            {
                continue;
            }

            if (!includeKey && property.Name == _map.KeyName)
            {
                continue;
            }

            if (!IsScalar(property.PropertyType) || !SafeIdentifier.IsMatch(property.Name))
            {
                continue;
            }

            parameters.Add(property.Name, property.GetValue(entity));
        }

        return parameters;
    }

    private string Procedure(string action) => _map.ResolveProcedure(action);

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset);
    }

    private static bool IsConflict(SqlException exception)
    {
        return exception.Number is 2601 or 2627 || exception.Number >= 50000;
    }

    private static string ConflictMessage(SqlException exception)
    {
        return exception.Number >= 50000
            ? exception.Message
            : "A record with the same value already exists.";
    }
}
