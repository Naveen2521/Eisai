namespace Eisai.Web.Theme;

public sealed class ThemeSettings
{
    public string ApplicationName { get; set; } = "Eisai";

    public string Subtitle { get; set; } = "Safety & Compliance";

    public int LoaderWidth { get; set; } = 140;

    public int LoaderHeight { get; set; } = 140;

    public string LoaderUrl { get; set; } = "/images/Loader.gif";

    public string? ExpandedLogoUrl { get; set; }

    public string? CollapsedLogoUrl { get; set; }

    public bool CollapseEnabled { get; set; } = true;

    public List<MenuOrderItem> MenuOrder { get; set; } = [];

    public string Mark
    {
        get
        {
            var letters = new string((ApplicationName ?? "").Where(char.IsLetterOrDigit).Take(3).ToArray());
            return letters.Length == 0 ? "Eisai" : letters.ToUpperInvariant();
        }
    }
}

public sealed class MenuOrderItem
{
    public string Id { get; set; } = "";

    public string? Label { get; set; }

    public List<MenuOrderItem> Children { get; set; } = [];
}
