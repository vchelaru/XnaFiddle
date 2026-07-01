using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Serves the published standalone Blazor WASM app from an in-process Kestrel server.
/// UseBlazorFrameworkFiles sets the Blazor-specific content types (notably application/wasm
/// for the Webcil assemblies and dotnet.native.wasm) that a plain static-file server would
/// serve as octet-stream, which the browser refuses to instantiate. Binding to port 0 lets
/// the OS pick a free port so parallel/CI runs never collide.
/// </summary>
public sealed class StaticSiteHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }

    private StaticSiteHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<StaticSiteHost> StartAsync(string webRoot)
    {
        WebApplication app = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = webRoot,
        }).Build();

        // Port 0 => OS-assigned free port; the real address is read back after Start.
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        // Deep links (and "/") fall back to index.html so the WASM router boots.
        app.MapFallbackToFile("index.html");

        await app.StartAsync();

        IServerAddressesFeature addresses =
            app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose a server address.");
        string baseUrl = addresses.Addresses.First();

        return new StaticSiteHost(app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
