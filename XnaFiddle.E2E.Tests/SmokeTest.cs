using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Phase 1 e2e smoke test (issue #97): boot the published Blazor WASM app under headless
/// Chromium with software GL (SwiftShader) and prove the default sample compiles (Roslyn),
/// runs (KNI), and initializes a WebGL context. This is the class of regression (#90/#95)
/// that unit tests cannot reach because the browser-only KNI/WebGL code no-ops under net8.0.
/// </summary>
[TestFixture]
public sealed class SmokeTest
{
    // Generous because the WASM payload is ~15-20 MB and Roslyn compiling in-browser is slow;
    // tune these up if CI is flaky rather than weakening the assertion.
    private const int BootTimeoutMs = 60_000;
    private const int RunTimeoutMs = 60_000;

    private StaticSiteHost _host = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _host = await StaticSiteHost.StartAsync(PublishOutput.ResolveWebRoot());

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Software GL for headless CI: ANGLE over SwiftShader. --enable-unsafe-swiftshader
            // is required on recent Chromium to allow SwiftShader for WebGL. See issue #97.
            Args =
            [
                "--use-gl=angle",
                "--use-angle=swiftshader",
                "--enable-unsafe-swiftshader",
            ],
        });
        _page = await _browser.NewPageAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Test]
    public async Task DefaultSample_BootsRunsAndInitializesWebGl()
    {
        await _page.GotoAsync(_host.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = BootTimeoutMs,
        });

        // Boot: canvas present and the render bridge wired (window.theInstance set by initRenderJS).
        await _page.WaitForSelectorAsync("#theCanvas", new PageWaitForSelectorOptions
        {
            Timeout = BootTimeoutMs,
        });
        await _page.WaitForFunctionAsync(
            "() => window.theInstance !== undefined && window.theInstance !== null",
            null,
            new PageWaitForFunctionOptions { Timeout = BootTimeoutMs, PollingInterval = 250 });

        // Run the default sample.
        await _page.ClickAsync("[data-testid=\"run-button\"]", new PageClickOptions
        {
            Timeout = BootTimeoutMs,
        });

        // Deterministic success signal: .NET sets window._canvasContextType once a game runs and
        // a WebGL context initializes ('webgl' = Reach, 'webgl2' = HiDef). Either proves that
        // Roslyn compiled + KNI ran + a GL context came up under SwiftShader.
        await _page.WaitForFunctionAsync(
            "() => window._canvasContextType === 'webgl' || window._canvasContextType === 'webgl2'",
            null,
            new PageWaitForFunctionOptions { Timeout = RunTimeoutMs, PollingInterval = 500 });

        // #blazor-error-ui is display:none via CSS unless an unhandled error surfaces.
        bool errorVisible = await _page.Locator("#blazor-error-ui").IsVisibleAsync();
        Assert.That(errorVisible, Is.False, "Blazor error banner should not be displayed.");
    }
}
