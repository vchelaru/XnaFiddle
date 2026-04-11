using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using XnaFiddle;
using XnaFiddle.Plugins;

namespace XnaFiddle.Tests;

public class ProjectExporterTests
{
    static LibraryRegistry CreateRegistry()
    {
        var registry = new LibraryRegistry();
        registry.Register(new GameWindowPlugin());
        registry.Register(new GumPlugin());
        registry.Register(new MlemPlugin());
        registry.Register(new AposShapesPlugin());
        registry.Register(new FontStashSharpPlugin());
        registry.Register(new MonoGameExtendedPlugin());
        registry.Register(new AetherPhysicsPlugin());
        registry.Register(new KernSmithPlugin());
        return registry;
    }

    // Minimal game code that triggers no third-party library detection
    const string MinimalCode = @"
using Microsoft.Xna.Framework;
public class Game1 : Game
{
    protected override void Draw(GameTime gt) { }
}";

    // Game code that references FontStashSharp
    const string FontStashSharpCode = @"
using Microsoft.Xna.Framework;
using FontStashSharp;
public class Game1 : Game
{
    FontSystem fs;
    protected override void Draw(GameTime gt) { }
}";

    // Game code that references Gum
    const string GumCode = @"
using Microsoft.Xna.Framework;
using MonoGameGum;
public class Game1 : Game
{
    protected override void Draw(GameTime gt) { }
}";

