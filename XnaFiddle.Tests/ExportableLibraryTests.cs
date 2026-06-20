using XnaFiddle;
using XnaFiddle.Plugins;

namespace XnaFiddle.Tests;

public class ExportableLibraryTests
{
    // -- Helper ───────────────────────────────────────────────────────────────

    static List<string> PackageIds(IExportableLibrary lib, ExportTarget target, string source = "") =>
        lib.GetExportPackages(target, source).Select(p => p.Id).ToList();

    // ── GumPlugin ────────────────────────────────────────────────────────────

    [Fact]
    public void Gum_DetectsMonoGameGumUsing()
    {
        var plugin = new GumPlugin();
        Assert.True(plugin.IsUsedInSource("using Gum;"));
    }

    [Fact]
    public void Gum_DetectsBareGumUsing()
    {
        // Post-2026-June migration, examples like AetherPhysics/SoundPlayback carry only a
        // bare `using Gum;` (no `Gum.` substring), so detection relies on this path.
        Assert.True(new GumPlugin().IsUsedInSource("using Gum;"));
    }

    [Fact]
    public void Gum_DetectsGumDotNamespace()
    {
        var plugin = new GumPlugin();
        Assert.True(plugin.IsUsedInSource("using Gum.Forms;"));
    }

    [Fact]
    public void Gum_NotDetectedInPlainCode()
    {
        var plugin = new GumPlugin();
        Assert.False(plugin.IsUsedInSource("using Microsoft.Xna.Framework;"));
    }

    [Fact]
    public void Gum_KniPackage()
    {
        var plugin = new GumPlugin();
        var ids = PackageIds(plugin, ExportTarget.KniDesktopGL);
        Assert.Contains("Gum.KNI", ids);
    }

    [Fact]
    public void Gum_MonoGamePackage()
    {
        var plugin = new GumPlugin();
        var ids = PackageIds(plugin, ExportTarget.MonoGameDesktopGL);
        Assert.Contains("Gum.MonoGame", ids);
    }

    // ── GumShapesPlugin ──────────────────────────────────────────────────────

    [Fact]
    public void GumShapes_DetectsShapeRendererUsage()
    {
        var plugin = new GumShapesPlugin();
        Assert.True(plugin.IsUsedInSource("ShapeRenderer.Self.Initialize();"));
    }

    [Fact]
    public void GumShapes_NotDetectedForPlainGumOrColoredRectangle()
    {
        // Plain Gum code (and base-Gum's ColoredRectangleRuntime, whose name contains
        // "RectangleRuntime") must NOT pull in the shapes package — only ShapeRenderer does.
        var plugin = new GumShapesPlugin();
        Assert.False(plugin.IsUsedInSource("using Gum;"));
        Assert.False(plugin.IsUsedInSource("var r = new ColoredRectangleRuntime();"));
    }

    [Fact]
    public void GumShapes_KniPackage()
    {
        var ids = PackageIds(new GumShapesPlugin(), ExportTarget.KniDesktopGL);
        Assert.Contains("Gum.Shapes.KNI", ids);
    }

    [Fact]
    public void GumShapes_MonoGamePackage()
    {
        var ids = PackageIds(new GumShapesPlugin(), ExportTarget.MonoGameDesktopGL);
        Assert.Contains("Gum.Shapes.MonoGame", ids);
    }

    // ── AposShapesPlugin ─────────────────────────────────────────────────────

    [Fact]
    public void AposShapes_DetectsUsage()
    {
        var plugin = new AposShapesPlugin();
        Assert.True(plugin.IsUsedInSource("using Apos.Shapes;"));
        Assert.False(plugin.IsUsedInSource("using Microsoft.Xna.Framework;"));
    }

    [Fact]
    public void AposShapes_KniPackage()
    {
        var ids = PackageIds(new AposShapesPlugin(), ExportTarget.KniDesktopGL);
        Assert.Contains("Apos.Shapes.KNI", ids);
    }

    [Fact]
    public void AposShapes_MonoGamePackage()
    {
        var ids = PackageIds(new AposShapesPlugin(), ExportTarget.MonoGameDesktopGL);
        Assert.Contains("Apos.Shapes", ids);
    }

