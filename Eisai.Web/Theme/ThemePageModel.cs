namespace Eisai.Web.Theme;

public sealed class ThemePageModel
{
    public required ThemeSettings Settings { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool Saved { get; init; }
}
