using Dapper;
using Eisai.Application.Common;
using Eisai.Application.Features.Master;
using Eisai.Application.Interfaces.Persistence;
using Eisai.Infrastructure.Persistence.Connection;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;

namespace Eisai.Infrastructure.Persistence.Masters;

public sealed class MasterCatalog : IMasterCatalog
{
    private const string EnsureSql = """
        IF OBJECT_ID(N'dbo.ctl_Master', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ctl_Master
            (
                MasterId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ctl_Master PRIMARY KEY,
                MasterName NVARCHAR(40) NOT NULL,
                UniqueIdPrefix NVARCHAR(10) NOT NULL,
                CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_ctl_Master_CreatedOn DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT UQ_ctl_Master_MasterName UNIQUE (MasterName)
            );
        END;

        IF COL_LENGTH(N'dbo.ctl_Master', N'MenuHeading') IS NULL
            ALTER TABLE dbo.ctl_Master ADD MenuHeading NVARCHAR(80) NULL;

        IF COL_LENGTH(N'dbo.ctl_Master', N'MenuSubHeading') IS NULL
            ALTER TABLE dbo.ctl_Master ADD MenuSubHeading NVARCHAR(80) NULL;

        IF OBJECT_ID(N'dbo.ctl_MasterColumn', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ctl_MasterColumn
            (
                ColumnId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ctl_MasterColumn PRIMARY KEY,
                MasterId BIGINT NOT NULL,
                ColumnName NVARCHAR(40) NOT NULL,
                DataType NVARCHAR(30) NOT NULL,
                Length INT NULL,
                IsMax BIT NOT NULL,
                PrecisionValue INT NULL,
                ScaleValue INT NULL,
                IsNullable BIT NOT NULL,
                IsUnique BIT NOT NULL,
                SortOrder INT NOT NULL,
                CONSTRAINT FK_ctl_MasterColumn_Master FOREIGN KEY (MasterId) REFERENCES dbo.ctl_Master (MasterId),
                CONSTRAINT UQ_ctl_MasterColumn UNIQUE (MasterId, ColumnName)
            );
        END;

        IF OBJECT_ID(N'dbo.ctl_MasterParent', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ctl_MasterParent
            (
                MasterId BIGINT NOT NULL CONSTRAINT PK_ctl_MasterParent PRIMARY KEY,
                ParentMasterId BIGINT NOT NULL,
                ColumnName NVARCHAR(40) NOT NULL,
                IsRequired BIT NOT NULL,
                OnParentDelete NVARCHAR(20) NOT NULL,
                CONSTRAINT FK_ctl_MasterParent_Master FOREIGN KEY (MasterId) REFERENCES dbo.ctl_Master (MasterId),
                CONSTRAINT FK_ctl_MasterParent_Parent FOREIGN KEY (ParentMasterId) REFERENCES dbo.ctl_Master (MasterId)
            );
        END;
        """;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IDbConnectionFactory _connectionFactory;

