using System.Globalization;
using Eisai.Application.Common;
using Dapper;

namespace Eisai.Infrastructure.Persistence.Masters;

internal sealed record BoundRecord(DynamicParameters Parameters, bool IsInsert);

internal static class MasterRecordBinder
{
    public static ServiceResult<BoundRecord> Bind(StoredMaster master, IReadOnlyDictionary<string, object?> values)
    {
        var source = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            source[pair.Key] = pair.Value;
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var parameters = new DynamicParameters();
        var key = MasterScripts.Key(master.MasterName);

        var id = ReadInt64(source, key, errors) ?? 0;
        if (id < 0)
        {
            Add(errors, key, "Id must be zero or greater.");
        }

        var isInsert = id == 0;
        var createdBy = ReadInt64(source, "createdBy", errors) ?? ReadInt64(source, "userId", errors);
        var updatedBy = ReadInt64(source, "updatedBy", errors);
        var actor = isInsert ? createdBy : updatedBy ?? createdBy;
        if (actor is null or <= 0)
        {
            Add(errors, isInsert ? "createdBy" : "updatedBy", isInsert
                ? "CreatedBy is required."
                : "UpdatedBy or createdBy is required.");
        }

        parameters.Add(key, id);
        parameters.Add("CreatedBy", isInsert ? actor : createdBy);
        parameters.Add("UpdatedBy", isInsert ? null : actor);
        parameters.Add("IsActive", ReadBool(source, "isActive", errors) ?? true);

        if (master.Parent is not null)
        {
            var parentValue = ReadInt64(source, master.Parent.ColumnName, errors);
            if (parentValue is null && master.Parent.Required)
            {
                Add(errors, master.Parent.ColumnName, $"{master.Parent.ColumnName} is required.");
            }
            else if (parentValue is <= 0)
            {
                Add(errors, master.Parent.ColumnName, $"{master.Parent.ColumnName} must be greater than zero.");
            }

            parameters.Add(master.Parent.ColumnName, parentValue);
        }

        foreach (var column in master.Columns)
        {
            source.TryGetValue(column.Name, out var raw);
            parameters.Add(column.Name, Coerce(column, raw, errors));
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            key,
            "uniqueId",
            "createdOn",
            "updatedOn",
            "createdBy",
            "userId",
            "updatedBy",
            "isActive"
        };

        if (master.Parent is not null)
        {
            known.Add(master.Parent.ColumnName);
        }

        foreach (var column in master.Columns)
        {
            known.Add(column.Name);
        }

        foreach (var pair in source)
        {
            if (!known.Contains(pair.Key))
            {
                Add(errors, pair.Key, "Unknown column.");
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult<BoundRecord>.Validation(errors.ToDictionary(
                pair => ToCamelCase(pair.Key),
                pair => pair.Value.Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase));
        }

        return ServiceResult<BoundRecord>.Ok(new BoundRecord(parameters, isInsert));
    }

    private static object? Coerce(StoredColumn column, object? raw, Dictionary<string, List<string>> errors)
    {
        if (raw is null)
        {
            if (!column.Nullable)
            {
                Add(errors, column.Name, $"{column.Name} is required.");
            }

            return null;
        }

        try
        {
            return column.DataType switch
            {
                "nvarchar" or "varchar" => CoerceText(column, raw, errors),
                "int" => CoerceInt(column, raw, errors),
                "bigint" => CoerceInt64(raw),
                "bit" => CoerceBit(column, raw, errors),
                "decimal" => CoerceDecimal(raw),
                "datetime" => CoerceDateTime(raw),
                "uniqueidentifier" => CoerceGuid(raw),
                _ => null
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            Add(errors, column.Name, $"{column.Name} has an invalid {column.DataType} value.");
            return null;
        }
    }

    private static string? CoerceText(StoredColumn column, object raw, Dictionary<string, List<string>> errors)
    {
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        if (!column.Nullable && text.Length == 0)
        {
            Add(errors, column.Name, $"{column.Name} is required.");
        }

        if (!column.IsMax && column.Length is int length && text.Length > length)
        {
            Add(errors, column.Name, $"{column.Name} cannot be longer than {length}.");
        }

        return text;
    }

    private static int CoerceInt(StoredColumn column, object raw, Dictionary<string, List<string>> errors)
    {
        var number = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        if (number is < int.MinValue or > int.MaxValue)
        {
            Add(errors, column.Name, $"{column.Name} is outside the range of int.");
        }

        return (int)number;
    }

    private static long CoerceInt64(object raw)
    {
        return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    private static bool CoerceBit(StoredColumn column, object raw, Dictionary<string, List<string>> errors)
    {
        return raw switch
        {
            bool value => value,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture) switch
            {
                0 => false,
                1 => true,
                _ => FailBit(column, errors)
            }
        };
    }

    private static bool FailBit(StoredColumn column, Dictionary<string, List<string>> errors)
    {
        Add(errors, column.Name, $"{column.Name} must be true or false.");
        return false;
    }

    private static decimal CoerceDecimal(object raw)
    {
        return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
    }

    private static DateTime CoerceDateTime(object raw)
    {
        if (raw is DateTime date)
        {
            return date;
        }

        if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        throw new FormatException();
    }

    private static Guid CoerceGuid(object raw)
    {
        if (raw is Guid guid)
        {
            return guid;
        }

        if (Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed))
        {
            return parsed;
        }

        throw new FormatException();
    }

    private static long? ReadInt64(Dictionary<string, object?> source, string name, Dictionary<string, List<string>> errors)
    {
        if (!source.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            Add(errors, name, $"{name} must be a whole number.");
            return null;
        }
    }

    private static bool? ReadBool(Dictionary<string, object?> source, string name, Dictionary<string, List<string>> errors)
    {
        if (!source.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        try
        {
            return raw switch
            {
                bool value => value,
                string text when bool.TryParse(text, out var parsed) => parsed,
                _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture) switch
                {
                    0 => false,
                    1 => true,
                    _ => UnknownBool(name, errors)
                }
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return UnknownBool(name, errors);
        }
    }

    private static bool? UnknownBool(string name, Dictionary<string, List<string>> errors)
    {
        Add(errors, name, $"{name} must be true or false.");
        return null;
    }

    private static void Add(Dictionary<string, List<string>> errors, string name, string message)
    {
        if (!errors.TryGetValue(name, out var messages))
        {
            messages = [];
            errors[name] = messages;
        }

        messages.Add(message);
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
