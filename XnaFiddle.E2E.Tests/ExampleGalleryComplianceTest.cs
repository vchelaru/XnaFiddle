using Microsoft.Playwright;
using NUnit.Framework;

namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Sweeps every built-in example in the Examples gallery and asserts each one compiles (Roslyn)
/// and runs (KNI + WebGL) cleanly against the published app. Regression coverage for the class of
/// bug where a dependency bump (e.g. Gum/Apos.Shapes) breaks an example's source or its runtime
/// ABI without failing `dotnet build` — the Examples/*.cs sources are `&lt;Compile Remove&gt;`-excluded
/// from the normal build and only ever compiled in-browser at runtime, so only an e2e run against
/// the real gallery UI catches it.
///
/// The catalog is discovered from the live DOM rather than referencing
/// <c>XnaFiddle.ExampleGallery</c> directly: BlazorGL targets net10.0-browser and isn't
/// referenceable from this net10.0 desktop test project. Each category in the Examples modal's
/// sidebar only renders its own example cards (see Index.razor), so discovery must click through
/// every category and collect (category, example) pairs — a single "grab all example-card
/// elements" pass would only see whichever category happens to be selected.
/// </summary>
[TestFixture]
public sealed class ExampleGalleryComplianceTest : E2ETestBase
{
    // "Empty" is the blank starting-point example — already covered by SmokeTest's default-sample
    // boot/run assertion, so it's excluded here per the e2e-testing skill's "add value, don't
    // duplicate" guidance rather than re-asserting the same thing under a different name.
    private const string SkippedExampleName = "Empty";

    private static readonly Lazy<(string Category, string Name)[]> _catalog =
        new(() => DiscoverCatalogAsync().GetAwaiter().GetResult());

    public static IEnumerable<TestCaseData> ExampleCases() =>
        _catalog.Value
            .Where(e => e.Name != SkippedExampleName)
            .Select(e => new TestCaseData(e.Category, e.Name).SetName($"Example_{e.Name}_CompilesAndRuns"));

    [Test]
    [TestCaseSource(nameof(ExampleCases))]
    public async Task Example_CompilesAndRunsCleanly(string category, string exampleName)
    {
        await BootAsync();

        await Page.ClickAsync("[data-testid=\"examples-button\"]");
        await Page.ClickAsync($"[data-testid=\"example-category\"][data-category-name=\"{category}\"]");
        await ResetCanvasContextAsync();
        await Page.ClickAsync($"[data-testid=\"example-card\"][data-example-name=\"{exampleName}\"]");

        (bool success, string diagnostics) = await WaitForRunOutcomeAsync();

        Assert.That(success, Is.True,
            $"Example '{exampleName}' (category '{category}') should compile and run cleanly. " +
            $"Diagnostics panel: {diagnostics}");
        await AssertNoBlazorErrorAsync($"after loading example '{exampleName}'");
    }

    // ---- Catalog discovery (runs once, before OneTimeSetUp, to build the TestCaseSource list) ----

    // NUnit enumerates TestCaseSource at test-discovery time, before any fixture's OneTimeSetUp
    // runs, so this spins up its own short-lived host + browser purely to read the gallery's
    // (category, example) pairs from the DOM. Lazy<T> ensures it only runs once even though NUnit
    // may query ExampleCases() more than once during discovery.
    private static async Task<(string Category, string Name)[]> DiscoverCatalogAsync()
    {
        await using StaticSiteHost host = await StaticSiteHost.StartAsync(PublishOutput.ResolveWebRoot());
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"],
        });
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#theCanvas");
        await page.WaitForFunctionAsync(
            "() => window.theInstance !== undefined && window.theInstance !== null",
            null,
            new PageWaitForFunctionOptions { PollingInterval = 250 });

        await page.ClickAsync("[data-testid=\"examples-button\"]");

        var categoryNames = new List<string>();
        foreach (IElementHandle el in await page.QuerySelectorAllAsync("[data-testid=\"example-category\"]"))
        {
            string? name = await el.GetAttributeAsync("data-category-name");
            if (!string.IsNullOrEmpty(name))
            {
                categoryNames.Add(name);
            }
        }

        var catalog = new List<(string Category, string Name)>();
        foreach (string category in categoryNames)
        {
            await page.ClickAsync($"[data-testid=\"example-category\"][data-category-name=\"{category}\"]");
            foreach (IElementHandle card in await page.QuerySelectorAllAsync("[data-testid=\"example-card\"]"))
            {
                string? name = await card.GetAttributeAsync("data-example-name");
                if (!string.IsNullOrEmpty(name))
                {
                    catalog.Add((category, name));
                }
            }
        }

        return catalog.ToArray();
    }
}