    // ── MonoGameExtendedPlugin ───────────────────────────────────────────────

    [Fact]
    public void MonoGameExtended_DetectsUsage()
    {
        var plugin = new MonoGameExtendedPlugin();
        Assert.True(plugin.IsUsedInSource("using MonoGame.Extended;"));
        Assert.False(plugin.IsUsedInSource("using Apos.Shapes;"));
    }

    [Fact]
    public void MonoGameExtended_KniPackage()
    {
        var ids = PackageIds(new MonoGameExtendedPlugin(), ExportTarget.KniDesktopGL);
        Assert.Contains("KNI.Extended", ids);
    }

    [Fact]
    public void MonoGameExtended_MonoGamePackage()
    {
        var ids = PackageIds(new MonoGameExtendedPlugin(), ExportTarget.MonoGameDesktopGL);
        Assert.Contains("MonoGame.Extended", ids);
    }

    // ── FontStashSharpPlugin ─────────────────────────────────────────────────

    [Fact]
    public void FontStashSharp_DetectsUsage()
    {
        var plugin = new FontStashSharpPlugin();
        Assert.True(plugin.IsUsedInSource("using FontStashSharp;"));
        Assert.False(plugin.IsUsedInSource("using Gum.Forms;"));
    }

    [Fact]
    public void FontStashSharp_KniPackage()
    {
        var ids = PackageIds(new FontStashSharpPlugin(), ExportTarget.KniDesktopGL);
        Assert.Contains("FontStashSharp.Kni", ids);
    }

    [Fact]
    public void FontStashSharp_MonoGamePackage()
    {
        var ids = PackageIds(new FontStashSharpPlugin(), ExportTarget.MonoGameDesktopGL);
        Assert.Contains("FontStashSharp.MonoGame", ids);
    }

    // ── AetherPhysicsPlugin ──────────────────────────────────────────────────

    [Fact]
    public void AetherPhysics_DetectsUsage()
    {
        var plugin = new AetherPhysicsPlugin();
        Assert.True(plugin.IsUsedInSource("using Aether.Physics2D;"));
        Assert.False(plugin.IsUsedInSource("using Gum.Forms;"));
    }

    [Fact]
    public void AetherPhysics_KniPackage()
    {
        var ids = PackageIds(new AetherPhysicsPlugin(), ExportTarget.KniDesktopGL);
        Assert.Contains("Aether.Physics2D.KNI", ids);
    }

    [Fact]
    public void AetherPhysics_MonoGamePackage()
    {
        var ids = PackageIds(new AetherPhysicsPlugin(), ExportTarget.MonoGameDesktopGL);
        Assert.Contains("Aether.Physics2D", ids);
    }

    // ── KernSmithPlugin ──────────────────────────────────────────────────────

    [Fact]
    public void KernSmith_DetectsUsage()
    {
        var plugin = new KernSmithPlugin();
        Assert.True(plugin.IsUsedInSource("using KernSmith;"));
        Assert.False(plugin.IsUsedInSource("using Gum.Forms;"));
    }

    [Fact]
    public void KernSmith_KniPackages_IncludesBaseAndGumAndRasterizer()
    {
        var ids = PackageIds(new KernSmithPlugin(), ExportTarget.KniDesktopGL);
        Assert.Contains("KernSmith", ids);
        Assert.Contains("KernSmith.KniGum", ids);
        Assert.Contains("KernSmith.Rasterizers.StbTrueType", ids);
    }

    [Fact]
    public void KernSmith_MonoGamePackages_UsesMonoGameGumVariant()
    {
        var ids = PackageIds(new KernSmithPlugin(), ExportTarget.MonoGameDesktopGL);
        Assert.Contains("KernSmith", ids);
        Assert.Contains("KernSmith.MonoGameGum", ids);
        Assert.Contains("KernSmith.Rasterizers.StbTrueType", ids);
    }

    // ── MlemPlugin ───────────────────────────────────────────────────────────

