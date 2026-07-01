using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Foundation e2e smoke tests (issue #97): boot the published Blazor WASM app under headless
/// Chromium with software GL (SwiftShader) and prove the default sample compiles (Roslyn),
/// runs (KNI), and initializes a WebGL context — plus the #90 repeated-restart guard. This is
/// the class of regression that unit tests cannot reach because the browser-only KNI/WebGL
/// code no-ops under net8.0. Harness lives in <see cref="E2ETestBase"/>. Broader behavior
/// coverage is in <see cref="CoreBehaviorsTest"/> (issue #112).
/// </summary>
[TestFixture]
public sealed class SmokeTest : E2ETestBase
{
    // Restart the game this many times with unchanged source (issue #90 guard). Each Restart
    // rebuilds the whole Game/GraphicsDevice, so this must exceed Chrome's ~16 live-WebGL-context
    // cap: if the UseReferenceDevice leak fix (see game-lifecycle skill) ever regresses, the
    // per-run OffscreenCanvas probe leak trips context loss around run ~16, which this would catch.
    private const int RestartCount = 20;

    [Test]
    public async Task DefaultSample_BootsRunsAndInitializesWebGl()
    {
        await BootAsync();

        await ClickRunAsync();
        await WaitForWebGlContextAsync();

        await AssertNoBlazorErrorAsync("after first run");
    }

    /// <summary>
    /// Issue #90 restart guard: clicking Restart repeatedly with unchanged source must not kill
    /// the Blazor circuit (error banner) or lose the WebGL context. Each iteration nulls
    /// window._canvasContextType and waits for the running game to re-set it — a deterministic,
    /// app-code-free signal that this specific restart cycled a fresh game + GL context to
    /// completion (DoCompileAndRun sets it at the end of every successful run).
    /// </summary>
    [Test]
    public async Task RepeatedRestart_UnchangedSource_StaysAliveAndKeepsWebGl()
    {
        await BootAsync();

        // First click compiles + runs; every later click is a cache-hit Restart (source unchanged).
        await ClickRunAsync();
        await WaitForWebGlContextAsync();

        for (int i = 1; i <= RestartCount; i++)
        {
            await ResetCanvasContextAsync();

            await ClickRunAsync();
            await WaitForWebGlContextAsync();

            await AssertNoBlazorErrorAsync($"on restart {i} of {RestartCount}");
        }
    }
}
