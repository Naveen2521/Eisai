using System.Text;

namespace Eisai.Infrastructure.Persistence.Masters;

internal static class MasterScripts
{
    public static string Quote(string name) => "[" + name + "]";

    public static string Table(string masterName) => "mst_" + masterName;

    public static string Key(string masterName) => masterName + "Id";

    public static string Procedure(string masterName, string action) => $"dbo.sp_Mst_{masterName}_{action}";

    public static string UniqueIndex(string masterName, string columnName) => $"UX_mst_{masterName}_{columnName}";

    public static string UniqueIdIndex(string masterName) => $"UX_mst_{masterName}_UniqueId";

    public static string ParentIndex(string masterName, string columnName) => $"IX_mst_{masterName}_{columnName}";

    public static string ForeignKey(string masterName, string parentName) => $"FK_mst_{masterName}_{parentName}";

    public static string SqlType(StoredColumn column)
    {
        return column.DataType switch
        {
            "nvarchar" => column.IsMax ? "NVARCHAR(MAX)" : $"NVARCHAR({column.Length})",
            "varchar" => column.IsMax ? "VARCHAR(MAX)" : $"VARCHAR({column.Length})",
            "int" => "INT",
            "bigint" => "BIGINT",
            "bit" => "BIT",
            "decimal" => $"DECIMAL({column.Precision},{column.Scale})",
            "datetime" => "DATETIME2",
            "uniqueidentifier" => "UNIQUEIDENTIFIER",
            _ => throw new InvalidOperationException($"Unsupported data type '{column.DataType}'.")
        };
    }

    public static string CreateTable(StoredMaster master)
    {
        var table = Table(master.MasterName);
        var lines = new List<string>
        {
            $"{Quote(Key(master.MasterName))} BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT {Quote("PK_" + table)} PRIMARY KEY",
            "UniqueId NVARCHAR(100) NOT NULL"
        };

        if (master.Parent is not null)
        {
            lines.Add($"{Quote(master.Parent.ColumnName)} BIGINT {(master.Parent.Required ? "NOT NULL" : "NULL")}");
        }

        lines.AddRange(master.Columns.OrderBy(column => column.SortOrder).Select(ColumnDefinition));
        lines.Add($"IsActive BIT NOT NULL CONSTRAINT {Quote("DF_" + table + "_IsActive")} DEFAULT (1)");
        lines.Add("CreatedBy BIGINT NOT NULL");
        lines.Add($"CreatedOn DATETIME2 NOT NULL CONSTRAINT {Quote("DF_" + table + "_CreatedOn")} DEFAULT (SYSUTCDATETIME())");
        lines.Add("UpdatedBy BIGINT NULL");
        lines.Add("UpdatedOn DATETIME2 NULL");

        return $"""
            CREATE TABLE dbo.{Quote(table)}
            (
                {string.Join("," + Environment.NewLine + "    ", lines)}
            );
            """;
    }

    public static IReadOnlyList<string> CreateIndexes(StoredMaster master)
    {
        var scripts = new List<string>
        {
            DropIndexIfExists(UniqueIdIndex(master.MasterName), Table(master.MasterName)),
            $"""
            CREATE UNIQUE INDEX {Quote(UniqueIdIndex(master.MasterName))}
                ON dbo.{Quote(Table(master.MasterName))} (UniqueId)
                WHERE IsActive = 1;
            """
        };

        if (master.Parent is not null)
        {
            scripts.Add(CreateParentIndex(master));
            scripts.Add(CreateForeignKey(master));
        }

        scripts.AddRange(master.Columns.Where(column => column.Unique).Select(column => CreateUniqueIndex(master, column)));
        return scripts;
    }

    public static string CreateUniqueIndex(StoredMaster master, StoredColumn column)
    {
        var keys = master.Parent is null
            ? Quote(column.Name)
            : $"{Quote(master.Parent.ColumnName)}, {Quote(column.Name)}";

        return $"""
            CREATE UNIQUE INDEX {Quote(UniqueIndex(master.MasterName, column.Name))}
                ON dbo.{Quote(Table(master.MasterName))} ({keys})
                WHERE IsActive = 1;
            """;
    }

    public static string CreateParentIndex(StoredMaster master)
    {
        return $"""
            CREATE INDEX {Quote(ParentIndex(master.MasterName, master.Parent!.ColumnName))}
                ON dbo.{Quote(Table(master.MasterName))} ({Quote(master.Parent.ColumnName)});
            """;
    }

