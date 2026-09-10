using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Coverage for the example browser's HLSL/Slang shader-language toggle (issue #144). The
/// "Grayscale" example ships the same effect twice — Grayscale.fx and Grayscale.slang — and opens
/// exactly one as a shader tab, so <see cref="ExampleGalleryComplianceTest"/>'s sweep only ever
/// exercises the default (HLSL) side. These tests are the only thing that drives the Slang path
/// end to end: ShadowDusk's Slang frontend converting .slang to .fx text before the usual
/// WasmShaderCompiler compile (see CompileRegisteredShadersAsync).
/// </summary>
[TestFixture]
public sealed class ShaderLanguageToggleTest : E2ETestBase
{
    private const string ShaderCategory = "2D Shaders";
    private const string SlangExample = "Grayscale";

    // An example in the same category with no .slang port — proves the "HLSL only" card tag.
    private const string HlslOnlyExample = "Invert";

    [Test]
    public async Task SlangSelected_GrayscaleExample_CompilesAndRuns()
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await Page.ClickAsync($"[data-testid=\"example-category\"][data-category-name=\"{ShaderCategory}\"]");
        await Page.ClickAsync("[data-testid=\"shader-language-toggle\"] [data-language=\"slang\"]");

        // The toggle must not silently lie about what an un-ported example will load.
        await AssertHlslOnlyTagAsync(SlangExample, expected: false);
        await AssertHlslOnlyTagAsync(HlslOnlyExample, expected: true);

        await ResetCanvasContextAsync();
        await Page.ClickAsync($"[data-testid=\"example-card\"][data-example-name=\"{SlangExample}\"]");

        (bool success, string diagnostics) = await WaitForRunOutcomeAsync();
        Assert.That(success, Is.True,
            $"'{SlangExample}' should compile and run from its .slang source. Diagnostics panel: {diagnostics}");

        await AssertShaderTabAsync("Grayscale.slang");
        await AssertNoBlazorErrorAsync($"after loading '{SlangExample}' as Slang");
    }

    [Test]
    public async Task SwitchingLanguage_WithExampleAlreadyLoaded_ReloadsItInTheNewLanguage()
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

        // Flipping the toggle with the example already loaded must reload it right away, not wait
        // for the card to be picked again — otherwise the running effect is still the old language.
        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await ResetCanvasContextAsync();
        await Page.ClickAsync("[data-testid=\"shader-language-toggle\"] [data-language=\"slang\"]");

        (bool slangSuccess, string slangDiagnostics) = await WaitForRunOutcomeAsync();
        Assert.That(slangSuccess, Is.True,
            $"Switching to Slang should re-run '{SlangExample}'. Diagnostics panel: {slangDiagnostics}");

        await AssertShaderTabAsync("Grayscale.slang");
        await AssertNoBlazorErrorAsync($"after switching '{SlangExample}' to Slang");
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
