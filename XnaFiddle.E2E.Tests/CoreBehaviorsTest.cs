using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Broadened e2e coverage (issue #112): pins ~10-15 core user-visible behaviors so a regression
/// in any of them fails CI. Not aiming for 100% coverage — aiming for "confident the basics work."
/// Harness (host, browser, per-test fresh page) lives in <see cref="E2ETestBase"/>; foundation
/// boot/restart smoke tests live in <see cref="SmokeTest"/>.
///
/// Cuts from the issue's candidate list, with reasons (no silent drops):
///   - #13 (asset via file input): the app has no file &lt;input&gt; — assets load via drag-drop
///     (un-synthesizable in headless) or URL fetch (needs network). Left to manual/file-loading.
///   - #14 (shader .fx compile): downloads ~17 MB DXC wasm and is slow; excluded to keep the e2e
///     job's wall-clock sane, as the issue permits. Shader logic is unit-tested elsewhere.
///   - #15 (export .zip download): lowest value (can't validate the zip builds in e2e).
/// Tests 1-12 are all implemented.
/// </summary>
[TestFixture]
public sealed class CoreBehaviorsTest : E2ETestBase
{
    // A minimal valid HiDef game: clears to a solid color each frame. Distinct clear colors give
    // distinct source (and thus a distinct CompileFingerprint) for the cache-hit vs recompile test.
    private const string HiDefGameBlue = @"using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyGame : Game
{
    public MyGame()
    {
        var g = new GraphicsDeviceManager(this);
        g.GraphicsProfile = GraphicsProfile.HiDef;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);
    }
}
";

    private const string HiDefGameBlack = @"using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyGame : Game
{
    public MyGame()
    {
        var g = new GraphicsDeviceManager(this);
        g.GraphicsProfile = GraphicsProfile.HiDef;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        base.Draw(gameTime);
    }
}
";

    // Reach (WebGL1) counterpart of the HiDef game, for the profile-switch test.
    private const string ReachGame = @"using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyGame : Game
{
    public MyGame()
    {
        var g = new GraphicsDeviceManager(this);
        g.GraphicsProfile = GraphicsProfile.Reach;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);
    }
}
";

    // ---- A. Compile / run core ----

    // Test 1: an invalid program surfaces a clean failure — status "Compilation failed.", no game
    // starts (context signal stays null), and the Blazor circuit survives (error banner hidden).
    [Test]
    public async Task CompileError_SurfacesCleanlyAndStartsNoGame()
    {
        await BootAsync();

        await SetEditorValueAsync("this is not valid C# @@@");
        await ClickRunAsync();

        await Page.GetByText("Compilation failed.").WaitForAsync(
            new LocatorWaitForOptions { Timeout = RunTimeoutMs });

        await AssertNoBlazorErrorAsync("after a compile error");
        string? ctx = await Page.EvaluateAsync<string?>("() => window._canvasContextType ?? null");
        Assert.That(ctx, Is.Null, "no game should start when compilation fails");
    }

    // Test 2: Stop clears the running game — status "Stopped." and the Run/Restart button reverts
    // to "Run" (label toggles on _game != null).
    [Test]
    public async Task Stop_ClearsRunningGame()
    {
        await BootAsync();

        await SetEditorValueAsync(HiDefGameBlue);
        await ClickRunAsync();
        await WaitForWebGlContextAsync();

        await Page.ClickAsync("[data-testid=\"stop-button\"]");
        await Page.GetByText("Stopped.").WaitForAsync(new LocatorWaitForOptions { Timeout = RunTimeoutMs });

        string runLabel = (await Page.Locator("[data-testid=\"run-button\"]").InnerTextAsync()).Trim();
        Assert.That(runLabel, Does.Contain("Run").And.Not.Contain("Restart"),
            "with the game stopped the button should read Run, not Restart");
        await AssertNoBlazorErrorAsync("after Stop");
    }

    // Test 3: first Run compiles ("Compiled in Xs"); an unchanged re-Run is a cache hit ("Restarted
    // without recompile"); editing the source forces a recompile again. Pins the CompileFingerprint.
    [Test]
    public async Task EditThenRerun_RecompilesVersusCacheHit()
    {
        await BootAsync();

        await SetEditorValueAsync(HiDefGameBlue);
        await ClickRunAsync();
        await WaitForDiagnosticsContainsAsync("Compiled in");
        await WaitForWebGlContextAsync();

        // Unchanged source -> cache hit.
        await ResetCanvasContextAsync();
        await ClickRunAsync();
        await WaitForDiagnosticsContainsAsync("Restarted without recompile");
        await WaitForWebGlContextAsync();

        // Edit the source -> recompiles.
        await SetEditorValueAsync(HiDefGameBlack);
        await ResetCanvasContextAsync();
        await ClickRunAsync();
        await WaitForDiagnosticsContainsAsync("Compiled in");
        await WaitForWebGlContextAsync();

        await AssertNoBlazorErrorAsync("after edit-then-rerun");
    }

    // ---- B. Examples / graphics profile ----

    // Test 4: picking an example from the Examples modal loads its code and runs it (core of #95).
    [Test]
    public async Task LoadExample_FromModal_LoadsCodeAndRuns()
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        ILocator firstCard = Page.Locator("[data-testid=\"example-card\"]").First;
        string? name = await firstCard.GetAttributeAsync("data-example-name");
        Assert.That(name, Is.Not.Null.And.Not.Empty, "an example card should expose its name");

        await ResetCanvasContextAsync();
        await firstCard.ClickAsync();
        await WaitForWebGlContextAsync();

        string buttonLabel = await Page.Locator("[data-testid=\"examples-button\"]").InnerTextAsync();
        Assert.That(buttonLabel, Does.Contain(name!), "the Examples button should show the loaded example's name");
        string code = await GetEditorValueAsync();
        Assert.That(code, Is.Not.Empty, "the example's code should be loaded into the editor");
        await AssertNoBlazorErrorAsync("after loading an example");
    }

    // Test 5 (full #95): A -> B -> A example round-trip stays alive. After each switch the running
    // game is asserted live via the GetInputDebugState debug hook — a deterministic input/state
    // assertion the DOM can't express (see e2e-testing skill boundary), not a flaky pixel read.
    [Test]
    public async Task SampleSwitch_RoundTrip_A_B_A_StaysAlive()
    {
        await BootAsync();

        // Capture two distinct example names from the default category.
        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        ILocator cards = Page.Locator("[data-testid=\"example-card\"]");
        int count = await cards.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(2), "need at least two examples to round-trip");
        string nameA = (await cards.Nth(0).GetAttributeAsync("data-example-name"))!;
        string nameB = (await cards.Nth(1).GetAttributeAsync("data-example-name"))!;
        // Close the modal without selecting (Escape-equivalent: click a card selects+runs, so use A).
        await cards.Nth(0).ClickAsync();
        await WaitForWebGlContextAsync();
        await AssertGameRunningAsync("after selecting A");

        await SelectExampleAndRunAsync(nameB);
        await AssertGameRunningAsync("after selecting B");

        await SelectExampleAndRunAsync(nameA);
        await AssertGameRunningAsync("after returning to A");

        await AssertNoBlazorErrorAsync("after A->B->A round-trip");
    }

    // Test 6 (issue #25 canvas-swap path): switching a Reach game and a HiDef game flips the WebGL
    // context type webgl <-> webgl2. Historically fragile because a canvas locks its context type on
    // first getContext, so a profile change must swap in a fresh canvas element.
    [Test]
    public async Task ReachHiDefProfileSwitch_FlipsContextTypeAndRuns()
    {
        await BootAsync();

        await SetEditorValueAsync(ReachGame);
        await ResetCanvasContextAsync();
        await ClickRunAsync();
        await WaitForCanvasContextAsync("webgl");

        await SetEditorValueAsync(HiDefGameBlue);
        await ClickRunAsync();
        await WaitForCanvasContextAsync("webgl2");

        await SetEditorValueAsync(ReachGame);
        await ClickRunAsync();
        await WaitForCanvasContextAsync("webgl");

        await AssertNoBlazorErrorAsync("after Reach<->HiDef switches");
    }

    // ---- C. Share / persistence (URL round-trip) ----

    // Test 7: the Share dialog's #code= URL round-trips through the real app — reload at that URL
    // reloads the same code and runs it. Exercises UrlCodec end-to-end through the app wiring.
    [Test]
    public async Task ShareCode_HashRoundTrip_ReloadsSameCodeAndRuns()
    {
        await BootAsync();
        string original = await GetEditorValueAsync();

        await Page.ClickAsync("[data-testid=\"share-button\"]");
        string shareUrl = await Page.Locator("[data-testid=\"share-url-input\"]").InputValueAsync();
        Assert.That(shareUrl, Does.Contain("#code="), "code-mode share URL should carry a #code= fragment");

        await BootFreshAsync(FragmentUrl(shareUrl));
        await WaitForWebGlContextAsync();

        string reloaded = await GetEditorValueAsync();
        Assert.That(Normalize(reloaded), Is.EqualTo(Normalize(original)),
            "reloading the #code= URL should restore the identical source");
        await AssertNoBlazorErrorAsync("after #code= reload");
    }

    // Test 8: the Share dialog's #snippet= URL round-trips through SnippetReverter/SnippetExpander
    // and the app wiring — navigating to it loads and runs a game.
    [Test]
    public async Task ShareSnippet_HashRoundTrip_LoadsAndRuns()
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"share-button\"]");
        await Page.ClickAsync("[data-testid=\"share-snippet-toggle\"]");
        string shareUrl = await Page.Locator("[data-testid=\"share-url-input\"]").InputValueAsync();
        Assert.That(shareUrl, Does.Contain("#snippet="), "snippet-mode share URL should carry a #snippet= fragment");

        await BootFreshAsync(FragmentUrl(shareUrl));
        await WaitForWebGlContextAsync();
        await AssertNoBlazorErrorAsync("after #snippet= reload");
    }

    // Test 9: a cold-boot deep link to a #code= payload loads and auto-runs with no manual click.
    // The payload is built with the real UrlCodec, so this pins the decode + auto-compile path.
    [Test]
    public async Task ColdBoot_CodeDeepLink_LoadsAndAutoRuns()
    {
        const string marker = "COLD_BOOT_MARKER_112";
        string code = "// " + marker + "\n" + HiDefGameBlue;
        string payload = UrlCodec.Encode(code);

        await BootAsync(BaseUrl + "/#code=" + payload);
        await WaitForWebGlContextAsync();

        string loaded = await GetEditorValueAsync();
        Assert.That(loaded, Does.Contain(marker), "the deep-linked code should be loaded into the editor");
        await AssertNoBlazorErrorAsync("after cold-boot deep link");
    }

    // ---- D. Editor / IntelliSense (browser-only Roslyn) ----

    // Test 10: Monaco boots with non-empty default code in a mounted editor. Cheap, high value.
    [Test]
    public async Task Monaco_BootsWithDefaultCodeVisible()
    {
        await BootAsync();

        Assert.That(await Page.Locator("#monacoContainer").CountAsync(), Is.GreaterThan(0),
            "the Monaco container should be mounted");
        string code = await GetEditorValueAsync();
        Assert.That(code, Is.Not.Empty, "the editor should boot with the default sample code");
    }

    // Test 11: the in-browser Roslyn completion service returns results. Calls the GetCompletionsAsync
    // JSInvokable directly (via the intellisense DotNetObjectReference) rather than driving the fragile
    // Monaco completion widget, per the issue.
    [Test]
    public async Task Intellisense_Completion_ReturnsResults()
    {
        await BootAsync();

        // Roslyn warmup runs shortly after boot and takes a few seconds; wait for the ready flag.
        await Page.WaitForFunctionAsync(
            "() => window.monacoInterop && window.monacoInterop._isIntellisenseReady === true",
            null,
            new PageWaitForFunctionOptions { Timeout = RunTimeoutMs, PollingInterval = 250 });

        int count = await Page.EvaluateAsync<int>(@"async () => {
            const source = 'using System; class C { void M() { Console. } }';
            const offset = source.indexOf('Console.') + 'Console.'.length;
            const items = await window.monacoInterop._intellisenseRef.invokeMethodAsync('GetCompletionsAsync', source, offset);
            return items ? items.length : 0;
        }");
        Assert.That(count, Is.GreaterThan(0), "completion after 'Console.' should return members");
    }

    // ---- E. Embed mode ----

    // Test 12: ?embed=true hides the editor panel and (with a #code= deep link) runs the game so the
    // canvas fills the viewport. Pins the embed layout + that embed auto-runs a deep link.
    [Test]
    public async Task EmbedMode_HidesEditorAndRunsGame()
    {
        string payload = UrlCodec.Encode(HiDefGameBlue);
        await BootAsync(BaseUrl + "/?embed=true#code=" + payload);
        await WaitForWebGlContextAsync();

        Assert.That(await Page.Locator("#editorPanel").CountAsync(), Is.EqualTo(0),
            "embed mode should not render the editor panel");
        Assert.That(await Page.Locator("[data-testid=\"game-canvas\"]").IsVisibleAsync(), Is.True,
            "the game canvas should be visible in embed mode");
        await AssertNoBlazorErrorAsync("in embed mode");
    }

    // ---- helpers ----

    // Opens the Examples modal, nulls the context signal (all examples are HiDef so the type doesn't
    // flip — nulling is what makes the wait prove THIS switch ran), selects the named example, and
    // waits for the fresh run to re-set the context.
    private async Task SelectExampleAndRunAsync(string exampleName)
    {
        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await ResetCanvasContextAsync();
        await Page.ClickAsync($"[data-testid=\"example-card\"][data-example-name=\"{exampleName}\"]");
        await WaitForWebGlContextAsync();
    }

    private async Task AssertGameRunningAsync(string when)
    {
        bool running = await Page.EvaluateAsync<bool>(@"async () => {
            const s = await window.theInstance.invokeMethodAsync('GetInputDebugState');
            return s.gameRunning;
        }");
        Assert.That(running, Is.True, $"the game should be running ({when})");
    }

    // Rebases a captured share URL (host is hard-coded to xnafiddle.net) onto the in-process test
    // host, keeping only the fragment so the round-trip navigates against the served app.
    private string FragmentUrl(string shareUrl)
    {
        int hash = shareUrl.IndexOf('#');
        Assert.That(hash, Is.GreaterThanOrEqualTo(0), "share URL should contain a fragment");
        return BaseUrl + "/" + shareUrl.Substring(hash);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");
}