    public MasterCatalog(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<ServiceResult<IReadOnlyList<MasterDefinitionDto>>> ListDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var names = await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT MasterName FROM dbo.ctl_Master ORDER BY MasterName",
                cancellationToken: cancellationToken));

            var definitions = new List<MasterDefinitionDto>();
            foreach (var name in names)
            {
                var master = await LoadAsync(connection, null, name, cancellationToken);
                if (master is null)
                {
                    continue;
                }

                var tableExists = await TableExistsAsync(connection, null, name, cancellationToken);
                definitions.Add(ToDto(master) with { TableExists = tableExists });
            }

            return ServiceResult<IReadOnlyList<MasterDefinitionDto>>.Ok(definitions);
        }, cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> GetDefinitionAsync(string masterName, CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            if (!MasterSqlNames.IsMaster(masterName))
            {
                return InvalidMaster<MasterDefinitionDto>();
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var master = await LoadAsync(connection, null, masterName, cancellationToken);
            return master is null
                ? ServiceResult<MasterDefinitionDto>.NotFound($"Master '{masterName}' was not found.")
                : ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> CreateAsync(CreateMasterCommand command, CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            var columns = command.Columns.Select((column, index) => ToColumn(column, index + 1)).ToList();
            var columnError = ValidateColumnList(command.MasterName, columns, command.Parent?.MasterName);
            if (columnError is not null)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest(columnError);
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);

            if (await LoadAsync(connection, null, command.MasterName, cancellationToken) is not null)
            {
                return ServiceResult<MasterDefinitionDto>.Conflict($"Master '{command.MasterName}' already exists.");
            }

            if (await TableExistsAsync(connection, null, command.MasterName, cancellationToken))
            {
                return ServiceResult<MasterDefinitionDto>.Conflict($"Table '{MasterScripts.Table(command.MasterName)}' already exists.");
            }

            StoredMaster? parentMaster = null;
            if (command.Parent is not null)
            {
                var parentError = await ParentErrorAsync(connection, null, command.MasterName, command.Parent.MasterName, cancellationToken);
                if (parentError is not null)
                {
                    return ParentFailure(parentError);
                }

                parentMaster = await LoadAsync(connection, null, command.Parent.MasterName, cancellationToken);
            }

            var master = new StoredMaster
            {
                MasterName = command.MasterName.Trim(),
                UniqueIdPrefix = command.UniqueIdPrefix.Trim().ToUpperInvariant(),
                MenuHeading = command.MenuHeading.Trim(),
                MenuSubHeading = command.MenuSubHeading.Trim(),
                Columns = columns,
                Parent = parentMaster is null
                    ? null
                    : ToParent(parentMaster, command.Parent!)
            };

            if (master.Parent is not null && columns.Any(column => column.Name.Equals(master.Parent.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest($"Column '{master.Parent.ColumnName}' is reserved for the parent link.");
            }

            return await InTransaction(connection, async (transaction, cancellationToken) =>
            {
                master.MasterId = await connection.QuerySingleAsync<long>(Command(
                    """
                    INSERT INTO dbo.ctl_Master (MasterName, UniqueIdPrefix, MenuHeading, MenuSubHeading)
                    OUTPUT INSERTED.MasterId
                    VALUES (@MasterName, @UniqueIdPrefix, @MenuHeading, @MenuSubHeading);
                    """,
                    new { master.MasterName, master.UniqueIdPrefix, master.MenuHeading, master.MenuSubHeading },
                    transaction,
                    cancellationToken));

                foreach (var column in master.Columns)
                {
                    await InsertColumnAsync(connection, transaction, master.MasterId, column, cancellationToken);
                }

                if (master.Parent is not null)
                {
                    await InsertParentAsync(connection, transaction, master, cancellationToken);
                }

                await ExecuteAsync(connection, transaction, MasterScripts.CreateTable(master), cancellationToken);
                foreach (var script in MasterScripts.CreateIndexes(master))
                {
                    await ExecuteAsync(connection, transaction, script, cancellationToken);
                }

                await RegenerateAsync(connection, transaction, master, cancellationToken);
                if (parentMaster is not null)
                {
                    await RegenerateStoredAsync(connection, transaction, parentMaster.MasterName, cancellationToken);
                }

                return ServiceResult<MasterDefinitionDto>.Created(ToDto(master), $"{master.MasterName} created successfully.");
            }, cancellationToken);
        }, cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> AddColumnAsync(AddMasterColumnCommand command, CancellationToken cancellationToken = default)
    {
        return Locked(ct => ChangeAsync(command.MasterName, async (connection, transaction, master, token) =>
        {
            var column = ToColumn(command.Column, master.Columns.Count == 0 ? 1 : master.Columns.Max(item => item.SortOrder) + 1);
            var columnError = ValidateColumnList(master.MasterName, [column], master.Parent?.MasterName);
            if (columnError is not null)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest(columnError);
            }

            if (master.Columns.Any(item => item.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase))
                || (master.Parent?.ColumnName.Equals(column.Name, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return ServiceResult<MasterDefinitionDto>.Conflict($"Column '{column.Name}' already exists.");
            }

            if (!column.Nullable && await CountRowsAsync(connection, transaction, master, token) > 0)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest("A required column can be added only when the master has no rows. Add it as nullable.");
            }

            await ExecuteAsync(connection, transaction, MasterScripts.AddColumn(master, column), token);
            await InsertColumnAsync(connection, transaction, master.MasterId, column, token);
            master.Columns.Add(column);
            if (column.Unique)
            {
                await ExecuteAsync(connection, transaction, MasterScripts.CreateUniqueIndex(master, column), token);
            }

            await RegenerateAsync(connection, transaction, master, token);
            return ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, ct), cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> AlterColumnAsync(AlterMasterColumnCommand command, CancellationToken cancellationToken = default)
    {
        return Locked(ct => ChangeAsync(command.MasterName, async (connection, transaction, master, token) =>
        {
            var current = master.Columns.FirstOrDefault(column => column.Name.Equals(command.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                return MissingColumn(master, command.ColumnName);
            }

            if (!string.IsNullOrWhiteSpace(command.Column.Name)
                && !command.Column.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest("Column name cannot be changed.");
            }

            if (!MasterColumnRules.IsSameTypeFamily(current.DataType, command.Column.DataType))
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest(
                    $"Column type cannot change from {current.DataType} to {MasterColumnRules.NormalizeType(command.Column.DataType)}.");
            }

            var updated = ToColumn(command.Column, current.SortOrder);
            updated.Name = current.Name;
            var columnError = MasterColumnRules.Validate(new MasterColumnInput
            {
                Name = current.Name,
                DataType = command.Column.DataType,
                Length = command.Column.Length,
                Precision = command.Column.Precision,
                Scale = command.Column.Scale,
                Nullable = command.Column.Nullable,
                Unique = command.Column.Unique
            });
            if (columnError is not null)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest(columnError);
            }

            if (await HasBlockingDataAsync(connection, transaction, master, current, updated, token) is string blocked)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest(blocked);
            }

            foreach (var column in master.Columns.Where(column => column.Unique))
            {
                await ExecuteAsync(connection, transaction, MasterScripts.DropIndexIfExists(MasterScripts.UniqueIndex(master.MasterName, column.Name), MasterScripts.Table(master.MasterName)), token);
            }

            if (NeedsAlter(current, updated))
            {
                await ExecuteAsync(connection, transaction, MasterScripts.AlterColumn(master, updated), token);
            }

            CopyColumn(current, updated);
            await UpdateColumnAsync(connection, transaction, master.MasterId, current, token);
            foreach (var column in master.Columns.Where(column => column.Unique))
            {
                await ExecuteAsync(connection, transaction, MasterScripts.CreateUniqueIndex(master, column), token);
            }

            await RegenerateAsync(connection, transaction, master, token);
            return ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, ct), cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> DropColumnAsync(string masterName, string columnName, CancellationToken cancellationToken = default)
    {
        return Locked(ct => ChangeAsync(masterName, async (connection, transaction, master, token) =>
        {
            if (!MasterSqlNames.IsColumn(columnName))
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest("Column name is invalid.");
            }

            if (master.Parent?.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase) == true)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest("Remove the parent instead of dropping this column.");
            }

            var column = master.Columns.FirstOrDefault(item => item.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                return MissingColumn(master, columnName);
            }

            if (master.Columns.Count == 1)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest("A master must keep at least one column.");
            }

            if (column.Unique)
            {
                await ExecuteAsync(connection, transaction, MasterScripts.DropIndexIfExists(MasterScripts.UniqueIndex(master.MasterName, column.Name), MasterScripts.Table(master.MasterName)), token);
            }

            await ExecuteAsync(connection, transaction, MasterScripts.DropColumn(master, column.Name), token);
            await connection.ExecuteAsync(Command(
                "DELETE FROM dbo.ctl_MasterColumn WHERE MasterId = @MasterId AND ColumnName = @ColumnName",
                new { master.MasterId, ColumnName = column.Name },
                transaction,
                token));
            master.Columns.Remove(column);
            await RegenerateAsync(connection, transaction, master, token);
            return ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, ct), cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> SetParentAsync(SetMasterParentCommand command, CancellationToken cancellationToken = default)
    {
        return Locked(ct => ChangeAsync(command.MasterName, async (connection, transaction, master, token) =>
        {
            if (master.Parent is not null)
            {
                return ServiceResult<MasterDefinitionDto>.Conflict("Parent is already set. Remove it before choosing another parent.");
            }

            var parentError = await ParentErrorAsync(connection, transaction, master.MasterName, command.Parent.MasterName, token);
            if (parentError is not null)
            {
                return ParentFailure(parentError);
            }

            var parentMaster = (await LoadAsync(connection, transaction, command.Parent.MasterName, token))!;
            var parent = ToParent(parentMaster, command.Parent);
            if (master.Columns.Any(column => column.Name.Equals(parent.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest($"Column '{parent.ColumnName}' already exists.");
            }

            var rows = await CountRowsAsync(connection, transaction, master, token);
            if (parent.Required && rows > 0 && command.Parent.DefaultParentId is null or <= 0)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest("defaultParentId is required because this master already has rows.");
            }

            if (command.Parent.DefaultParentId is > 0
                && await ActiveParentCountAsync(connection, transaction, parentMaster, command.Parent.DefaultParentId.Value, token) == 0)
            {
                return ServiceResult<MasterDefinitionDto>.BadRequest($"{parent.MasterName} was not found.");
            }

            var added = new StoredColumn
            {
                Name = parent.ColumnName,
                DataType = "bigint",
                Nullable = !parent.Required || rows > 0,
                SortOrder = 0
            };
            await ExecuteAsync(connection, transaction, MasterScripts.AddColumn(master, added), token);
            if (command.Parent.DefaultParentId is > 0)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"UPDATE dbo.{MasterScripts.Quote(MasterScripts.Table(master.MasterName))} SET {MasterScripts.Quote(parent.ColumnName)} = @ParentId WHERE {MasterScripts.Quote(parent.ColumnName)} IS NULL;",
                    token,
                    new { ParentId = command.Parent.DefaultParentId });
            }

            master.Parent = parent;
            if (parent.Required)
            {
                added.Nullable = false;
                await ExecuteAsync(connection, transaction, MasterScripts.AlterColumn(master, added), token);
            }

            await RebuildIndexesAsync(connection, transaction, master, token);
            await InsertParentAsync(connection, transaction, master, token);
            await RegenerateAsync(connection, transaction, master, token);
            await RegenerateStoredAsync(connection, transaction, parent.MasterName, token);
            return ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, ct), cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> UpdateParentAsync(UpdateMasterParentCommand command, CancellationToken cancellationToken = default)
    {
        return Locked(ct => ChangeAsync(command.MasterName, async (connection, transaction, master, token) =>
        {
            if (master.Parent is null)
            {
                return ServiceResult<MasterDefinitionDto>.NotFound($"Master '{master.MasterName}' has no parent.");
            }

            var parentMaster = await LoadAsync(connection, transaction, master.Parent.MasterName, token);
            if (parentMaster is null)
            {
                return ServiceResult<MasterDefinitionDto>.NotFound($"Parent master '{master.Parent.MasterName}' was not found.");
            }

            var required = command.Required;
            var onDelete = MasterSqlNames.NormalizeParentDelete(command.OnParentDelete);
            if (required && await CountNullsAsync(connection, transaction, master, master.Parent.ColumnName, token) > 0)
            {
                if (command.DefaultParentId is null or <= 0)
                {
                    return ServiceResult<MasterDefinitionDto>.BadRequest("defaultParentId is required because some rows have no parent.");
                }

                if (await ActiveParentCountAsync(connection, transaction, parentMaster, command.DefaultParentId.Value, token) == 0)
                {
                    return ServiceResult<MasterDefinitionDto>.BadRequest($"{master.Parent.MasterName} was not found.");
                }

                await ExecuteAsync(
                    connection,
                    transaction,
                    $"UPDATE dbo.{MasterScripts.Quote(MasterScripts.Table(master.MasterName))} SET {MasterScripts.Quote(master.Parent.ColumnName)} = @ParentId WHERE {MasterScripts.Quote(master.Parent.ColumnName)} IS NULL;",
                    token,
                    new { ParentId = command.DefaultParentId });
            }

            var nullabilityChanged = master.Parent.Required != required;
            master.Parent.Required = required;
            master.Parent.OnParentDelete = onDelete;
            if (nullabilityChanged)
            {
                await RebuildIndexesAsync(connection, transaction, master, token, recreate: false);
                await ExecuteAsync(
                    connection,
                    transaction,
                    MasterScripts.AlterColumn(master, new StoredColumn
                    {
                        Name = master.Parent.ColumnName,
                        DataType = "bigint",
                        Nullable = !required
                    }),
                    token);
                await RebuildIndexesAsync(connection, transaction, master, token);
            }

            await connection.ExecuteAsync(Command(
                """
                UPDATE dbo.ctl_MasterParent
                SET IsRequired = @IsRequired, OnParentDelete = @OnParentDelete
                WHERE MasterId = @MasterId
                """,
                new { master.MasterId, IsRequired = required, OnParentDelete = onDelete },
                transaction,
                token));
            await RegenerateAsync(connection, transaction, master, token);
            await RegenerateStoredAsync(connection, transaction, master.Parent.MasterName, token);
            return ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, ct), cancellationToken);
    }

    public Task<ServiceResult<MasterDefinitionDto>> RemoveParentAsync(string masterName, CancellationToken cancellationToken = default)
    {
        return Locked(ct => ChangeAsync(masterName, async (connection, transaction, master, token) =>
        {
            if (master.Parent is null)
            {
                return ServiceResult<MasterDefinitionDto>.NotFound($"Master '{master.MasterName}' has no parent.");
            }

            var parentName = master.Parent.MasterName;
            await ExecuteAsync(connection, transaction, MasterScripts.DropForeignKeyIfExists(master), token);
            await ExecuteAsync(connection, transaction, MasterScripts.DropIndexIfExists(MasterScripts.ParentIndex(master.MasterName, master.Parent.ColumnName), MasterScripts.Table(master.MasterName)), token);
            foreach (var column in master.Columns.Where(column => column.Unique))
            {
                await ExecuteAsync(connection, transaction, MasterScripts.DropIndexIfExists(MasterScripts.UniqueIndex(master.MasterName, column.Name), MasterScripts.Table(master.MasterName)), token);
            }

            await ExecuteAsync(connection, transaction, MasterScripts.DropColumn(master, master.Parent.ColumnName), token);
            master.Parent = null;
            foreach (var column in master.Columns.Where(column => column.Unique))
            {
                await ExecuteAsync(connection, transaction, MasterScripts.CreateUniqueIndex(master, column), token);
            }

            await connection.ExecuteAsync(Command(
                "DELETE FROM dbo.ctl_MasterParent WHERE MasterId = @MasterId",
                new { master.MasterId },
                transaction,
                token));
            await RegenerateAsync(connection, transaction, master, token);
            await RegenerateStoredAsync(connection, transaction, parentName, token);
            return ServiceResult<MasterDefinitionDto>.Ok(ToDto(master));
        }, ct), cancellationToken);
    }

    public Task<ServiceResult> DeleteDefinitionAsync(string masterName, CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            if (!MasterSqlNames.IsMaster(masterName))
            {
                return InvalidMaster();
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var master = await LoadAsync(connection, null, masterName, cancellationToken);
            if (master is null)
            {
                return ServiceResult.NotFound($"Master '{masterName}' was not found.");
            }

            var children = await LoadChildrenAsync(connection, null, master.MasterId, cancellationToken);
            if (children.Count > 0)
            {
                return ServiceResult.Conflict($"{master.MasterName} is a parent of {string.Join(", ", children.Select(child => child.MasterName))}.");
            }

            var parentName = master.Parent?.MasterName;
            return await InTransaction(connection, async (transaction, token) =>
            {
                foreach (var script in MasterScripts.DropProcedures(master.MasterName))
                {
                    await ExecuteAsync(connection, transaction, script, token);
                }

                await ExecuteAsync(connection, transaction, MasterScripts.DropTable(master.MasterName), token);
                await connection.ExecuteAsync(Command(
                    """
                    DELETE FROM dbo.ctl_MasterParent WHERE MasterId = @MasterId;
                    DELETE FROM dbo.ctl_MasterColumn WHERE MasterId = @MasterId;
                    DELETE FROM dbo.ctl_Master WHERE MasterId = @MasterId;
                    """,
                    new { master.MasterId },
                    transaction,
                    token));

                if (parentName is not null)
                {
                    await RegenerateStoredAsync(connection, transaction, parentName, token);
                }

                return ServiceResult.Ok();
            }, cancellationToken);
        }, cancellationToken);
    }

    public Task<ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ListRecordsAsync(
        string masterName,
        long? parentId,
        CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            if (!MasterSqlNames.IsMaster(masterName))
            {
                return ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>.BadRequest("Master name is invalid.");
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var master = await LoadAsync(connection, null, masterName, cancellationToken);
            if (master is null)
            {
                return ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>.NotFound($"Master '{masterName}' was not found.");
            }

            if (parentId is not null && master.Parent is null)
            {
                return ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>.BadRequest($"{master.MasterName} has no parent.");
            }

            var parameters = new DynamicParameters();
            if (master.Parent is not null)
            {
                parameters.Add(master.Parent.ColumnName, parentId);
            }

            return await QueryRowsAsync(connection, master, "GetAll", parameters, cancellationToken);
        }, cancellationToken);
    }

    public Task<ServiceResult<IReadOnlyDictionary<string, object?>>> GetRecordAsync(
        string masterName,
        long id,
        CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            if (id <= 0)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.BadRequest("Id must be greater than zero.");
            }

            if (!MasterSqlNames.IsMaster(masterName))
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.BadRequest("Master name is invalid.");
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var master = await LoadAsync(connection, null, masterName, cancellationToken);
            if (master is null)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.NotFound($"Master '{masterName}' was not found.");
            }

            var parameters = new DynamicParameters();
            parameters.Add(MasterScripts.Key(master.MasterName), id);
            var rows = await QueryRowsAsync(connection, master, "GetById", parameters, cancellationToken);
            if (!rows.Succeeded)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.Fail(rows.StatusCode, rows.Message ?? "Request failed.", rows.Errors);
            }

            return rows.Data is { Count: > 0 }
                ? ServiceResult<IReadOnlyDictionary<string, object?>>.Ok(rows.Data[0])
                : ServiceResult<IReadOnlyDictionary<string, object?>>.NotFound($"{master.MasterName} was not found.");
        }, cancellationToken);
    }

    public Task<ServiceResult<IReadOnlyDictionary<string, object?>>> SaveRecordAsync(
        string masterName,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            if (!MasterSqlNames.IsMaster(masterName))
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.BadRequest("Master name is invalid.");
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var master = await LoadAsync(connection, null, masterName, cancellationToken);
            if (master is null)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.NotFound($"Master '{masterName}' was not found.");
            }

            var bound = MasterRecordBinder.Bind(master, values);
            if (!bound.Succeeded || bound.Data is null)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.Fail(
                    bound.StatusCode,
                    bound.Message ?? "Validation failed.",
                    bound.Errors);
            }

            var duplicate = await FindDuplicateAsync(connection, master, bound.Data.Parameters, cancellationToken);
            if (duplicate is not null)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.Conflict(duplicate);
            }

            var rows = await QueryRowsAsync(connection, master, "Save", bound.Data.Parameters, cancellationToken);
            if (!rows.Succeeded)
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.Fail(rows.StatusCode, rows.Message ?? "Request failed.", rows.Errors);
            }

            if (rows.Data is not { Count: > 0 })
            {
                return ServiceResult<IReadOnlyDictionary<string, object?>>.NotFound($"{master.MasterName} was not found.");
            }

            return bound.Data.IsInsert
                ? ServiceResult<IReadOnlyDictionary<string, object?>>.Created(rows.Data[0], $"{master.MasterName} created successfully.")
                : ServiceResult<IReadOnlyDictionary<string, object?>>.Ok(rows.Data[0]);
        }, cancellationToken);
    }

    private static bool MustStayUnique(StoredColumn column)
    {
        var text = column.DataType is "nvarchar" or "varchar";
        return column.Unique || (text && !column.Nullable && !column.IsMax);
    }

    private static async Task<string?> FindDuplicateAsync(
        SqlConnection connection,
        StoredMaster master,
        DynamicParameters parameters,
        CancellationToken cancellationToken)
    {
        var key = MasterScripts.Key(master.MasterName);
        var id = parameters.Get<long>(key);
        var table = MasterScripts.Quote(MasterScripts.Table(master.MasterName));
        foreach (var column in master.Columns.Where(MustStayUnique))
        {
            var value = parameters.Get<object?>(column.Name);
            if (value is null || value is string text && string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var compare = column.DataType is "nvarchar" or "varchar"
                ? $"LOWER(LTRIM(RTRIM({MasterScripts.Quote(column.Name)}))) = LOWER(LTRIM(RTRIM(@DuplicateValue)))"
                : $"{MasterScripts.Quote(column.Name)} = @DuplicateValue";
            var parentSql = master.Parent is null
                ? string.Empty
                : $" AND ((@DuplicateParent IS NULL AND {MasterScripts.Quote(master.Parent.ColumnName)} IS NULL) OR {MasterScripts.Quote(master.Parent.ColumnName)} = @DuplicateParent)";
            var query = new DynamicParameters();
            query.Add("DuplicateId", id);
            query.Add("DuplicateValue", value);
            if (master.Parent is not null)
            {
                query.Add("DuplicateParent", parameters.Get<object?>(master.Parent.ColumnName));
            }

            var count = await connection.ExecuteScalarAsync<long>(Command(
                $"""
                SELECT COUNT_BIG(1)
                FROM dbo.{table}
                WHERE IsActive = 1
                  AND {MasterScripts.Quote(key)} <> @DuplicateId
                  AND {compare}{parentSql}
                """,
                query,
                null,
                cancellationToken));
            if (count > 0)
            {
                return $"{column.Name} already exists.";
            }
        }

        return null;
    }

    public Task<ServiceResult> DeleteRecordAsync(string masterName, long id, CancellationToken cancellationToken = default)
    {
        return Locked(async cancellationToken =>
        {
            if (id <= 0)
            {
                return ServiceResult.BadRequest("Id must be greater than zero.");
            }

            if (!MasterSqlNames.IsMaster(masterName))
            {
                return ServiceResult.BadRequest("Master name is invalid.");
            }

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureAsync(connection, cancellationToken);
            var master = await LoadAsync(connection, null, masterName, cancellationToken);
            if (master is null)
            {
                return ServiceResult.NotFound($"Master '{masterName}' was not found.");
            }

            try
            {
                var parameters = new DynamicParameters();
                parameters.Add(MasterScripts.Key(master.MasterName), id);
                var affected = await connection.QuerySingleAsync<int>(Command(
                    MasterScripts.Procedure(master.MasterName, "Delete"),
                    parameters,
                    null,
                    cancellationToken,
                    CommandType.StoredProcedure));

                return affected > 0
                    ? ServiceResult.Ok()
                    : ServiceResult.NotFound($"{master.MasterName} was not found.");
            }
            catch (SqlException exception)
            {
                return MapSql(exception);
            }
        }, cancellationToken);
    }

    private async Task<ServiceResult<MasterDefinitionDto>> ChangeAsync(
        string masterName,
        Func<SqlConnection, IDbTransaction, StoredMaster, CancellationToken, Task<ServiceResult<MasterDefinitionDto>>> change,
        CancellationToken cancellationToken)
    {
        if (!MasterSqlNames.IsMaster(masterName))
        {
            return InvalidMaster<MasterDefinitionDto>();
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureAsync(connection, cancellationToken);
        var master = await LoadAsync(connection, null, masterName, cancellationToken);
        if (master is null)
        {
            return ServiceResult<MasterDefinitionDto>.NotFound($"Master '{masterName}' was not found.");
        }

        return await InTransaction(
            connection,
            (transaction, token) => change(connection, transaction, master, token),
            cancellationToken);
    }

    private async Task<T> InTransaction<T>(
        SqlConnection connection,
        Func<IDbTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(transaction, cancellationToken);
            if (result is ServiceResult serviceResult && !serviceResult.Succeeded)
            {
                await RollbackAsync(transaction, cancellationToken);
                return result;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException exception) when (exception.Number > 0 && exception.Class < 20)
        {
            await RollbackAsync(transaction, cancellationToken);
            return ToResult<T>(MapSql(exception));
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private static async Task<ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> QueryRowsAsync(
        SqlConnection connection,
        StoredMaster master,
        string action,
        DynamicParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await connection.QueryAsync(Command(
                MasterScripts.Procedure(master.MasterName, action),
                parameters,
                null,
                cancellationToken,
                CommandType.StoredProcedure));

            var list = new List<IReadOnlyDictionary<string, object?>>();
            foreach (var row in rows)
            {
                list.Add(ToDictionary((object)row));
            }

            return ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>.Ok(list);
        }
        catch (SqlException exception)
        {
            var mapped = MapSql(exception);
            return ServiceResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>.Fail(
                mapped.StatusCode,
                mapped.Message ?? "Request failed.");
        }
    }

    private static Dictionary<string, object?> ToDictionary(object row)
    {
        var source = (IDictionary<string, object>)row;
        var result = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value is DBNull ? null : pair.Value;
        }

        return result;
    }

    private static async Task EnsureAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(EnsureSql, null, null, cancellationToken));
    }

    private static async Task<StoredMaster?> LoadAsync(
        SqlConnection connection,
        IDbTransaction? transaction,
        string masterName,
        CancellationToken cancellationToken)
    {
        var header = await connection.QuerySingleOrDefaultAsync<MasterHeader>(Command(
            """
            SELECT MasterId, MasterName, UniqueIdPrefix, MenuHeading, MenuSubHeading
            FROM dbo.ctl_Master
            WHERE MasterName = @MasterName
            """,
            new { MasterName = masterName },
            transaction,
            cancellationToken));

        if (header is null)
        {
            return null;
        }

        var columns = (await connection.QueryAsync<StoredColumn>(Command(
            """
            SELECT
                ColumnName AS [Name],
                DataType,
                Length,
                IsMax,
                PrecisionValue AS [Precision],
                ScaleValue AS [Scale],
                IsNullable AS [Nullable],
                IsUnique AS [Unique],
                SortOrder
            FROM dbo.ctl_MasterColumn
            WHERE MasterId = @MasterId
            ORDER BY SortOrder, ColumnName
            """,
            new { header.MasterId },
            transaction,
            cancellationToken))).ToList();

        var parent = await connection.QuerySingleOrDefaultAsync<StoredParent>(Command(
            """
            SELECT
                link.ParentMasterId,
                parent.MasterName,
                link.ColumnName,
                link.IsRequired AS [Required],
                link.OnParentDelete
            FROM dbo.ctl_MasterParent link
            INNER JOIN dbo.ctl_Master parent ON parent.MasterId = link.ParentMasterId
            WHERE link.MasterId = @MasterId
            """,
            new { header.MasterId },
            transaction,
            cancellationToken));

        return new StoredMaster
        {
            MasterId = header.MasterId,
            MasterName = header.MasterName,
            UniqueIdPrefix = header.UniqueIdPrefix,
            MenuHeading = header.MenuHeading,
            MenuSubHeading = header.MenuSubHeading,
            Columns = columns,
            Parent = parent
        };
    }

    private static async Task<IReadOnlyList<ChildReference>> LoadChildrenAsync(
        SqlConnection connection,
        IDbTransaction? transaction,
        long masterId,
        CancellationToken cancellationToken)
    {
        var children = await connection.QueryAsync<ChildReference>(Command(
            """
            SELECT child.MasterName, link.ColumnName, link.OnParentDelete
            FROM dbo.ctl_MasterParent link
            INNER JOIN dbo.ctl_Master child ON child.MasterId = link.MasterId
            WHERE link.ParentMasterId = @MasterId
            ORDER BY child.MasterName
            """,
            new { MasterId = masterId },
            transaction,
            cancellationToken));

        return children.ToList();
    }

    private static async Task RegenerateAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster master,
        CancellationToken cancellationToken)
    {
        var children = await LoadChildrenAsync(connection, transaction, master.MasterId, cancellationToken);
        foreach (var script in MasterScripts.Procedures(master, children))
        {
            await ExecuteAsync(connection, transaction, script, cancellationToken);
        }
    }

    private static async Task RegenerateStoredAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string masterName,
        CancellationToken cancellationToken)
    {
        var master = await LoadAsync(connection, transaction, masterName, cancellationToken);
        if (master is not null)
        {
            await RegenerateAsync(connection, transaction, master, cancellationToken);
        }
    }

    private static async Task InsertColumnAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        long masterId,
        StoredColumn column,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(
            """
            INSERT INTO dbo.ctl_MasterColumn
            (
                MasterId, ColumnName, DataType, Length, IsMax, PrecisionValue, ScaleValue, IsNullable, IsUnique, SortOrder
            )
            VALUES
            (
                @MasterId, @Name, @DataType, @Length, @IsMax, @Precision, @Scale, @Nullable, @Unique, @SortOrder
            )
            """,
            new
            {
                MasterId = masterId,
                column.Name,
                column.DataType,
                column.Length,
                column.IsMax,
                column.Precision,
                column.Scale,
                column.Nullable,
                column.Unique,
                column.SortOrder
            },
            transaction,
            cancellationToken));
    }

    private static async Task UpdateColumnAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        long masterId,
        StoredColumn column,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(
            """
            UPDATE dbo.ctl_MasterColumn
            SET
                DataType = @DataType,
                Length = @Length,
                IsMax = @IsMax,
                PrecisionValue = @Precision,
                ScaleValue = @Scale,
                IsNullable = @Nullable,
                IsUnique = @Unique
            WHERE MasterId = @MasterId AND ColumnName = @Name
            """,
            new
            {
                MasterId = masterId,
                column.Name,
                column.DataType,
                column.Length,
                column.IsMax,
                column.Precision,
                column.Scale,
                column.Nullable,
                column.Unique
            },
            transaction,
            cancellationToken));
    }

    private static async Task InsertParentAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster master,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(
            """
            INSERT INTO dbo.ctl_MasterParent (MasterId, ParentMasterId, ColumnName, IsRequired, OnParentDelete)
            VALUES (@MasterId, @ParentMasterId, @ColumnName, @Required, @OnParentDelete)
            """,
            new
            {
                master.MasterId,
                master.Parent!.ParentMasterId,
                master.Parent.ColumnName,
                master.Parent.Required,
                master.Parent.OnParentDelete
            },
            transaction,
            cancellationToken));
    }

    private static async Task RebuildIndexesAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster master,
        CancellationToken cancellationToken,
        bool includeParent = true,
        bool recreate = true)
    {
        foreach (var column in master.Columns.Where(column => column.Unique))
        {
            await ExecuteAsync(
                connection,
                transaction,
                MasterScripts.DropIndexIfExists(MasterScripts.UniqueIndex(master.MasterName, column.Name), MasterScripts.Table(master.MasterName)),
                cancellationToken);
        }

        if (includeParent && master.Parent is not null)
        {
            await ExecuteAsync(
                connection,
                transaction,
                MasterScripts.DropIndexIfExists(MasterScripts.ParentIndex(master.MasterName, master.Parent.ColumnName), MasterScripts.Table(master.MasterName)),
                cancellationToken);
            await ExecuteAsync(connection, transaction, MasterScripts.DropForeignKeyIfExists(master), cancellationToken);
        }

        if (!recreate)
        {
            return;
        }

        foreach (var column in master.Columns.Where(column => column.Unique))
        {
            await ExecuteAsync(connection, transaction, MasterScripts.CreateUniqueIndex(master, column), cancellationToken);
        }

        if (includeParent && master.Parent is not null)
        {
            await ExecuteAsync(connection, transaction, MasterScripts.CreateParentIndex(master), cancellationToken);
            await ExecuteAsync(connection, transaction, MasterScripts.CreateForeignKey(master), cancellationToken);
        }
    }

    private async Task<string?> ParentErrorAsync(
        SqlConnection connection,
        IDbTransaction? transaction,
        string childName,
        string parentName,
        CancellationToken cancellationToken)
    {
        if (childName.Equals(parentName, StringComparison.OrdinalIgnoreCase))
        {
            return "A master cannot be its own parent.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = parentName;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (!seen.Add(current))
            {
                return "Parent relationship is circular.";
            }

            if (current.Equals(childName, StringComparison.OrdinalIgnoreCase))
            {
                return "A master cannot be a child of its own descendant.";
            }

            var master = await LoadAsync(connection, transaction, current, cancellationToken);
            if (master is null)
            {
                return $"Parent master '{parentName}' was not found.";
            }

            current = master.Parent?.MasterName ?? string.Empty;
        }

        return null;
    }

    private static async Task<string?> HasBlockingDataAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster master,
        StoredColumn current,
        StoredColumn updated,
        CancellationToken cancellationToken)
    {
        var table = MasterScripts.Quote(MasterScripts.Table(master.MasterName));
        var column = MasterScripts.Quote(current.Name);
        if (!current.Nullable && updated.Nullable)
        {
            // widening nullability needs no data check
        }
        else if (!updated.Nullable)
        {
            var nulls = await connection.ExecuteScalarAsync<long>(Command(
                $"SELECT COUNT_BIG(1) FROM dbo.{table} WHERE {column} IS NULL",
                null,
                transaction,
                cancellationToken));
            if (nulls > 0)
            {
                return $"{current.Name} still has empty values, so it cannot be required.";
            }
        }

        if (IsNarrowerText(current, updated))
        {
            var longest = await connection.ExecuteScalarAsync<int?>(Command(
                $"SELECT MAX(LEN({column})) FROM dbo.{table}",
                null,
                transaction,
                cancellationToken));
            if (updated.Length is int limit && longest > limit)
            {
                return $"{current.Name} has a value longer than {limit}.";
            }
        }

        if (current.DataType == "decimal" && (updated.Precision < current.Precision || updated.Scale < current.Scale))
        {
            var unfit = await connection.ExecuteScalarAsync<long>(Command(
                $"""
                SELECT COUNT_BIG(1)
                FROM dbo.{table}
                WHERE {column} IS NOT NULL
                  AND (
                        TRY_CAST({column} AS DECIMAL({updated.Precision},{updated.Scale})) IS NULL
                        OR TRY_CAST({column} AS DECIMAL({updated.Precision},{updated.Scale})) <> {column}
                      )
                """,
                null,
                transaction,
                cancellationToken));
            if (unfit > 0)
            {
                return $"{current.Name} has values that do not fit DECIMAL({updated.Precision},{updated.Scale}).";
            }
        }

        if (updated.Unique && !current.Unique)
        {
            var parentGroup = master.Parent is null
                ? string.Empty
                : $"{MasterScripts.Quote(master.Parent.ColumnName)}, ";
            var duplicates = await connection.ExecuteScalarAsync<long>(Command(
                $"""
                SELECT COUNT_BIG(1)
                FROM
                (
                    SELECT 1 AS RowExists
                    FROM dbo.{table}
                    WHERE IsActive = 1 AND {column} IS NOT NULL
                    GROUP BY {parentGroup}{column}
                    HAVING COUNT_BIG(1) > 1
                ) duplicates
                """,
                null,
                transaction,
                cancellationToken));
            if (duplicates > 0)
            {
                return $"{current.Name} has duplicate values, so it cannot be unique.";
            }
        }

        return null;
    }

    private static async Task<long> CountRowsAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster master,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<long>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.{MasterScripts.Quote(MasterScripts.Table(master.MasterName))}",
            null,
            transaction,
            cancellationToken));
    }

    private static async Task<long> CountNullsAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster master,
        string columnName,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<long>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.{MasterScripts.Quote(MasterScripts.Table(master.MasterName))} WHERE {MasterScripts.Quote(columnName)} IS NULL",
            null,
            transaction,
            cancellationToken));
    }

    private static async Task<long> ActiveParentCountAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        StoredMaster parent,
        long parentId,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<long>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.{MasterScripts.Quote(MasterScripts.Table(parent.MasterName))} WHERE {MasterScripts.Quote(MasterScripts.Key(parent.MasterName))} = @ParentId AND IsActive = 1",
            new { ParentId = parentId },
            transaction,
            cancellationToken));
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection connection,
        IDbTransaction? transaction,
        string masterName,
        CancellationToken cancellationToken)
    {
        var objectId = await connection.ExecuteScalarAsync<int?>(Command(
            "SELECT OBJECT_ID(@Name, N'U')",
            new { Name = "dbo." + MasterScripts.Table(masterName) },
            transaction,
            cancellationToken));
        return objectId is not null;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        object? parameters = null)
    {
        await connection.ExecuteAsync(Command(sql, parameters, transaction, cancellationToken));
    }

    private static CommandDefinition Command(
        string? sql,
        object? parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken,
        CommandType? commandType = null)
    {
        return new CommandDefinition(sql ?? string.Empty, parameters, transaction, commandTimeout: 120, commandType: commandType, cancellationToken: cancellationToken);
    }

    private async Task<T> Locked<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static StoredColumn ToColumn(MasterColumnInput input, int sortOrder)
    {
        var dataType = MasterColumnRules.NormalizeType(input.DataType);
        return new StoredColumn
        {
            Name = input.Name.Trim(),
            DataType = dataType,
            IsMax = input.Length?.IsMax == true,
            Length = input.Length is { IsMax: false } ? input.Length.Value.Size : null,
            Precision = dataType == "decimal" ? input.Precision : null,
            Scale = dataType == "decimal" ? input.Scale : null,
            Nullable = input.Nullable,
            Unique = input.Unique,
            SortOrder = sortOrder
        };
    }

    private static StoredParent ToParent(StoredMaster parent, MasterParentInput input)
    {
        return new StoredParent
        {
            ParentMasterId = parent.MasterId,
            MasterName = parent.MasterName,
            ColumnName = parent.MasterName + "Id",
            Required = input.Required,
            OnParentDelete = MasterSqlNames.NormalizeParentDelete(input.OnParentDelete)
        };
    }

    private static string? ValidateColumnList(string masterName, IReadOnlyList<StoredColumn> columns, string? parentName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var key = MasterScripts.Key(masterName);
        var parentColumn = string.IsNullOrWhiteSpace(parentName) ? null : parentName.Trim() + "Id";
        foreach (var column in columns)
        {
            var ruleError = MasterColumnRules.Validate(new MasterColumnInput
            {
                Name = column.Name,
                DataType = column.DataType,
                Length = column.IsMax ? new SqlLength { IsMax = true } : column.Length is null ? null : new SqlLength { Size = column.Length },
                Precision = column.Precision,
                Scale = column.Scale,
                Nullable = column.Nullable,
                Unique = column.Unique
            });
            if (ruleError is not null)
            {
                return ruleError;
            }

            if (!names.Add(column.Name))
            {
                return $"Column '{column.Name}' is duplicated.";
            }

            if (MasterColumnRules.ReservedColumnNames.Contains(column.Name) || column.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return $"Column '{column.Name}' is reserved.";
            }

            if (parentColumn is not null && column.Name.Equals(parentColumn, StringComparison.OrdinalIgnoreCase))
            {
                return $"Column '{column.Name}' is reserved for the parent link.";
            }
        }

        return null;
    }

    private static bool NeedsAlter(StoredColumn current, StoredColumn updated)
    {
        return current.DataType != updated.DataType
            || current.IsMax != updated.IsMax
            || current.Length != updated.Length
            || current.Precision != updated.Precision
            || current.Scale != updated.Scale
            || current.Nullable != updated.Nullable;
    }

    private static bool IsNarrowerText(StoredColumn current, StoredColumn updated)
    {
        if (current.DataType is not ("nvarchar" or "varchar") || updated.IsMax)
        {
            return false;
        }

        return current.IsMax || (updated.Length is int next && current.Length is int previous && next < previous);
    }

    private static void CopyColumn(StoredColumn target, StoredColumn source)
    {
        target.DataType = source.DataType;
        target.Length = source.Length;
        target.IsMax = source.IsMax;
        target.Precision = source.Precision;
        target.Scale = source.Scale;
        target.Nullable = source.Nullable;
        target.Unique = source.Unique;
    }

    private static MasterDefinitionDto ToDto(StoredMaster master)
    {
        return new MasterDefinitionDto(
            master.MasterName,
            master.UniqueIdPrefix,
            MasterScripts.Table(master.MasterName),
            master.Parent is null
                ? null
                : new MasterParentDto(master.Parent.MasterName, master.Parent.ColumnName, master.Parent.Required, master.Parent.OnParentDelete),
            master.Columns
                .OrderBy(column => column.SortOrder)
                .Select(column => new MasterColumnDto(
                    column.Name,
                    column.DataType,
                    column.Length,
                    column.IsMax,
                    column.Precision,
                    column.Scale,
                    column.Nullable,
                    column.Unique))
                .ToList(),
            string.IsNullOrWhiteSpace(master.MenuHeading) ? null : master.MenuHeading,
            string.IsNullOrWhiteSpace(master.MenuSubHeading) ? null : master.MenuSubHeading);
    }

    private static ServiceResult<MasterDefinitionDto> MissingColumn(StoredMaster master, string columnName)
    {
        return ServiceResult<MasterDefinitionDto>.NotFound($"Column '{columnName}' was not found on {master.MasterName}.");
    }

    private static ServiceResult<T> InvalidMaster<T>() => ServiceResult<T>.BadRequest("Master name is invalid.");

    private static ServiceResult InvalidMaster() => ServiceResult.BadRequest("Master name is invalid.");

    private static ServiceResult<MasterDefinitionDto> ParentFailure(string message)
    {
        return message.Contains("was not found", StringComparison.Ordinal)
            ? ServiceResult<MasterDefinitionDto>.NotFound(message)
            : ServiceResult<MasterDefinitionDto>.BadRequest(message);
    }

    private static ServiceResult MapSql(SqlException exception)
    {
        if (exception.Number is 50040 or 50060)
        {
            return ServiceResult.BadRequest(exception.Message);
        }

        if (exception.Number >= 50000 || exception.Number is 2601 or 2627 or 547)
        {
            return ServiceResult.Conflict(exception.Number >= 50000
                ? exception.Message
                : "A record with the same value already exists.");
        }

        if (exception.Number > 0 && exception.Class < 20)
        {
            return ServiceResult.BadRequest(exception.Message);
        }

        throw exception;
    }

    private static T ToResult<T>(ServiceResult failure)
    {
        if (typeof(T) == typeof(ServiceResult))
        {
            return (T)(object)failure;
        }

        var fail = typeof(T).GetMethod(
            nameof(ServiceResult.Fail),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly,
            [typeof(int), typeof(string), typeof(IReadOnlyDictionary<string, string[]>)])!;

        return (T)fail.Invoke(null, [failure.StatusCode, failure.Message ?? "Request failed.", null])!;
    }

    private static async Task RollbackAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is DbTransaction dbTransaction)
        {
            try
            {
                await dbTransaction.RollbackAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private sealed class MasterHeader
    {
        public long MasterId { get; set; }

        public string MasterName { get; set; } = string.Empty;

        public string UniqueIdPrefix { get; set; } = string.Empty;

        public string? MenuHeading { get; set; }

        public string? MenuSubHeading { get; set; }
    }

}
