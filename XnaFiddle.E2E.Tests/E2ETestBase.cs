using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Shared e2e harness (issue #97 foundation, widened in #112). Owns the expensive, reusable
/// pieces once per fixture — the in-process Kestrel host serving the published WASM app, the
/// Playwright driver, the headless Chromium browser, and a single browser context — in
/// <see cref="OneTimeSetUp"/>. Each test gets a FRESH page (<see cref="SetUp"/>/<see cref="TearDown"/>)
/// so a still-running game, a set <c>window._canvasContextType</c>, or leftover DOM can't leak
/// across tests. The context is shared so the ~15-20 MB WASM payload stays in the HTTP cache
/// between pages instead of re-downloading per test.
/// </summary>
public abstract class E2ETestBase
{
    // Generous because the WASM payload is ~15-20 MB and Roslyn compiling in-browser is slow;
    // tune these up if CI is flaky rather than weakening the assertion.
    protected const int BootTimeoutMs = 60_000;
    protected const int RunTimeoutMs = 60_000;

    private StaticSiteHost _host = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;

    protected IPage Page { get; private set; } = null!;

    protected string BaseUrl => _host.BaseUrl;

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
        _context = await _browser.NewContextAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

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

    [SetUp]
    public async Task SetUp()
    {
        Page = await _context.NewPageAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        // A fresh page per test disposes the running game and any set window globals with it.
        await Page.CloseAsync();
    }

    // ---- Shared helpers ----

    /// <summary>Navigates to the app root and waits for the boot gate.</summary>
    protected Task BootAsync() => BootAsync(BaseUrl);

    /// <summary>
    /// Navigates to an absolute URL (root, a <c>?embed=true</c> query, or a <c>#code=</c> deep
    /// link) and waits for the boot gate: the canvas present and the render bridge wired
    /// (<c>window.theInstance</c> set by initRenderJS).
    /// </summary>
    protected async Task BootAsync(string url)
    {
        await Page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = BootTimeoutMs,
        });

        await Page.WaitForSelectorAsync("#theCanvas", new PageWaitForSelectorOptions
        {
            Timeout = BootTimeoutMs,
        });
        await Page.WaitForFunctionAsync(
            "() => window.theInstance !== undefined && window.theInstance !== null",
            null,
            new PageWaitForFunctionOptions { Timeout = BootTimeoutMs, PollingInterval = 250 });
    }

    // Boots a deep link when the page is ALREADY on the served app: a browser treats a navigation
    // that differs only by #fragment as in-page (no reload), so Blazor's OnAfterRender never re-runs
    // and the #code=/#snippet= payload is ignored. Going to about:blank first guarantees the target
    // is a cross-document load. (A fresh page's first GotoAsync is already cross-document, so the
    // deep-link tests that navigate a brand-new page don't need this.)
    protected async Task BootFreshAsync(string url)
    {
        await Page.GotoAsync("about:blank");
        await BootAsync(url);
    }

    // ClickAsync auto-waits for actionability, so it naturally waits out the transient "Stop"
    // (compiling) button and clicks the Run/Restart button once it is back and enabled.
    protected Task ClickRunAsync() =>
        Page.ClickAsync("[data-testid=\"run-button\"]", new PageClickOptions
        {
            Timeout = BootTimeoutMs,
        });

    // Deterministic success signal: .NET sets window._canvasContextType once a game runs and a
    // WebGL context initializes ('webgl' = Reach, 'webgl2' = HiDef). Either proves that Roslyn
    // compiled + KNI ran + a GL context came up under SwiftShader.
    protected Task WaitForWebGlContextAsync() =>
        Page.WaitForFunctionAsync(
            "() => window._canvasContextType === 'webgl' || window._canvasContextType === 'webgl2'",
            null,
            new PageWaitForFunctionOptions { Timeout = RunTimeoutMs, PollingInterval = 500 });

    // Waits for a SPECIFIC context type ('webgl' Reach / 'webgl2' HiDef) — used by the profile-switch
    // test where the point is that the type flips, not merely that some context came up.
    protected Task WaitForCanvasContextAsync(string contextType) =>
        Page.WaitForFunctionAsync(
            $"() => window._canvasContextType === '{contextType}'",
            null,
            new PageWaitForFunctionOptions { Timeout = RunTimeoutMs, PollingInterval = 500 });

    // Clears the success signal so a following WaitForWebGlContextAsync can only pass once THIS
    // run re-sets it (the value stays set across same-profile restarts). See e2e-testing skill.
    protected Task ResetCanvasContextAsync() =>
        Page.EvaluateAsync("() => { window._canvasContextType = null; }");

    // #blazor-error-ui is display:none via CSS unless an unhandled error surfaces.
    protected async Task AssertNoBlazorErrorAsync(string when)
    {
        bool errorVisible = await Page.Locator("#blazor-error-ui").IsVisibleAsync();
        Assert.That(errorVisible, Is.False, $"Blazor error banner should not be displayed ({when}).");
    }

    // Replaces the C# editor content (targets the C# program model, not the active tab).
    protected Task SetEditorValueAsync(string code) =>
        Page.EvaluateAsync("code => window.monacoInterop.setValue(code)", code);

    protected Task<string> GetEditorValueAsync() =>
        Page.EvaluateAsync<string>("() => window.monacoInterop.getValue()");

    // Waits until the diagnostics panel text contains the given substring. The panel only renders
    // once _diagnosticsOutput is non-empty, so this doubles as "diagnostics appeared".
    protected Task WaitForDiagnosticsContainsAsync(string expected) =>
        Page.WaitForFunctionAsync(
            @"expected => {
                const el = document.querySelector('[data-testid=""diagnostics""]');
                return el != null && el.textContent.includes(expected);
            }",
            expected,
            new PageWaitForFunctionOptions { Timeout = RunTimeoutMs, PollingInterval = 250 });
}