    static Dictionary<string, string> ExtractTextFiles(byte[] zip)
    {
        var files = new Dictionary<string, string>();
        using var ms = new MemoryStream(zip);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files[entry.FullName] = reader.ReadToEnd();
        }
        return files;
    }

    // ── Single-platform export (regression) ──────────────────────────────────

    [Fact]
    public void SinglePlatform_ProducesFlatStructure()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        // Flat structure: slnx at root, everything else in MyGame/
        Assert.Contains("MyGame.slnx", files.Keys);
        Assert.Contains("MyGame/MyGame.csproj", files.Keys);
        Assert.Contains("MyGame/Game1.cs", files.Keys);
        Assert.Contains("MyGame/Program.cs", files.Keys);
        Assert.Contains("MyGame/RawContentManager.cs", files.Keys);

        // No Common project
        Assert.DoesNotContain(files.Keys, k => k.Contains("Common"));
    }

    // ── Multi-platform export structure ──────────────────────────────────────

    [Fact]
    public void MultiPlatform_ProducesCommonPlusPlatformProjects()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        // Solution
        Assert.Contains("MyGame.slnx", files.Keys);

        // Common project
        Assert.Contains("MyGameCommon/MyGameCommon.csproj", files.Keys);
        Assert.Contains("MyGameCommon/Game1.cs", files.Keys);
        Assert.Contains("MyGameCommon/RawContentManager.cs", files.Keys);

        // Platform projects
        Assert.Contains("MyGame.DesktopGL/MyGame.DesktopGL.csproj", files.Keys);
        Assert.Contains("MyGame.DesktopGL/Program.cs", files.Keys);
        Assert.Contains("MyGame.Android/MyGame.Android.csproj", files.Keys);
        Assert.Contains("MyGame.Android/Activity1.cs", files.Keys);

        // Game1 should NOT be in platform projects
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("MyGame.DesktopGL/Game1"));
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("MyGame.Android/Game1"));
    }

    [Fact]
    public void MultiPlatform_ContentAtSolutionRoot()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        var assets = new Dictionary<string, byte[]>
        {
            ["test.png"] = new byte[] { 0x89, 0x50 }
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", assets);
        var files = ExtractTextFiles(zip);

        // Content at solution root, not inside any project
        Assert.Contains("Content/test.png", files.Keys);
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("MyGameCommon/Content"));
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("MyGame.DesktopGL/Content"));
    }

    // ── Common csproj package filtering ──────────────────────────────────────

    [Fact]
    public void CommonCsproj_Kni_ExcludesPlatformPackages()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        // Should have framework packages
        Assert.Contains("nkast.Xna.Framework", common);

        // Should NOT have platform-specific packages
        Assert.DoesNotContain("nkast.Kni.Platform", common);
        Assert.DoesNotContain("Content.Pipeline.Builder", common);
    }

    [Fact]
    public void CommonCsproj_MonoGame_GetsDesktopGLWithPrivateAssets()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        // Should have MonoGame.Framework.DesktopGL as compile-time reference
        Assert.Contains("MonoGame.Framework.DesktopGL", common);
        Assert.Contains("PrivateAssets", common);

        // Should NOT have MonoGame.Content.Builder.Task
        Assert.DoesNotContain("MonoGame.Content.Builder.Task", common);
    }

    [Fact]
    public void CommonCsproj_IsLibrary()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        Assert.Contains("<OutputType>Library</OutputType>", common);
    }

    [Fact]
    public void CommonCsproj_AssemblyNameMatchesFolder()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        // AssemblyName should be MyGameCommon (not MyGame) to avoid NuGet ambiguity
        Assert.Contains("<AssemblyName>MyGameCommon</AssemblyName>", common);
        // RootNamespace should still be MyGame for code compatibility
        Assert.Contains("<RootNamespace>MyGame</RootNamespace>", common);
    }

    // ── Platform csproj content ──────────────────────────────────────────────

    [Fact]
    public void PlatformCsproj_HasProjectReferenceToCommon()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        string desktop = files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"];
        Assert.Contains(@"..\MyGameCommon\MyGameCommon.csproj", desktop);

        string android = files["MyGame.Android/MyGame.Android.csproj"];
        Assert.Contains(@"..\MyGameCommon\MyGameCommon.csproj", android);
    }

    [Fact]
    public void PlatformCsproj_Kni_HasOnlyPlatformPackages()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        string desktop = files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"];
        Assert.Contains("nkast.Kni.Platform.SDL2.GL", desktop);
        Assert.Contains("Content.Pipeline.Builder", desktop);
        // Should NOT have framework packages
        Assert.DoesNotContain("nkast.Xna.Framework.Graphics", desktop);
    }

    [Fact]
    public void PlatformCsproj_MonoGame_HasFrameworkPackage()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        string desktop = files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"];
        Assert.Contains("MonoGame.Framework.DesktopGL", desktop);

        string android = files["MyGame.Android/MyGame.Android.csproj"];
        Assert.Contains("MonoGame.Framework.Android", android);
    }

    // ── Content linking in platform projects ─────────────────────────────────

    [Fact]
    public void PlatformCsproj_DesktopGL_LinksContentFromParent()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        var assets = new Dictionary<string, byte[]> { ["x.png"] = [0] };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", assets);
        var files = ExtractTextFiles(zip);

        string desktop = files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"];
        Assert.Contains(@"..\Content\**\*", desktop);
        Assert.Contains(@"Link=""Content\", desktop);
        Assert.Contains("PreserveNewest", desktop);
    }

    [Fact]
    public void PlatformCsproj_Android_LinksContentAsAndroidAsset()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        var assets = new Dictionary<string, byte[]> { ["x.png"] = [0] };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", assets);
        var files = ExtractTextFiles(zip);

        string android = files["MyGame.Android/MyGame.Android.csproj"];
        Assert.Contains("AndroidAsset", android);
        Assert.Contains(@"..\Content\**\*", android);
    }

    [Fact]
    public void PlatformCsproj_BlazorGL_HasCopySharedContentTarget()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniBlazorGL };
        var assets = new Dictionary<string, byte[]> { ["x.png"] = [0] };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", assets);
        var files = ExtractTextFiles(zip);

        string blazor = files["MyGame.BlazorGL/MyGame.BlazorGL.csproj"];
        Assert.Contains("CopySharedContent", blazor);
        Assert.Contains(@"AfterTargets=""Build""", blazor);
        Assert.Contains(@"wwwroot\Content\", blazor);
    }

    // ── Slnx ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiPlatform_SlnxListsAllProjects()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string slnx = files["MyGame.slnx"];

        Assert.Contains(@"MyGameCommon\MyGameCommon.csproj", slnx);
        Assert.Contains(@"MyGame.DesktopGL\MyGame.DesktopGL.csproj", slnx);
        Assert.Contains(@"MyGame.Android\MyGame.Android.csproj", slnx);
    }

    [Fact]
    public void MultiPlatform_SlnxHasDeployConfigForAndroid()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string slnx = files["MyGame.slnx"];

        Assert.Contains("Deploy", slnx);
    }

    [Fact]
    public void MultiPlatform_SlnxNoDeployWithoutAndroid()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniWindowsDX };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string slnx = files["MyGame.slnx"];

        Assert.DoesNotContain("Deploy", slnx);
    }

    // ── Third-party library detection ────────────────────────────────────────

    [Fact]
    public void FontStashSharp_MonoGame_UsesCorrectPackageName()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameAndroid };
        byte[] zip = ProjectExporter.Export(FontStashSharpCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        // Should use FontStashSharp.MonoGame, NOT the deprecated FontStashSharp
        Assert.Contains("FontStashSharp.MonoGame", common);
        Assert.DoesNotContain("\"FontStashSharp\"", common);
    }

    [Fact]
    public void FontStashSharp_Kni_UsesKniPackage()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(FontStashSharpCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        Assert.Contains("FontStashSharp.Kni", common);
    }

    [Fact]
    public void ThirdPartyLibs_GoInCommonProject()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(GumCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string common = files["MyGameCommon/MyGameCommon.csproj"];
        Assert.Contains("Gum.KNI", common);

        // Should NOT be in platform projects
        string desktop = files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"];
        Assert.DoesNotContain("Gum", desktop);
    }

    // ── BlazorGL multi-platform entry points ─────────────────────────────────

    [Fact]
    public void MultiPlatform_BlazorGL_HasBlazorFiles()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniBlazorGL };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame.BlazorGL/Program.cs", files.Keys);
        Assert.Contains("MyGame.BlazorGL/App.razor", files.Keys);
        Assert.Contains("MyGame.BlazorGL/Pages/Index.razor", files.Keys);
        Assert.Contains("MyGame.BlazorGL/wwwroot/index.html", files.Keys);
    }
}