    public static string CreateForeignKey(StoredMaster master)
    {
        var parent = master.Parent!;
        return $"""
            ALTER TABLE dbo.{Quote(Table(master.MasterName))}
            ADD CONSTRAINT {Quote(ForeignKey(master.MasterName, parent.MasterName))}
            FOREIGN KEY ({Quote(parent.ColumnName)})
            REFERENCES dbo.{Quote(Table(parent.MasterName))} ({Quote(Key(parent.MasterName))});
            """;
    }

    public static string DropIndexIfExists(string indexName, string tableName)
    {
        return $"""
            IF EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'{indexName}'
                  AND object_id = OBJECT_ID(N'dbo.{tableName}')
            )
            DROP INDEX {Quote(indexName)} ON dbo.{Quote(tableName)};
            """;
    }

    public static string DropForeignKeyIfExists(StoredMaster master)
    {
        var parent = master.Parent!;
        var constraint = ForeignKey(master.MasterName, parent.MasterName);
        return $"""
            IF OBJECT_ID(N'dbo.{constraint}', N'F') IS NOT NULL
                ALTER TABLE dbo.{Quote(Table(master.MasterName))} DROP CONSTRAINT {Quote(constraint)};
            """;
    }

    public static string AddColumn(StoredMaster master, StoredColumn column)
    {
        return $"""
            ALTER TABLE dbo.{Quote(Table(master.MasterName))}
            ADD {ColumnDefinition(column)};
            """;
    }

    public static string AlterColumn(StoredMaster master, StoredColumn column)
    {
        return $"""
            ALTER TABLE dbo.{Quote(Table(master.MasterName))}
            ALTER COLUMN {ColumnDefinition(column)};
            """;
    }

    public static string DropColumn(StoredMaster master, string columnName)
    {
        return $"""
            ALTER TABLE dbo.{Quote(Table(master.MasterName))}
            DROP COLUMN {Quote(columnName)};
            """;
    }

    public static IReadOnlyList<string> Procedures(StoredMaster master, IReadOnlyList<ChildReference> children)
    {
        return
        [
            GetAll(master),
            GetById(master),
            Save(master),
            Delete(master, children)
        ];
    }

    public static IReadOnlyList<string> DropProcedures(string masterName)
    {
        return
        [
            $"DROP PROCEDURE IF EXISTS {Procedure(masterName, "GetAll")};",
            $"DROP PROCEDURE IF EXISTS {Procedure(masterName, "GetById")};",
            $"DROP PROCEDURE IF EXISTS {Procedure(masterName, "Save")};",
            $"DROP PROCEDURE IF EXISTS {Procedure(masterName, "Delete")};"
        ];
    }

    public static string DropTable(string masterName)
    {
        return $"DROP TABLE IF EXISTS dbo.{Quote(Table(masterName))};";
    }

    private static string GetAll(StoredMaster master)
    {
        var parent = master.Parent;
        var signature = parent is null ? string.Empty : $"\n    @{parent.ColumnName} BIGINT = NULL";
        var filter = parent is null
            ? string.Empty
            : $"\n      AND (@{parent.ColumnName} IS NULL OR {Quote(parent.ColumnName)} = @{parent.ColumnName})";
        var order = Quote(master.Columns.OrderBy(column => column.SortOrder).First().Name);

        return $"""
            CREATE OR ALTER PROCEDURE {Procedure(master.MasterName, "GetAll")}{signature}
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    {SelectList(master)}
                FROM dbo.{Quote(Table(master.MasterName))}
                WHERE IsActive = 1{filter}
                ORDER BY {order};
            END
            """;
    }

    private static string GetById(StoredMaster master)
    {
        var key = Key(master.MasterName);
        return $"""
            CREATE OR ALTER PROCEDURE {Procedure(master.MasterName, "GetById")}
                @{key} BIGINT
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    {SelectList(master)}
                FROM dbo.{Quote(Table(master.MasterName))}
                WHERE {Quote(key)} = @{key}
                  AND IsActive = 1;
            END
            """;
    }

