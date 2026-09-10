using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Coverage for the example browser's HLSL/Slang shader-language radio (issue #144). Shader
/// examples ship the same effect twice — <c>{Name}.{Shader}.fx</c> and <c>{Name}.{Shader}.slang</c>
/// — and open exactly one language as a shader tab, so <see cref="ExampleGalleryComplianceTest"/>'s
/// sweep only ever exercises the default (HLSL) side. These tests are the only thing that drives
/// the Slang path end to end: ShadowDusk's Slang frontend converting .slang to .fx text before the
/// usual WasmShaderCompiler compile (see CompileRegisteredShadersAsync).
///
/// One example proves that wiring, so this deliberately does not sweep every ported example. The
/// per-shader check is the headless <c>ShadowDuskCLI &lt;in&gt; &lt;out&gt; /Profile:OpenGL</c>
/// gate, which costs seconds instead of a browser boot apiece.
/// </summary>
[TestFixture]
public sealed class ShaderLanguageToggleTest : E2ETestBase
{
    private const string ShaderCategory = "2D Shaders";
    private const string SlangExample = "Grayscale";

    // The radio input itself, not its <label>, so the test can read IsChecked as well as click.
    private const string SlangRadioSelector =
        "[data-testid=\"shader-language-toggle\"] [data-language=\"slang\"] input";

    [Test]
    public async Task SlangSelected_GrayscaleExample_CompilesAndRuns()
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await Page.ClickAsync($"[data-testid=\"example-category\"][data-category-name=\"{ShaderCategory}\"]");
        await Page.ClickAsync(SlangRadioSelector);

        // The radio must not lie about what a card will load. Only the negative half is assertable
        // today: every shader example ships a .slang, so no card renders the "HLSL only" tag.
        await AssertHlslOnlyTagAsync(SlangExample, expected: false);

        await ResetCanvasContextAsync();
        await Page.ClickAsync($"[data-testid=\"example-card\"][data-example-name=\"{SlangExample}\"]");

        (bool success, string diagnostics) = await WaitForRunOutcomeAsync();
        Assert.That(success, Is.True,
            $"'{SlangExample}' should compile and run from its .slang source. Diagnostics panel: {diagnostics}");

        await AssertShaderTabAsync("Grayscale.slang");
        await AssertNoBlazorErrorAsync($"after loading '{SlangExample}' as Slang");
    }

    /// <summary>
    /// The radio is a preference, not an action: it decides what the next card click loads. Picking
    /// it with an example already open must not touch the running fiddle, because reloading would
    /// throw away unsaved edits to the open shader tab.
    /// </summary>
    [Test]
    public async Task SwitchingLanguage_WithExampleAlreadyLoaded_LeavesTheOpenFiddleAlone()
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await Page.ClickAsync($"[data-testid=\"example-category\"][data-category-name=\"{ShaderCategory}\"]");
        await ResetCanvasContextAsync();
        await Page.ClickAsync($"[data-testid=\"example-card\"][data-example-name=\"{SlangExample}\"]");

        (bool hlslSuccess, string hlslDiagnostics) = await WaitForRunOutcomeAsync();
        Assert.That(hlslSuccess, Is.True,
            $"'{SlangExample}' should compile and run from its .fx source. Diagnostics panel: {hlslDiagnostics}");
        await AssertShaderTabAsync("Grayscale.fx");
        string codeBeforeSwitch = await GetEditorValueAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await Page.ClickAsync($"[data-testid=\"example-category\"][data-category-name=\"{ShaderCategory}\"]");
        await Page.ClickAsync(SlangRadioSelector);

        Assert.That(await Page.Locator(SlangRadioSelector).IsCheckedAsync(), Is.True,
            "clicking the Slang radio should select it");
        Assert.That(await Page.Locator("[data-testid=\"shader-language-toggle\"]").CountAsync(), Is.EqualTo(1),
            "the example browser should stay open — the radio is a preference, not a card pick");

        await AssertShaderTabAsync("Grayscale.fx");
        Assert.That(await GetEditorValueAsync(), Is.EqualTo(codeBeforeSwitch),
            "picking a shader language should not touch the C# editor");
        await AssertNoBlazorErrorAsync($"after switching the preference with '{SlangExample}' loaded");
    }

    // Exactly one shader tab, carrying the expected filename: the .fx and the .slang register the
    // same bare content key, so both being open at once is the failure this guards.
    private async Task AssertShaderTabAsync(string expectedFileName)
    {
        await Page.WaitForSelectorAsync(
            $"[data-testid=\"shader-tab\"][data-shader-name=\"{expectedFileName}\"]",
            new PageWaitForSelectorOptions { Timeout = RunTimeoutMs });

        int tabCount = await Page.Locator("[data-testid=\"shader-tab\"]").CountAsync();
        Assert.That(tabCount, Is.EqualTo(1), $"only '{expectedFileName}' should be open as a shader tab");
    }

    private async Task AssertHlslOnlyTagAsync(string exampleName, bool expected)
    {
        int tagCount = await Page
            .Locator($"[data-testid=\"example-card\"][data-example-name=\"{exampleName}\"] [data-testid=\"hlsl-only-tag\"]")
            .CountAsync();

        Assert.That(tagCount > 0, Is.EqualTo(expected),
            $"'{exampleName}' should{(expected ? "" : " not")} show the \"HLSL only\" tag while Slang is selected");
    }
}
