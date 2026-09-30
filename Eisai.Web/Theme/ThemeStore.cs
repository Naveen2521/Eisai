using System.Text.Json;

namespace Eisai.Web.Theme;

public sealed class ThemeStore
{
    public const string DefaultLoaderUrl = "/images/Loader.gif";
    public const string LoaderFileName = "loader.gif";
    public const string ExpandedLogoFileName = "logo-expanded.png";
    public const string CollapsedLogoFileName = "logo-collapsed.png";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions MenuJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly string _uploadDirectory;
    private readonly object _gate = new();
    private ThemeSettings _current;

    public ThemeStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _settingsPath = Path.Combine(dataDirectory, "theme-settings.json");
        _uploadDirectory = Path.Combine(environment.WebRootPath, "uploads", "theme");
        Directory.CreateDirectory(_uploadDirectory);
        _current = Read();
    }

    public ThemeSettings Current
    {
        get
        {
            lock (_gate)
            {
                return Copy(_current);
            }
        }
    }

    public string MenuOrderJson => JsonSerializer.Serialize(Current.MenuOrder, MenuJson);

    public static List<MenuOrderItem>? ParseOrder(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<MenuOrderItem>>(json, MenuJson);
            return items is null ? null : Sanitize(items, 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(ThemeSettings settings)
    {
        var copy = Copy(settings);
        var json = JsonSerializer.Serialize(copy, JsonOptions);
        lock (_gate)
        {
            File.WriteAllText(_settingsPath, json);
            _current = copy;
        }
    }

    public async Task<string> SaveFileAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_uploadDirectory);
        var path = Path.Combine(_uploadDirectory, fileName);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return "/uploads/theme/" + fileName + "?v=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private ThemeSettings Read()
    {
        if (!File.Exists(_settingsPath))
        {
            return new ThemeSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            return settings is null ? new ThemeSettings() : Copy(settings);
        }
        catch (JsonException)
        {
            return new ThemeSettings();
        }
    }

    private static ThemeSettings Copy(ThemeSettings source)
    {
        var name = (source.ApplicationName ?? "").Trim();
        var subtitle = (source.Subtitle ?? "").Trim();
        return new ThemeSettings
        {
            ApplicationName = name.Length == 0 ? "DAR" : name,
            Subtitle = subtitle.Length == 0 ? "Safety & Compliance" : subtitle,
            LoaderWidth = Math.Clamp(source.LoaderWidth, 40, 400),
            LoaderHeight = Math.Clamp(source.LoaderHeight, 40, 400),
            LoaderUrl = SafeLoader(source.LoaderUrl),
            ExpandedLogoUrl = SafeLogo(source.ExpandedLogoUrl, ExpandedLogoFileName),
            CollapsedLogoUrl = SafeLogo(source.CollapsedLogoUrl, CollapsedLogoFileName),
            CollapseEnabled = source.CollapseEnabled,
            MenuOrder = Sanitize(source.MenuOrder, 0)
        };
    }

    private static List<MenuOrderItem> Sanitize(IEnumerable<MenuOrderItem>? items, int depth)
    {
        if (items is null || depth > 4)
        {
            return [];
        }

        var result = new List<MenuOrderItem>();
        foreach (var item in items)
        {
            if (item is null || result.Count >= 200)
            {
                break;
            }

            var id = (item.Id ?? "").Trim();
            if (id.Length == 0 || id.Length > 200)
            {
                continue;
            }

            var label = (item.Label ?? "").Trim();
            if (label.Length > 80)
            {
                label = label[..80];
            }

            result.Add(new MenuOrderItem
            {
                Id = id,
                Label = label.Length == 0 ? null : label,
                Children = Sanitize(item.Children, depth + 1)
            });
        }

        return result;
    }

    private static string SafeLoader(string? url)
    {
        if (Matches(url, DefaultLoaderUrl) || Matches(url, "/uploads/theme/" + LoaderFileName))
        {
            return url!;
        }

        return DefaultLoaderUrl;
    }

    private static string? SafeLogo(string? url, string fileName)
    {
        return Matches(url, "/uploads/theme/" + fileName) ? url : null;
    }

    private static bool Matches(string? url, string path)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var bare = url.Split('?')[0];
        return bare.Equals(path, StringComparison.OrdinalIgnoreCase);
    }
}
