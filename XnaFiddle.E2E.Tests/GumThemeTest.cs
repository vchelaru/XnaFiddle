using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Regression coverage for the GumUI example's Gum.Themes.* theme picker (click a ToggleButton
/// chip). <see cref="ExampleGalleryComplianceTest"/> already proves GumUI compiles and boots,
/// but never sends input, so it never exercises the theme-switch code path: clearing/reapplying
/// <c>FrameworkElement.DefaultFormsTemplates</c> and destroying/rebuilding the Forms controls
/// panel (see GumUI.cs's <c>ApplyTheme</c>/<c>RebuildControlsPanel</c>).
///
/// KNOWN LIMITATION: synthetic Playwright clicks land at the correct viewport coordinates but
/// currently do not register as hits on Gum controls, because <c>BlazorGameWindow.ClientBounds</c>
/// computes mouse/touch position relative to the browser window instead of the canvas — broken
/// whenever the canvas doesn't fill the window (XnaFiddle's editor-pane layout always hits this).
/// The fix (KniSB commit b2c25f8c) is pinned on the not-yet-merged
/// <c>fix/kni-mouse-touch-canvas-offset</c> branch. Until that lands, this test can only assert
/// "clicking doesn't crash the game" — it cannot yet assert a theme actually switched. Once the
/// fix merges, tighten this test to assert on the resulting Diagnostics/DOM state.
/// </summary>
[TestFixture]
public sealed class GumThemeTest : E2ETestBase
{
    [Test]
    public async Task GumUI_ClickingThemeChips_DoesNotCrash()
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await Page.ClickAsync("[data-testid=\"example-category\"][data-category-name=\"Gum\"]");
        await ResetCanvasContextAsync();
        await Page.ClickAsync("[data-testid=\"example-card\"][data-example-name=\"GumUI\"]");
        await WaitForWebGlContextAsync();
        await AssertNoBlazorErrorAsync("after loading GumUI");

        IElementHandle? canvas = await Page.QuerySelectorAsync("#theCanvas");
        Assert.That(canvas, Is.Not.Null, "the game canvas should be present");
        ElementHandleBoundingBoxResult? box = await canvas!.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, "the game canvas should report a bounding box");

        // Picker row mirrors GumUI.cs's BuildThemePicker: 6 ToggleButtons (Default + 5 themes),
        // each ~108 wide with 8px spacing, starting at X=16 Y=40 in Gum's canvas coordinate
        // space (which maps 1:1 onto canvas CSS pixels).
        const float ChipWidth = 108;
        const float Spacing = 8;
        const float StartX = 16;
        const float ClickY = 55;
        const int ChipCount = 6;

        for (int i = 0; i < ChipCount; i++)
        {
            float chipCenterX = box!.X + StartX + i * (ChipWidth + Spacing) + ChipWidth / 2;
            float clickY = box.Y + ClickY;

            await Page.Mouse.ClickAsync(chipCenterX, clickY);
            await Page.WaitForTimeoutAsync(100);
        }

        await AssertNoBlazorErrorAsync("after clicking every theme chip");
        bool contextStillLive = await Page.EvaluateAsync<bool>(
            "() => window._canvasContextType === 'webgl' || window._canvasContextType === 'webgl2'");
        Assert.That(contextStillLive, Is.True, "the WebGL context should still be alive after clicking theme chips");
    }
}
