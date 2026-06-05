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
        Assert.Contains("FlatRedBall.InterpolationCore", plugin.RequiredAssemblies);
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

    // ── AposShapesPlugin ─────────────────────────────────────────────────────

    [Fact]
    public void AposShapesPlugin_Name()
    {
        Assert.Equal("Apos.Shapes", new AposShapesPlugin().Name);
    }

    [Fact]
    public void AposShapesPlugin_RequiredAssemblies()
    {
        Assert.Equal(new[] { "Apos.Shapes.KNI" }, new AposShapesPlugin().RequiredAssemblies);
    }

    [Fact]
    public void AposShapesPlugin_VersionInfo()
    {
        var info = new AposShapesPlugin().VersionInfo;
        Assert.Equal("Apos.Shapes.KNI", info.Label);
        Assert.Equal(new[] { "Apos.Shapes.KNI" }, info.AssemblyNames);
    }

    [Fact]
    public void AposShapesPlugin_CleanUp_IsNoOp()
    {
        new AposShapesPlugin().CleanUp();
        new AposShapesPlugin().CleanUp();
    }

    // ── FontStashSharpPlugin ─────────────────────────────────────────────────

    [Fact]
    public void FontStashSharpPlugin_Name()
    {
        Assert.Equal("FontStashSharp", new FontStashSharpPlugin().Name);
    }

    [Fact]
    public void FontStashSharpPlugin_RequiredAssemblies()
    {
        var assemblies = new FontStashSharpPlugin().RequiredAssemblies;
        Assert.Contains("FontStashSharp.Kni", assemblies);
        Assert.Contains("FontStashSharp.Base", assemblies);
        Assert.Contains("FontStashSharp.Rasterizers.StbTrueTypeSharp", assemblies);
    }

    [Fact]
    public void FontStashSharpPlugin_VersionInfo()
    {
        var info = new FontStashSharpPlugin().VersionInfo;
        Assert.Equal("FontStashSharp.Kni", info.Label);
        Assert.Equal(new[] { "FontStashSharp.Kni", "FontStashSharp.Base" }, info.AssemblyNames);
    }

    // ── MonoGameExtendedPlugin ───────────────────────────────────────────────

    [Fact]
    public void MonoGameExtendedPlugin_Name()
    {
        Assert.Equal("MonoGame.Extended", new MonoGameExtendedPlugin().Name);
    }

    [Fact]
    public void MonoGameExtendedPlugin_RequiredAssemblies()
    {
        Assert.Equal(new[] { "KNI.Extended" }, new MonoGameExtendedPlugin().RequiredAssemblies);
    }

    [Fact]
    public void MonoGameExtendedPlugin_VersionInfo()
    {
        var info = new MonoGameExtendedPlugin().VersionInfo;
        Assert.Equal("KNI.Extended", info.Label);
        Assert.Equal(new[] { "KNI.Extended" }, info.AssemblyNames);
    }

    // ── AetherPhysicsPlugin ──────────────────────────────────────────────────

    [Fact]
    public void AetherPhysicsPlugin_Name()
    {
        Assert.Equal("Aether.Physics2D", new AetherPhysicsPlugin().Name);
    }

    [Fact]
    public void AetherPhysicsPlugin_RequiredAssemblies()
    {
        Assert.Equal(new[] { "Aether.Physics2D" }, new AetherPhysicsPlugin().RequiredAssemblies);
    }

    [Fact]
    public void AetherPhysicsPlugin_VersionInfo()
    {
        var info = new AetherPhysicsPlugin().VersionInfo;
        Assert.Equal("Aether.Physics2D", info.Label);
        Assert.Equal(new[] { "Aether.Physics2D" }, info.AssemblyNames);
    }

    // ── KernSmithPlugin ──────────────────────────────────────────────────────

    [Fact]
    public void KernSmithPlugin_Name()
    {
        Assert.Equal("KernSmith", new KernSmithPlugin().Name);
    }

    [Fact]
    public void KernSmithPlugin_RequiredAssemblies()
    {
        var assemblies = new KernSmithPlugin().RequiredAssemblies;
        Assert.Contains("KernSmith", assemblies);
        Assert.Contains("KernSmith.GumCommon", assemblies);
        Assert.Contains("KernSmith.KniGum", assemblies);
    }

    [Fact]
    public void KernSmithPlugin_VersionInfo()
    {
        var info = new KernSmithPlugin().VersionInfo;
        Assert.Equal("KernSmith.KniGum", info.Label);
        Assert.Equal(new[] { "KernSmith.KniGum", "KernSmith.GumCommon", "KernSmith" }, info.AssemblyNames);
    }

    // ── FlatRedBallAnimationChainPlugin ──────────────────────────────────────

    [Fact]
    public void FlatRedBallAnimationChainPlugin_Name()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.Equal("FlatRedBall.AnimationChain", plugin.Name);
    }

    [Fact]
    public void FlatRedBallAnimationChainPlugin_RequiredAssemblies()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.Single(plugin.RequiredAssemblies);
        Assert.Contains("AnimationChain.KNI", plugin.RequiredAssemblies);
    }

    [Fact]
    public void FlatRedBallAnimationChainPlugin_VersionInfo_Label()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.Equal("FlatRedBall.AnimationChain", plugin.VersionInfo.Label);
    }

    [Fact]
    public void FlatRedBallAnimationChainPlugin_VersionInfo_AssemblyNames()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.Equal(new[] { "AnimationChain.KNI" }, plugin.VersionInfo.AssemblyNames);
    }

    [Fact]
    public void FlatRedBallAnimationChainPlugin_CleanUp_WhenNotLoaded_IsNoOp()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        // Should not throw even if the plugin is not loaded
        plugin.CleanUp();
    }

    [Fact]
    public void FlatRedBallAnimationChainPlugin_CleanUp_IsIdempotent()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        plugin.CleanUp();
        plugin.CleanUp();
        // No exception = pass
    }
}