    private static string Save(StoredMaster master)
    {
        var key = Key(master.MasterName);
        var table = Quote(Table(master.MasterName));
        var builder = new StringBuilder();
        builder.AppendLine($"CREATE OR ALTER PROCEDURE {Procedure(master.MasterName, "Save")}");
        builder.AppendLine($"    @{key} BIGINT,");

        if (master.Parent is not null)
        {
            var parentDefault = master.Parent.Required ? string.Empty : " = NULL";
            builder.AppendLine($"    @{master.Parent.ColumnName} BIGINT{parentDefault},");
        }

        foreach (var column in master.Columns.OrderBy(column => column.SortOrder))
        {
            var optional = column.Nullable ? " = NULL" : string.Empty;
            builder.AppendLine($"    @{column.Name} {SqlType(column)}{optional},");
        }

        builder.AppendLine("    @IsActive BIT = 1,");
        builder.AppendLine("    @CreatedBy BIGINT = NULL,");
        builder.AppendLine("    @UpdatedBy BIGINT = NULL");
        builder.AppendLine("AS");
        builder.AppendLine("BEGIN");
        builder.AppendLine("    SET NOCOUNT ON;");
        builder.AppendLine("    SET XACT_ABORT ON;");
        builder.AppendLine();

        foreach (var column in master.Columns.Where(column => column.DataType is "nvarchar" or "varchar"))
        {
            builder.AppendLine($"    SET @{column.Name} = LTRIM(RTRIM(@{column.Name}));");
        }

        builder.AppendLine();
        builder.AppendLine($"    IF @{key} = 0");
        builder.AppendLine("    BEGIN");
        builder.AppendLine("        IF @CreatedBy IS NULL OR @CreatedBy <= 0");
        builder.AppendLine("            THROW 50060, 'CreatedBy is required.', 1;");
        builder.AppendLine();
        builder.Append(Checks(master, key));
        builder.AppendLine("        BEGIN TRANSACTION;");
        builder.AppendLine($"        INSERT INTO dbo.{table}");
        builder.AppendLine("        (");
        builder.AppendLine("            UniqueId,");
        if (master.Parent is not null)
        {
            builder.AppendLine($"            {Quote(master.Parent.ColumnName)},");
        }

        foreach (var column in master.Columns.OrderBy(column => column.SortOrder))
        {
            builder.AppendLine($"            {Quote(column.Name)},");
        }

        builder.AppendLine("            IsActive,");
        builder.AppendLine("            CreatedBy");
        builder.AppendLine("        )");
        builder.AppendLine("        VALUES");
        builder.AppendLine("        (");
        builder.AppendLine("            N'TMP-' + CONVERT(NVARCHAR(36), NEWID()),");
        if (master.Parent is not null)
        {
            builder.AppendLine($"            @{master.Parent.ColumnName},");
        }

        foreach (var column in master.Columns.OrderBy(column => column.SortOrder))
        {
            builder.AppendLine($"            @{column.Name},");
        }

        builder.AppendLine("            @IsActive,");
        builder.AppendLine("            @CreatedBy");
        builder.AppendLine("        );");
        builder.AppendLine();
        builder.AppendLine($"        SET @{key} = CONVERT(BIGINT, SCOPE_IDENTITY());");
        builder.AppendLine($"        UPDATE dbo.{table}");
        builder.AppendLine($"        SET UniqueId = N'{master.UniqueIdPrefix}-' + RIGHT(N'0000' + CONVERT(NVARCHAR(20), @{key}), 4)");
        builder.AppendLine($"        WHERE {Quote(key)} = @{key};");
        builder.AppendLine("        COMMIT TRANSACTION;");
        builder.AppendLine("    END");
        builder.AppendLine("    ELSE");
        builder.AppendLine("    BEGIN");
        builder.AppendLine($"        IF NOT EXISTS (SELECT 1 FROM dbo.{table} WHERE {Quote(key)} = @{key} AND IsActive = 1)");
        builder.AppendLine("        BEGIN");
        builder.AppendLine("            SELECT");
        builder.AppendLine($"                {SelectList(master, 16)}");
        builder.AppendLine($"            FROM dbo.{table}");
        builder.AppendLine("            WHERE 1 = 0;");
        builder.AppendLine("            RETURN;");
        builder.AppendLine("        END");
        builder.AppendLine();
        builder.Append(Checks(master, key));
        builder.AppendLine($"        UPDATE dbo.{table}");
        builder.AppendLine("        SET");
        if (master.Parent is not null)
        {
            builder.AppendLine($"            {Quote(master.Parent.ColumnName)} = @{master.Parent.ColumnName},");
        }

        foreach (var column in master.Columns.OrderBy(column => column.SortOrder))
        {
            builder.AppendLine($"            {Quote(column.Name)} = @{column.Name},");
        }

        builder.AppendLine("            IsActive = @IsActive,");
        builder.AppendLine("            UpdatedBy = @UpdatedBy,");
        builder.AppendLine("            UpdatedOn = SYSUTCDATETIME()");
        builder.AppendLine($"        WHERE {Quote(key)} = @{key}");
        builder.AppendLine("          AND IsActive = 1;");
        builder.AppendLine("    END");
        builder.AppendLine();
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {SelectList(master, 8)}");
        builder.AppendLine($"    FROM dbo.{table}");
        builder.AppendLine($"    WHERE {Quote(key)} = @{key};");
        builder.AppendLine("END");
        return builder.ToString();
    }

