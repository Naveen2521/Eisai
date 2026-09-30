namespace Eisai.Infrastructure.Persistence.Masters;

internal sealed class StoredMaster
{
    public long MasterId { get; set; }

    public string MasterName { get; set; } = string.Empty;

    public string UniqueIdPrefix { get; set; } = string.Empty;

    public string? MenuHeading { get; set; }

    public string? MenuSubHeading { get; set; }

    public List<StoredColumn> Columns { get; set; } = [];

    public StoredParent? Parent { get; set; }
}

internal sealed class StoredColumn
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public int? Length { get; set; }

    public bool IsMax { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool Nullable { get; set; }

    public bool Unique { get; set; }

    public int SortOrder { get; set; }
}

internal sealed class StoredParent
{
    public long ParentMasterId { get; set; }

    public string MasterName { get; set; } = string.Empty;

    public string ColumnName { get; set; } = string.Empty;

    public bool Required { get; set; }

    public string OnParentDelete { get; set; } = "restrict";
}

internal sealed record ChildReference(string MasterName, string ColumnName, string OnParentDelete);
