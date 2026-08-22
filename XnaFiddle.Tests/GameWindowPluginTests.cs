using XnaFiddle.Plugins;

namespace XnaFiddle.Tests;

// These guard the contract for GameWindowPlugin.ClearedWindowEventFields — the set of
// nkast.Wasm.Dom.Window event delegates CleanUp() nulls between runs.
//
// Testability note: the *behavior* (does mouse still work after a 2nd game?) can't be unit-tested
// here — nkast.Wasm.Dom.Window and KNI's BlazorGameWindow/ConcreteMouse are browser-only and don't
// load in net10.0, so CleanUp() no-ops in this process. (That's exactly why the issue #95 regression
// shipped green.) These tests instead pin the *contract* so the cleared set can't silently drift
// back into clearing handlers that aren't re-subscribed per game.
public class GameWindowPluginTests
{
    [Fact]
    public void ClearedWindowEventFields_match_the_per_game_resubscribed_set()
    {
        // Exactly the delegates BlazorGameWindow's ctor re-subscribes on every game.
        Assert.Equal(
            new[]
            {
                "OnTouchStart", "OnTouchMove", "OnTouchEnd", "OnTouchCancel",
                "OnKeyDown", "OnKeyUp", "OnFocus", "OnBlur",
            },
            GameWindowPlugin.ClearedWindowEventFields);
    }

    // Issue #95 regression guard: mouse and gamepad are subscribed ONCE (ConcreteMouse
    // .PlatformSetWindowHandle / ConcreteGamePad, gated on the first game's Mouse.WindowHandle) and
    // never re-subscribed. Nulling them orphans input permanently after the first restart. They must
    // never appear in the cleared set.
    [Theory]
    [InlineData("OnMouseMove")]
    [InlineData("OnMouseDown")]
    [InlineData("OnMouseUp")]
    [InlineData("OnMouseWheel")]
    [InlineData("OnGamepadConnected")]
    [InlineData("OnGamepadDisconnected")]
    [InlineData("OnResize")]
    public void ClearedWindowEventFields_never_include_subscribe_once_handlers(string field)
    {
        Assert.DoesNotContain(field, GameWindowPlugin.ClearedWindowEventFields);
    }

    // ClearCanvasElementCache() clears KNI's Document._elementsCache — see its doc comment for why
    // this must be called only on an actual canvas swap, never on every restart via CleanUp().
    // Same testability note as above: nkast.Wasm.Dom.Window is browser-only and absent under
    // net10.0, so this just pins that the method itself is safe to call (idempotent, swallows the
    // reflection miss) — not the browser-only behavior it reflects into.
    [Fact]
    public void ClearCanvasElementCache_DoesNotThrow()
    {
        var plugin = new GameWindowPlugin();

        plugin.ClearCanvasElementCache();
        plugin.ClearCanvasElementCache();
    }
}
