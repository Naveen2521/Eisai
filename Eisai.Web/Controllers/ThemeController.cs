using Eisai.Web.Theme;
using Microsoft.AspNetCore.Mvc;

namespace Eisai.Web.Controllers;

public sealed class ThemeController : Controller
{
    private const int MaxFileBytes = 2_000_000;

    private static readonly byte[] GifMagic = "GIF8"u8.ToArray();
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly ThemeStore _store;

    public ThemeController(ThemeStore store)
    {
        _store = store;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Theme";
        ViewData["ShowHeaderTitle"] = false;
        return View(new ThemePageModel
        {
            Settings = _store.Current,
            Saved = string.Equals(Request.Query["saved"], "1", StringComparison.Ordinal)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Index(
        int loaderWidth,
        int loaderHeight,
        bool collapseEnabled,
        IFormFile? loader,
        IFormFile? expandedLogo,
        IFormFile? collapsedLogo,
        string? menuOrder,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Theme";
        ViewData["ShowHeaderTitle"] = false;

        var errors = new List<string>();
        if (loaderWidth is < 40 or > 400 || loaderHeight is < 40 or > 400)
        {
            errors.Add("Loader width and height must be between 40 and 400 pixels.");
        }

        var loaderBytes = await ReadFileAsync(loader, ".gif", GifMagic, "Loader", errors, cancellationToken);
        var expandedBytes = await ReadFileAsync(expandedLogo, ".png", PngMagic, "Expanded logo", errors, cancellationToken);
        var collapsedBytes = await ReadFileAsync(collapsedLogo, ".png", PngMagic, "Collapsed logo", errors, cancellationToken);

        var current = _store.Current;
        var order = ThemeStore.ParseOrder(menuOrder) ?? current.MenuOrder;
        if (errors.Count > 0)
        {
            return View(new ThemePageModel
            {
                Settings = new ThemeSettings
                {
                    ApplicationName = current.ApplicationName,
                    Subtitle = current.Subtitle,
                    LoaderWidth = loaderWidth,
                    LoaderHeight = loaderHeight,
                    LoaderUrl = current.LoaderUrl,
                    ExpandedLogoUrl = current.ExpandedLogoUrl,
                    CollapsedLogoUrl = current.CollapsedLogoUrl,
                    CollapseEnabled = collapseEnabled,
                    MenuOrder = order
                },
                Errors = errors
            });
        }

        var saved = new ThemeSettings
        {
            ApplicationName = current.ApplicationName,
            Subtitle = current.Subtitle,
            LoaderWidth = loaderWidth,
            LoaderHeight = loaderHeight,
            LoaderUrl = loaderBytes is null
                ? current.LoaderUrl
                : await _store.SaveFileAsync(loaderBytes, ThemeStore.LoaderFileName, cancellationToken),
            ExpandedLogoUrl = expandedBytes is null
                ? current.ExpandedLogoUrl
                : await _store.SaveFileAsync(expandedBytes, ThemeStore.ExpandedLogoFileName, cancellationToken),
            CollapsedLogoUrl = !collapseEnabled || collapsedBytes is null
                ? current.CollapsedLogoUrl
                : await _store.SaveFileAsync(collapsedBytes, ThemeStore.CollapsedLogoFileName, cancellationToken),
            CollapseEnabled = collapseEnabled,
            MenuOrder = order
        };
        _store.Save(saved);
        return RedirectToAction(nameof(Index), new { saved = 1 });
    }

    private static async Task<byte[]?> ReadFileAsync(
        IFormFile? file,
        string extension,
        byte[] magic,
        string label,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        if (!Path.GetExtension(file.FileName).Equals(extension, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} must be a {extension} file.");
            return null;
        }

        if (file.Length > MaxFileBytes)
        {
            errors.Add($"{label} must be 2 MB or smaller.");
            return null;
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.Length < magic.Length || !bytes.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            errors.Add($"{label} is not a valid {extension} file.");
            return null;
        }

        return bytes;
    }
}
