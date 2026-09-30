using System.Diagnostics;//
using Eisai.Web.Theme;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ThemeStore>();
builder.Services.AddHttpClient("EisaiApi", client =>
{
    var apiBase = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5218";
    client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
});

var app = builder.Build();
var ownedApi = await ApiHost.EnsureStartedAsync(app);
app.Lifetime.ApplicationStopping.Register(() => ApiHost.Stop(ownedApi));

app.UseStaticFiles();
app.UseRouting();
app.Map("/api/{**remainder}", async (HttpContext context, IHttpClientFactory clients, string? remainder) =>
{
    var client = clients.CreateClient("EisaiApi");
    var target = "api/" + (remainder ?? string.Empty) + context.Request.QueryString;
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
    if (context.Request.ContentLength is > 0)
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrEmpty(context.Request.ContentType))
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }
    }

    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;
    var contentType = response.Content.Headers.ContentType?.ToString();
    if (!string.IsNullOrEmpty(contentType))
    {
        context.Response.ContentType = contentType;
    }

    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static class ApiHost
{
    public static async Task<Process?> EnsureStartedAsync(WebApplication app)
    {
        var apiBase = (app.Configuration["ApiBaseUrl"] ?? "http://localhost:5218").TrimEnd('/');
        if (await IsUpAsync(apiBase))
        {
            return null;
        }

        var exe = Path.GetFullPath(Path.Combine(
            app.Environment.ContentRootPath,
            "..",
            "Eisai",
            "Eisai.Api",
            "bin",
            "Debug",
            "net10.0",
            "Eisai.Api.exe"));
        if (!File.Exists(exe))
        {
            return null;
        }

        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = apiBase;
        var process = Process.Start(start);
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsUpAsync(apiBase))
            {
                return process;
            }

            if (process is { HasExited: true })
            {
                return null;
            }

            await Task.Delay(300);
        }

        return process;
    }

    public static void Stop(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<bool> IsUpAsync(string apiBase)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync($"{apiBase}/api/masters");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