    [Fact]
    public void Mlem_DetectsUsage()
    {
        var plugin = new MlemPlugin();
        Assert.True(plugin.IsUsedInSource("using MLEM.Font;"));
        Assert.False(plugin.IsUsedInSource("using Gum.Forms;"));
    }

    [Fact]
    public void Mlem_BaseOnly_KniPackage()
    {
        var plugin = new MlemPlugin();
        // When only "MLEM" is in source (not MLEM.Ui or MLEM.Extended),
        // should only include the base package
        var ids = PackageIds(plugin, ExportTarget.KniDesktopGL, "using MLEM.Font;");
        Assert.Contains("MLEM.KNI", ids);
        Assert.DoesNotContain("MLEM.Ui.KNI", ids);
        Assert.DoesNotContain("MLEM.Extended.KNI", ids);
    }

    [Fact]
    public void Mlem_BaseOnly_MonoGamePackage()
    {
        var plugin = new MlemPlugin();
        var ids = PackageIds(plugin, ExportTarget.MonoGameDesktopGL, "using MLEM.Font;");
        Assert.Contains("MLEM", ids);
        Assert.DoesNotContain("MLEM.Ui", ids);
        Assert.DoesNotContain("MLEM.Extended", ids);
    }

    [Fact]
    public void Mlem_WithUi_IncludesUiPackage()
    {
        var plugin = new MlemPlugin();
        // Simulate source that uses MLEM.Ui
        string source = "using MLEM.Ui;";
        Assert.True(plugin.IsUsedInSource(source));
        var packages = plugin.GetExportPackages(ExportTarget.KniDesktopGL, source);
        var ids = packages.Select(p => p.Id).ToList();
        Assert.Contains("MLEM.KNI", ids);
        Assert.Contains("MLEM.Ui.KNI", ids);
    }

    [Fact]
    public void Mlem_WithExtended_IncludesExtendedPackage()
    {
        var plugin = new MlemPlugin();
        string source = "using MLEM.Extended.Font;";
        var packages = plugin.GetExportPackages(ExportTarget.MonoGameDesktopGL, source);
        var ids = packages.Select(p => p.Id).ToList();
        Assert.Contains("MLEM", ids);
        Assert.Contains("MLEM.Extended", ids);
    }

    // ── FlatRedBallAnimationChainPlugin ──────────────────────────────────────

    [Fact]
    public void FlatRedBallAnimationChain_DetectsAnimationChainUsage()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.True(plugin.IsUsedInSource("var chain = new AnimationChain();"));
    }

    [Fact]
    public void FlatRedBallAnimationChain_DetectsAnimationPlayerUsage()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.True(plugin.IsUsedInSource("var player = new AnimationPlayer();"));
    }

    [Fact]
    public void FlatRedBallAnimationChain_NotDetectedInUnrelatedCode()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        Assert.False(plugin.IsUsedInSource("using Microsoft.Xna.Framework;"));
    }

    [Fact]
    public void FlatRedBallAnimationChain_KniPackage()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        var ids = PackageIds(plugin, ExportTarget.KniDesktopGL);
        Assert.Contains("FlatRedBall.AnimationChain.KNI", ids);
        Assert.Single(ids); // Should only have one package
    }

    [Fact]
    public void FlatRedBallAnimationChain_MonoGamePackage()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        var ids = PackageIds(plugin, ExportTarget.MonoGameDesktopGL);
        Assert.Contains("FlatRedBall.AnimationChain.MonoGame", ids);
        Assert.Single(ids); // Should only have one package
    }

    [Fact]
    public void FlatRedBallAnimationChain_KniAndroidPackage()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        var ids = PackageIds(plugin, ExportTarget.KniAndroid);
        Assert.Contains("FlatRedBall.AnimationChain.KNI", ids);
    }

    [Fact]
    public void FlatRedBallAnimationChain_CorrectVersion()
    {
        var plugin = new FlatRedBallAnimationChainPlugin();
        var packages = plugin.GetExportPackages(ExportTarget.KniDesktopGL, "");
        var pkg = packages.First(p => p.Id == "FlatRedBall.AnimationChain.KNI");
        Assert.Equal(PackageVersions.FlatRedBallAnimationChain, pkg.Version);
    }
}
