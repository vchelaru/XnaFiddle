using XnaFiddle.Plugins;

namespace XnaFiddle.Tests;

public class PluginTests
{
    // ── GameWindowPlugin ─────────────────────────────────────────────────────

    [Fact]
    public void GameWindowPlugin_Name()
    {
        var plugin = new GameWindowPlugin();
        Assert.Equal("GameWindow", plugin.Name);
    }

    [Fact]
    public void GameWindowPlugin_RequiredAssemblies_IsEmpty()
    {
        // GameWindow is part of the KNI platform, not a third-party library.
        // Its assemblies are already in KniAssemblyNames — the plugin exists
        // only for cleanup, not to add compilation references.
        var plugin = new GameWindowPlugin();
        Assert.Empty(plugin.RequiredAssemblies);
    }

    [Fact]
    public void GameWindowPlugin_CleanUp_WhenNoInstancesExist_IsNoOp()
    {
        var plugin = new GameWindowPlugin();

        // Should not throw even if no game windows have been created
        plugin.CleanUp();
    }

    [Fact]
    public void GameWindowPlugin_CleanUp_IsIdempotent()
    {
        var plugin = new GameWindowPlugin();

        plugin.CleanUp();
        plugin.CleanUp();
        // No exception = pass
    }

    // ── GumPlugin ────────────────────────────────────────────────────────────

    [Fact]
    public void GumPlugin_Name()
    {
        var plugin = new GumPlugin();
        Assert.Equal("Gum", plugin.Name);
    }

    [Fact]
    public void GumPlugin_RequiredAssemblies_ContainsKniGumAndGumCommon()
    {
        var plugin = new GumPlugin();
        Assert.Contains("KniGum", plugin.RequiredAssemblies);
        Assert.Contains("GumCommon", plugin.RequiredAssemblies);
    }

    [Fact]
    public void GumPlugin_VersionInfo_Label()
    {
        var plugin = new GumPlugin();
        Assert.Equal("Gum.KNI", plugin.VersionInfo.Label);
    }

    [Fact]
    public void GumPlugin_VersionInfo_AssemblyNames()
    {
        var plugin = new GumPlugin();
        Assert.Equal(new[] { "GumCommon", "KniGum" }, plugin.VersionInfo.AssemblyNames);
    }

    [Fact]
    public void GumPlugin_CleanUp_WhenGumNotLoaded_IsNoOp()
    {
        // In the test environment, KniGum is not loaded. CleanUp should
        // silently no-op rather than throwing.
        var plugin = new GumPlugin();
        plugin.CleanUp();
    }

    [Fact]
    public void GumPlugin_CleanUp_IsIdempotent()
    {
        var plugin = new GumPlugin();
        plugin.CleanUp();
        plugin.CleanUp();
    }

    // ── MlemPlugin ───────────────────────────────────────────────────────────

    [Fact]
    public void MlemPlugin_Name()
    {
        var plugin = new MlemPlugin();
        Assert.Equal("MLEM", plugin.Name);
    }

    [Fact]
    public void MlemPlugin_RequiredAssemblies_ContainsMlemPackages()
    {
        var plugin = new MlemPlugin();
        Assert.Contains("MLEM.KNI", plugin.RequiredAssemblies);
        Assert.Contains("MLEM.Ui.KNI", plugin.RequiredAssemblies);
        Assert.Contains("MLEM.Extended.KNI", plugin.RequiredAssemblies);
    }

    [Fact]
    public void MlemPlugin_VersionInfo_Label()
    {
        var plugin = new MlemPlugin();
        Assert.Equal("MLEM", plugin.VersionInfo.Label);
    }

    [Fact]
    public void MlemPlugin_VersionInfo_AssemblyNames()
    {
        var plugin = new MlemPlugin();
        Assert.Equal(new[] { "MLEM.KNI", "MLEM.Ui.KNI", "MLEM.Extended.KNI" }, plugin.VersionInfo.AssemblyNames);
    }

    [Fact]
    public void MlemPlugin_CleanUp_WhenMlemNotLoaded_IsNoOp()
    {
        var plugin = new MlemPlugin();
        plugin.CleanUp();
    }

    [Fact]
    public void MlemPlugin_CleanUp_IsIdempotent()
    {
        var plugin = new MlemPlugin();
        plugin.CleanUp();
        plugin.CleanUp();
    }
}
