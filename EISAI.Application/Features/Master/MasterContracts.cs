using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eisai.Application.Features.Master;

[JsonConverter(typeof(SqlLengthJsonConverter))]
public readonly struct SqlLength
{
    public int? Size { get; init; }

    public bool IsMax { get; init; }

    public override string ToString()
    {
        return IsMax ? "max" : Size?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

public sealed class SqlLengthJsonConverter : JsonConverter<SqlLength>
{
    public override SqlLength Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var size))
        {
            return new SqlLength { Size = size };
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()?.Trim();
            if (string.Equals(text, "max", StringComparison.OrdinalIgnoreCase))
            {
                return new SqlLength { IsMax = true };
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return new SqlLength { Size = parsed };
            }
        }

        throw new JsonException("Length must be a number or \"max\".");
    }

    public override void Write(Utf8JsonWriter writer, SqlLength value, JsonSerializerOptions options)
    {
        if (value.IsMax)
        {
            writer.WriteStringValue("max");
            return;
        }

        writer.WriteNumberValue(value.Size ?? 0);
    }
}

public sealed class MasterColumnInput
{
    public string Name { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public SqlLength? Length { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public bool Nullable { get; init; }

    public bool Unique { get; init; }
}

public sealed class MasterParentInput
{
    public string MasterName { get; init; } = string.Empty;

    public bool Required { get; init; } = true;

    public string OnParentDelete { get; init; } = "restrict";

    public long? DefaultParentId { get; init; }
}

public sealed record MasterColumnDto(
    string Name,
    string DataType,
    int? Length,
    bool IsMax,
    int? Precision,
    int? Scale,
    bool Nullable,
    bool Unique);

public sealed record MasterParentDto(
    string MasterName,
    string ColumnName,
    bool Required,
    string OnParentDelete);

public sealed record MasterDefinitionDto(
    string MasterName,
    string UniqueIdPrefix,
    string TableName,
    MasterParentDto? Parent,
    IReadOnlyList<MasterColumnDto> Columns,
    string? MenuHeading = null,
    string? MenuSubHeading = null,
    bool TableExists = true);

public static class MasterColumnRules
{
    public static readonly HashSet<string> DataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvarchar",
        "varchar",
        "int",
        "bigint",
        "bit",
        "decimal",
        "datetime",
        "uniqueidentifier"
    };

    public static readonly HashSet<string> ReservedColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "UniqueId",
        "IsActive",
        "CreatedBy",
        "CreatedOn",
        "UpdatedBy",
        "UpdatedOn"
    };

    public static string NormalizeType(string dataType) => dataType.Trim().ToLowerInvariant();

    public static string? Validate(MasterColumnInput column)
    {
        if (!MasterSqlNames.IsColumn(column.Name))
        {
            return "Column name must start with a letter and contain only letters, numbers, and underscores (max 40).";
        }

        if (!DataTypes.Contains(column.DataType.Trim()))
        {
            return $"Data type '{column.DataType}' is not supported.";
        }

        var dataType = NormalizeType(column.DataType);
        var lengthError = ValidateLength(dataType, column.Length);
        if (lengthError is not null)
        {
            return lengthError;
        }

        if (dataType == "decimal")
        {
            if (column.Precision is null || column.Scale is null)
            {
                return "Decimal columns require precision and scale.";
            }

            if (column.Precision is < 1 or > 38 || column.Scale < 0 || column.Scale > column.Precision)
            {
                return "Decimal precision must be 1 to 38 and scale must be between 0 and precision.";
            }
        }
        else if (column.Precision is not null || column.Scale is not null)
        {
            return "Precision and scale are only allowed for decimal columns.";
        }

        if (column.Unique && IsUnlimitedText(dataType, column.Length))
        {
            return "A max text column cannot be unique. Set a length, or turn unique off.";
        }

        if (column.Unique && dataType is "nvarchar" or "varchar")
        {
            var size = column.Length?.Size ?? 0;
            var limit = dataType == "nvarchar" ? 400 : 800;
            if (size > limit)
            {
                return $"A unique {dataType} column cannot be longer than {limit}.";
            }
        }

        return null;
    }

    public static string? ValidateLength(string dataType, SqlLength? length)
    {
        var isText = dataType is "nvarchar" or "varchar";
        if (!isText)
        {
            return length is null
                ? null
                : "Length is only allowed for nvarchar and varchar columns.";
        }

        if (length is null)
        {
            return "nvarchar and varchar columns require a length, as a number or \"max\".";
        }

        if (length.Value.IsMax)
        {
            return null;
        }

        var size = length.Value.Size ?? 0;
        var max = dataType == "nvarchar" ? 4000 : 8000;
        return size is >= 1 && size <= max
            ? null
            : $"{dataType} length must be 1 to {max}, or max.";
    }

    public static bool IsSameTypeFamily(string left, string right)
    {
        return NormalizeType(left) == NormalizeType(right);
    }

    private static bool IsUnlimitedText(string dataType, SqlLength? length)
    {
        return dataType is "nvarchar" or "varchar" && length?.IsMax == true;
    }
}

public static class MasterSqlNames
{
    public static bool IsMaster(string name)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            name,
            "^[A-Za-z][A-Za-z0-9_]{0,39}$");
    }

    public static bool IsColumn(string name) => IsMaster(name);

    public static bool IsPrefix(string prefix)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(prefix, "^[A-Za-z]{2,10}$");
    }

    public static string NormalizeParentDelete(string value) => value.Trim().ToLowerInvariant();

    public static bool IsParentDelete(string value)
    {
        var normalized = NormalizeParentDelete(value);
        return normalized is "restrict" or "setnull";
    }
}