    private static string Delete(StoredMaster master, IReadOnlyList<ChildReference> children)
    {
        var key = Key(master.MasterName);
        var builder = new StringBuilder();
        builder.AppendLine($"CREATE OR ALTER PROCEDURE {Procedure(master.MasterName, "Delete")}");
        builder.AppendLine($"    @{key} BIGINT");
        builder.AppendLine("AS");
        builder.AppendLine("BEGIN");
        builder.AppendLine("    SET NOCOUNT ON;");
        builder.AppendLine();

        var errorNumber = 50050;
        foreach (var child in children)
        {
            if (string.Equals(child.OnParentDelete, "restrict", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"    IF EXISTS (SELECT 1 FROM dbo.{Quote(Table(child.MasterName))} WHERE {Quote(child.ColumnName)} = @{key} AND IsActive = 1)");
                builder.AppendLine($"        THROW {errorNumber}, '{master.MasterName} is in use by {child.MasterName}.', 1;");
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine($"    UPDATE dbo.{Quote(Table(child.MasterName))}");
                builder.AppendLine($"    SET {Quote(child.ColumnName)} = NULL, UpdatedOn = SYSUTCDATETIME()");
                builder.AppendLine($"    WHERE {Quote(child.ColumnName)} = @{key};");
                builder.AppendLine();
            }

            errorNumber++;
        }

        builder.AppendLine($"    UPDATE dbo.{Quote(Table(master.MasterName))}");
        builder.AppendLine("    SET");
        builder.AppendLine("        IsActive = 0,");
        builder.AppendLine("        UpdatedOn = SYSUTCDATETIME()");
        builder.AppendLine($"    WHERE {Quote(key)} = @{key}");
        builder.AppendLine("      AND IsActive = 1;");
        builder.AppendLine();
        builder.AppendLine("    SELECT @@ROWCOUNT;");
        builder.AppendLine("END");
        return builder.ToString();
    }

    private static string Checks(StoredMaster master, string key)
    {
        var builder = new StringBuilder();
        if (master.Parent is not null)
        {
            builder.AppendLine($"        IF @{master.Parent.ColumnName} IS NOT NULL");
            builder.AppendLine("        BEGIN");
            builder.AppendLine($"            IF NOT EXISTS (SELECT 1 FROM dbo.{Quote(Table(master.Parent.MasterName))} WHERE {Quote(Key(master.Parent.MasterName))} = @{master.Parent.ColumnName} AND IsActive = 1)");
            builder.AppendLine($"                THROW 50040, '{master.Parent.MasterName} was not found.', 1;");
            builder.AppendLine("        END");
            builder.AppendLine();
        }

        var errorNumber = 50001;
        foreach (var column in master.Columns.Where(column => column.Unique).OrderBy(column => column.SortOrder))
        {
            var parentMatch = string.Empty;
            if (master.Parent is not null)
            {
                parentMatch = master.Parent.Required
                    ? $"\n                  AND {Quote(master.Parent.ColumnName)} = @{master.Parent.ColumnName}"
                    : $"\n                  AND ((@{master.Parent.ColumnName} IS NULL AND {Quote(master.Parent.ColumnName)} IS NULL) OR {Quote(master.Parent.ColumnName)} = @{master.Parent.ColumnName})";
            }

            builder.AppendLine("        IF EXISTS");
            builder.AppendLine("        (");
            builder.AppendLine("            SELECT 1");
            builder.AppendLine($"            FROM dbo.{Quote(Table(master.MasterName))}");
            builder.AppendLine($"            WHERE {Quote(column.Name)} = @{column.Name}");
            builder.AppendLine("              AND IsActive = 1");
            builder.AppendLine($"              AND {Quote(key)} <> @{key}{parentMatch}");
            builder.AppendLine("        )");
            builder.AppendLine($"            THROW {errorNumber}, '{column.Name} already exists.', 1;");
            builder.AppendLine();
            errorNumber++;
        }

        return builder.ToString();
    }

    private static string SelectList(StoredMaster master, int indent = 12)
    {
        var columns = new List<string> { Quote(Key(master.MasterName)), "UniqueId" };
        if (master.Parent is not null)
        {
            columns.Add(Quote(master.Parent.ColumnName));
        }

        columns.AddRange(master.Columns.OrderBy(column => column.SortOrder).Select(column => Quote(column.Name)));
        columns.Add("IsActive");
        columns.Add("CreatedBy");
        columns.Add("CreatedOn");
        columns.Add("UpdatedBy");
        columns.Add("UpdatedOn");
        var padding = new string(' ', indent);
        return string.Join("," + Environment.NewLine + padding, columns);
    }

    private static string ColumnDefinition(StoredColumn column)
    {
        return $"{Quote(column.Name)} {SqlType(column)} {(column.Nullable ? "NULL" : "NOT NULL")}";
    }
}
