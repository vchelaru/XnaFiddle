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
        registry.Register(new FlatRedBallAnimationChainPlugin());
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
using Gum;
public class Game1 : Game
{
     protected override void Draw(GameTime gt) { }
}";

    // Game code that references FlatRedBall.AnimationChain
    const string FlatRedBallAnimationChainCode = @"
using Microsoft.Xna.Framework;
using FlatRedBall.AnimationChain;
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

    static HashSet<string> ExtractAllFileNames(byte[] zip)
    {
        var names = new HashSet<string>();
        using var ms = new MemoryStream(zip);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
            names.Add(entry.FullName);
        return names;
    }

    [Fact]
    public void SinglePlatform_Android_IncludesManifestAndResources()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        var allFiles = ExtractAllFileNames(zip);

        Assert.Contains("MyGame/AndroidManifest.xml", files.Keys);
        Assert.Contains("MyGame/Resources/Values/strings.xml", files.Keys);
        Assert.Contains("MyGame/Resources/Values/styles.xml", allFiles);
        Assert.Contains("MyGame/Resources/Values/ic_launcher_background.xml", allFiles);
        Assert.Contains("MyGame/Resources/drawable-hdpi/icon.png", allFiles);
        Assert.Contains("MyGame/Resources/drawable-mdpi/icon.png", allFiles);
        Assert.Contains("MyGame/Resources/drawable-xhdpi/icon.png", allFiles);
        Assert.Contains("MyGame/Resources/drawable-xxhdpi/icon.png", allFiles);
        Assert.Contains("MyGame/Resources/drawable-xxxhdpi/icon.png", allFiles);
        Assert.Contains("MyGame/Resources/drawable-hdpi/splash.png", allFiles);

        Assert.Contains("com.companyname.MyGame", files["MyGame/AndroidManifest.xml"]);
        Assert.Contains("<string name=\"app_name\">MyGame</string>", files["MyGame/Resources/Values/strings.xml"]);
    }

    [Fact]
    public void MultiPlatform_Android_IncludesManifestAndResources()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var allFiles = ExtractAllFileNames(zip);

        Assert.Contains("MyGame.Android/AndroidManifest.xml", allFiles);
        Assert.Contains("MyGame.Android/Resources/Values/strings.xml", allFiles);
        Assert.Contains("MyGame.Android/Resources/drawable-xxxhdpi/icon.png", allFiles);
    }

    // ── MonoGame DX12 export ──────────────────────────────────────────────────

    [Fact]
    public void MonoGameDX12_SinglePlatform_UsesNativeFrameworkAndDx12Runtime()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameWindowsDX12 };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string csproj = files["MyGame/MyGame.csproj"];

        Assert.Contains("<MonoGamePlatform>WindowsDX12</MonoGamePlatform>", csproj);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", csproj);
        Assert.Contains("MonoGame.Framework.Native", csproj);
        Assert.Contains("MonoGame.Runtime.Windows.DX12", csproj);

        // DX12 uses neither the per-platform framework package nor the legacy MGCB content path.
        Assert.DoesNotContain("MonoGame.Framework.DesktopGL", csproj);
        Assert.DoesNotContain("MonoGame.Content.Builder.Task", csproj);
        Assert.DoesNotContain(files.Keys, k => k.Contains("dotnet-tools.json"));
    }

    [Fact]
    public void MonoGameDesktopVK_SinglePlatform_UsesNativeFrameworkAndCrossPlatformVulkanRuntimes()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopVK };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string csproj = files["MyGame/MyGame.csproj"];

        Assert.Contains("<MonoGamePlatform>DesktopVK</MonoGamePlatform>", csproj);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", csproj);
        Assert.Contains("MonoGame.Framework.Native", csproj);

        // Vulkan ships a native runtime per desktop OS.
        Assert.Contains("MonoGame.Runtime.Windows.Vulkan", csproj);
        Assert.Contains("MonoGame.Runtime.Linux.Vulkan", csproj);
        Assert.Contains("MonoGame.Runtime.Mac.Vulkan", csproj);

        Assert.DoesNotContain("MonoGame.Content.Builder.Task", csproj);
        Assert.DoesNotContain(files.Keys, k => k.Contains("dotnet-tools.json"));
    }

    [Fact]
    public void MonoGameDX12_MultiPlatform_RuntimePackageStaysOutOfCommonProject()
    {
        var targets = new List<ExportTarget>
        {
            ExportTarget.MonoGameDesktopGL,
            ExportTarget.MonoGameWindowsDX12,
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        string dx12 = files["MyGame.WindowsDX12/MyGame.WindowsDX12.csproj"];
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        // The native runtime belongs to the DX12 platform project only.
        Assert.Contains("MonoGame.Runtime.Windows.DX12", dx12);
        Assert.Contains("<MonoGamePlatform>WindowsDX12</MonoGamePlatform>", dx12);
        Assert.DoesNotContain("MonoGame.Runtime.Windows.DX12", common);
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
    public void CommonCsproj_MonoGame_GetsNativeFrameworkWithPrivateAssets()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        // Should have MonoGame.Framework.Native as compile-time reference
        Assert.Contains("MonoGame.Framework.Native", common);
        Assert.Contains("PrivateAssets", common);

        // Should NOT have MonoGame.Content.Builder.Task
        Assert.DoesNotContain("MonoGame.Content.Builder.Task", common);
    }

    [Fact]
    public void CommonCsproj_MonoGame_UsesNativeFramework()
    {
        // MonoGame 3.8.5 shared libraries compile against the renderer-agnostic
        // MonoGame.Framework.Native (the mg2dstartkit convention), not DesktopGL.
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameWindowsDX };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.Native"" Version=""{PackageVersions.MonoGameFramework}"" PrivateAssets=""All"" />",
            common);
        // The common project must not pin a concrete backend as its framework reference.
        Assert.DoesNotContain("MonoGame.Framework.DesktopGL", common);
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

    // ── MonoGame framework version selector ──────────────────────────────────

    [Fact]
    public void MonoGame_DefaultVersion_PinsStableFramework()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame");
        var files = ExtractTextFiles(zip);
        string csproj = files["MyGame/MyGame.csproj"];

        Assert.Equal("3.8.5", PackageVersions.MonoGameFramework);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Content.Builder.Task"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);
    }

    // ── MGCB dotnet-tools manifest ───────────────────────────────────────────

    [Fact]
    public void MonoGame_StableVersion_EmitsMgcbToolManifest()
    {
        // Apos.Shapes (and any buildTransitive MonoGameContentReference) needs `dotnet mgcb`,
        // which only resolves when a .config/dotnet-tools.json manifest is present.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyFiddle");
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyFiddle/.config/dotnet-tools.json", files.Keys);
        string manifest = files["MyFiddle/.config/dotnet-tools.json"];
        Assert.Contains("dotnet-mgcb", manifest);
        Assert.Contains(PackageVersions.MonoGameFramework, manifest);
    }

    [Fact]
    public void Kni_OmitsMgcbToolManifest()
    {
        // KNI uses a different content pipeline tool chain; no dotnet-mgcb manifest.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyFiddle");
        var files = ExtractTextFiles(zip);

        Assert.DoesNotContain("MyFiddle/.config/dotnet-tools.json", files.Keys);
    }

    // ── Mark-of-the-Web unblock target ───────────────────────────────────────

    [Fact]
    public void MonoGame_EmitsMarkOfTheWebUnblockTarget()
    {
        // A downloaded+extracted .zip tags .config/dotnet-tools.json with the Mark of the Web,
        // which makes `dotnet tool restore` refuse the manifest and breaks the MGCB content build.
        // The csproj ships a Windows-only target that strips the mark before tool-restore runs.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyFiddle");
        var files = ExtractTextFiles(zip);

        string csproj = files["MyFiddle/MyFiddle.csproj"];
        Assert.Contains("_UnblockMarkOfTheWeb", csproj);
        Assert.Contains(@"BeforeTargets=""_RestoreMGCBTool", csproj);
        Assert.Contains(@"Condition=""'$(OS)' == 'Windows_NT'""", csproj);
        Assert.Contains("Unblock-File", csproj);
    }

    [Fact]
    public void Kni_OmitsMarkOfTheWebUnblockTarget()
    {
        // KNI ships no dotnet-tools manifest and runs no mgcb, so there is nothing to unblock.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyFiddle");
        var files = ExtractTextFiles(zip);

        string csproj = files["MyFiddle/MyFiddle.csproj"];
        Assert.DoesNotContain("_UnblockMarkOfTheWeb", csproj);
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

    // ── FlatRedBallAnimationChain ────────────────────────────────────────────

    [Fact]
    public void FlatRedBallAnimationChain_Kni_IncludesKniPackage()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(FlatRedBallAnimationChainCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string common = files["MyGameCommon/MyGameCommon.csproj"];
        Assert.Contains("FlatRedBall.AnimationChain.KNI", common);
        Assert.DoesNotContain("FlatRedBall.AnimationChain.MonoGame", common);
    }

    [Fact]
    public void FlatRedBallAnimationChain_MonoGame_IncludesMonoGamePackage()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameAndroid };
        byte[] zip = ProjectExporter.Export(FlatRedBallAnimationChainCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string common = files["MyGameCommon/MyGameCommon.csproj"];
        Assert.Contains("FlatRedBall.AnimationChain.MonoGame", common);
        Assert.DoesNotContain("FlatRedBall.AnimationChain.KNI", common);
    }

    [Fact]
    public void FlatRedBallAnimationChain_Kni_CorrectVersion()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL };
        byte[] zip = ProjectExporter.Export(FlatRedBallAnimationChainCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string csproj = files["MyGame/MyGame.csproj"];
        // Verify version from PackageVersions (0.3.1-preview.1)
        Assert.Contains("FlatRedBall.AnimationChain.KNI", csproj);
        Assert.Contains("0.3.1-preview.1", csproj);
    }

    [Fact]
    public void FlatRedBallAnimationChain_RawContentManager_HasAchxBranch()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(FlatRedBallAnimationChainCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        // RawContentManager lives in the common project, alongside the package reference.
        string rcm = files["MyGameCommon/RawContentManager.cs"];
        Assert.Contains("using FlatRedBall.AnimationChain;", rcm);
        Assert.Contains("typeof(T) == typeof(AnimationChainList)", rcm);
        Assert.Contains("new AchxLoader(", rcm);
        Assert.Contains("SanitizeFrames", rcm);
    }

    [Fact]
    public void FlatRedBallAnimationChain_RawContentManager_SingleTarget_HasAchxBranch()
    {
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL };
        byte[] zip = ProjectExporter.Export(FlatRedBallAnimationChainCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string rcm = files["MyGame/RawContentManager.cs"];
        Assert.Contains("typeof(T) == typeof(AnimationChainList)", rcm);
    }

    [Fact]
    public void RawContentManager_WithoutAnimationChain_OmitsAchxBranch()
    {
        // A project that does not use AnimationChain must not reference the package's
        // types, or it would fail to compile (the package isn't referenced).
        var targets = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniAndroid };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string rcm = files["MyGameCommon/RawContentManager.cs"];
        Assert.DoesNotContain("AnimationChain", rcm);
        Assert.DoesNotContain("AchxLoader", rcm);
    }

    // ── FNA desktop export ───────────────────────────────────────────────────

    [Fact]
    public void FnaDesktop_ReferencesFnaNetAndNoOtherRuntimes()
    {
        var targets = new List<ExportTarget> { ExportTarget.FnaDesktop };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", libraryRegistry: CreateRegistry());
        var files = ExtractTextFiles(zip);

        string csproj = files["MyGame/MyGame.csproj"];

        // FNA.NET is the single framework package, at the version from PackageVersions.
        Assert.Contains("FNA.NET", csproj);
        Assert.Contains("2.2.11.2602", csproj);

        // No MonoGame, KNI, or nkast packages should leak in.
        Assert.DoesNotContain("MonoGame", csproj);
        Assert.DoesNotContain("nkast", csproj);
        Assert.DoesNotContain("KniPlatform", csproj);
        Assert.DoesNotContain("MonoGamePlatform", csproj);

        // Standard desktop entry point + shared sources.
        Assert.Contains("MyGame/Program.cs", files.Keys);
        Assert.Contains("MyGame/Game1.cs", files.Keys);
        Assert.Contains("MyGame/RawContentManager.cs", files.Keys);
    }

    [Fact]
    public void FnaDesktop_IncludesFnaCompatShim()
    {
        // FNA lacks MonoGame/KNI's optional-parameter SpriteBatch.Begin, so fiddle code authored
        // against the in-browser KNI runtime needs the shim to compile on FNA (issue #48/#54).
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.FnaDesktop, "MyGame");
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame/FnaCompat.cs", files.Keys);
        string compat = files["MyGame/FnaCompat.cs"];
        Assert.Contains("static class FnaSpriteBatchCompat", compat);
        Assert.Contains("public static void Begin(this SpriteBatch", compat);
    }

    [Fact]
    public void NonFna_OmitsFnaCompatShim()
    {
        // The shim is FNA-only; KNI/MonoGame exports have the real optional-parameter Begin.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyGame");
        Assert.DoesNotContain("MyGame/FnaCompat.cs", ExtractTextFiles(zip).Keys);
    }

    [Fact]
    public void FnaDesktop_IncludesFnaNetReadme()
    {
        var targets = new List<ExportTarget> { ExportTarget.FnaDesktop };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame/README.txt", files.Keys);
        string readme = files["MyGame/README.txt"];
        Assert.Contains("FNA.NET", readme);
        Assert.Contains("PackageReference", readme);
    }

    [Fact]
    public void RawContentManager_PremultiplyDetection_IncludesFnaAndKni()
    {
        // FNA's Texture2D.FromStream does NOT premultiply alpha, so the generated
        // RawContentManager must detect FNA (assembly FNA.NET) alongside KNI
        // (Xna.Framework.*) and premultiply. MonoGame stays out (it premultiplies itself).
        // Guards against the FNA case silently regressing back to "false".
        byte[] zip = ProjectExporter.Export(MinimalCode, [ExportTarget.FnaDesktop], "MyGame");
        var files = ExtractTextFiles(zip);

        string rcm = files["MyGame/RawContentManager.cs"];
        Assert.Contains("NeedsPremultiply", rcm);
        Assert.Contains("FNA.NET", rcm);
        Assert.Contains("Xna.Framework", rcm);
    }

    [Fact]
    public void FnaDesktop_CannotCombineWithOtherTargets()
    {
        var targets = new List<ExportTarget> { ExportTarget.FnaDesktop, ExportTarget.KniDesktopGL };
        Assert.Throws<System.ArgumentException>(() =>
            ProjectExporter.Export(MinimalCode, targets, "MyGame"));
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

    // ── Runtime shader (.fx) export (issue #39) ──────────────────────────────

    // Shaders are supplied to the exporter as a name -> HLSL-source map (not detected from the
    // game source), so MinimalCode is enough to drive these.
    static Dictionary<string, string> OneShader() => new()
    {
        ["Grayscale.fx"] = "// hlsl\nfloat4 PS() : COLOR0 { return 0; }",
    };

    [Theory]
    [InlineData(ExportTarget.KniDesktopGL)]
    [InlineData(ExportTarget.MonoGameDesktopGL)]
    [InlineData(ExportTarget.KniWindowsDX)]
    [InlineData(ExportTarget.MonoGameWindowsDX)]
    [InlineData(ExportTarget.KniBlazorGL)]
    [InlineData(ExportTarget.FnaDesktop)]
    [InlineData(ExportTarget.KniAndroid)]
    [InlineData(ExportTarget.MonoGameAndroid)]
    [InlineData(ExportTarget.MonoGameDesktopVK)] // ShadowDusk 0.12.0 Vulkan backend (issue #52)
    public void SupportsRuntimeShaders_TrueForWiredTargets(ExportTarget target)
    {
        Assert.True(ProjectExporter.SupportsRuntimeShaders(target));
    }

    [Theory]
    [InlineData(ExportTarget.MonoGameWindowsDX12)]
    public void SupportsRuntimeShaders_FalseForGatedTargets(ExportTarget target)
    {
        Assert.False(ProjectExporter.SupportsRuntimeShaders(target));
    }

    [Fact]
    public void SinglePlatform_Shader_MonoGameDesktopVK_UsesVulkanBackend()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopVK, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        // Same desktop compiler package as GL/DX/Android, but the DesktopVK runtime needs SPIR-V.
        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.Contains($@"<PackageReference Include=""ShadowDusk.Compiler"" Version=""{PackageVersions.ShadowDusk}"" />",
            files["MyGame/MyGame.csproj"]);
        Assert.Contains("PlatformTarget.Vulkan", files["MyGame/Program.cs"]);
    }

    [Fact]
    public void SinglePlatform_Shader_FnaDesktop_ShipsFxAndWiresFnaBackend()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.FnaDesktop, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        // FNA ships the .fx and references the desktop compiler (it emits legacy D3D9 .fxb).
        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.Contains($@"<PackageReference Include=""ShadowDusk.Compiler"" Version=""{PackageVersions.ShadowDusk}"" />",
            files["MyGame/MyGame.csproj"]);

        // The entry point injects EffectCompiler with the FNA backend.
        string program = files["MyGame/Program.cs"];
        Assert.Contains("new EffectCompiler()", program);
        Assert.Contains("PlatformTarget.Fna", program);

        // Effect-compiling content manager is present.
        Assert.Contains("ShaderCompiler.Compile(", files["MyGame/RawContentManager.cs"]);
    }

    [Fact]
    public void SinglePlatform_Shader_DesktopGL_ShipsFxAndWiresOpenGLCompiler()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        // The .fx SOURCE ships under Content/, keyed by its full name.
        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.Contains("float4 PS()", files["MyGame/Content/Grayscale.fx"]);

        // Desktop references the native compiler package, pinned to the shared ShadowDusk version.
        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains($@"<PackageReference Include=""ShadowDusk.Compiler"" Version=""{PackageVersions.ShadowDusk}"" />", csproj);

        // The entry point injects the concrete compiler + OpenGL backend.
        string program = files["MyGame/Program.cs"];
        Assert.Contains("using ShadowDusk.Compiler;", program);
        Assert.Contains("new EffectCompiler()", program);
        Assert.Contains("PlatformTarget.OpenGL", program);

        // The content manager compiles against the Core interface and has the Effect branch.
        string rcm = files["MyGame/RawContentManager.cs"];
        Assert.Contains("using ShadowDusk.Core;", rcm);
        Assert.Contains("public IShaderCompiler ShaderCompiler", rcm);
        Assert.Contains("typeof(T) == typeof(Effect)", rcm);
        Assert.Contains("ShaderCompiler.Compile(", rcm);
    }

    [Fact]
    public void SinglePlatform_Shader_WindowsDX_UsesDirectXBackend()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniWindowsDX, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        // Same desktop compiler package, but the WindowsDX runtime needs DXBC, not GLSL.
        Assert.Contains("ShadowDusk.Compiler", files["MyGame/MyGame.csproj"]);
        Assert.Contains("PlatformTarget.DirectX", files["MyGame/Program.cs"]);
    }

    [Fact]
    public void SinglePlatform_Shader_BlazorGL_AwaitsInitializeAsyncBeforeRun()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniBlazorGL, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        // Blazor serves content from wwwroot/.
        Assert.Contains("MyGame/wwwroot/Content/Grayscale.fx", files.Keys);

        // Browser uses the WASM compiler package, which targets net8.0-browser — so the project
        // must too, or the ShadowDusk.Wasm reference is NU1201-incompatible and its namespace won't
        // resolve (the CS0234 a real multi-project export hit). Regression guard for that.
        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains("ShadowDusk.Wasm", csproj);
        Assert.Contains("<TargetFramework>net8.0-browser</TargetFramework>", csproj);

        // The synchronous Compile inside Content.Load<Effect> needs the WASM modules loaded first,
        // so InitializeAsync must be awaited before the render loop starts.
        string razor = files["MyGame/Pages/Index.razor"];
        Assert.Contains("WasmShaderCompiler", razor);
        Assert.Contains("await _shaderCompiler.InitializeAsync();", razor);
        Assert.Contains("ShadowDusk.Core.PlatformTarget.OpenGL", razor);
    }

    [Fact]
    public void MultiPlatform_Shader_CoreInCommon_ConcreteCompilerPerPlatform()
    {
        // A mixed export: two supported GL/Web targets plus Android (now wired like desktop).
        var targets = new List<ExportTarget>
        {
            ExportTarget.KniDesktopGL,
            ExportTarget.KniBlazorGL,
            ExportTarget.KniAndroid,
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        // Common project references only the interface package; never the concrete (browser-only Wasm
        // would break the net8.0 common lib, and desktop Compiler belongs per-platform).
        string common = files["MyGameCommon/MyGameCommon.csproj"];
        Assert.Contains("ShadowDusk.Core", common);
        Assert.DoesNotContain("ShadowDusk.Compiler", common);
        Assert.DoesNotContain("ShadowDusk.Wasm", common);

        // Each supported platform brings its concrete compiler. The Blazor project must move to
        // net8.0-browser alongside its ShadowDusk.Wasm reference (NU1201 / CS0234 otherwise).
        Assert.Contains("ShadowDusk.Compiler", files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"]);
        string blazor = files["MyGame.BlazorGL/MyGame.BlazorGL.csproj"];
        Assert.Contains("ShadowDusk.Wasm", blazor);
        Assert.Contains("<TargetFramework>net8.0-browser</TargetFramework>", blazor);

        // Android gets the desktop native compiler package and entry-point injection (issue #88).
        Assert.Contains("ShadowDusk.Compiler", files["MyGame.Android/MyGame.Android.csproj"]);
        Assert.Contains("new EffectCompiler()", files["MyGame.Android/Activity1.cs"]);
        Assert.Contains("PlatformTarget.OpenGL", files["MyGame.Android/Activity1.cs"]);

        // The shared content manager (in common) has the Effect branch; .fx ships at solution root.
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGameCommon/RawContentManager.cs"]);
        Assert.Contains("Content/Grayscale.fx", files.Keys);
    }

    [Fact]
    public void NoShaders_OmitsShadowDuskReferenceAndEffectBranch()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyGame");
        var files = ExtractTextFiles(zip);

        Assert.DoesNotContain("ShadowDusk", files["MyGame/MyGame.csproj"]);
        Assert.DoesNotContain("ShadowDusk", files["MyGame/Program.cs"]);
        Assert.DoesNotContain(files.Keys, k => k.EndsWith(".fx"));

        string rcm = files["MyGame/RawContentManager.cs"];
        Assert.DoesNotContain("ShadowDusk", rcm);
        Assert.DoesNotContain("typeof(T) == typeof(Effect)", rcm);
    }

    [Fact]
    public void BlazorGL_WithoutShaders_StaysNet80_NoWasmToolsRequirement()
    {
        // A shader-free KNI Blazor export must keep building with just `dotnet restore` on net8.0;
        // it must NOT be forced to net8.0-browser (which would drag in the wasm-tools workload).
        byte[] single = ProjectExporter.Export(MinimalCode, ExportTarget.KniBlazorGL, "MyGame");
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>",
            ExtractTextFiles(single)["MyGame/MyGame.csproj"]);

        var multi = new List<ExportTarget> { ExportTarget.KniDesktopGL, ExportTarget.KniBlazorGL };
        byte[] zip = ProjectExporter.Export(MinimalCode, multi, "MyGame");
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>",
            ExtractTextFiles(zip)["MyGame.BlazorGL/MyGame.BlazorGL.csproj"]);
    }

    [Fact]
    public void SinglePlatform_Android_Shader_ShipsFxAndWiresOpenGLCompiler()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniAndroid, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.Contains($@"<PackageReference Include=""ShadowDusk.Compiler"" Version=""{PackageVersions.ShadowDusk}"" />",
            files["MyGame/MyGame.csproj"]);

        string activity = files["MyGame/Activity1.cs"];
        Assert.Contains("using ShadowDusk.Compiler;", activity);
        Assert.Contains("new EffectCompiler()", activity);
        Assert.Contains("PlatformTarget.OpenGL", activity);

        Assert.Contains("ShaderCompiler.Compile(", files["MyGame/RawContentManager.cs"]);
    }

    // ── CompilesShippedShaders: two independent axes (ContentBuildMode x ShaderCompileMode) ──

    [Theory]
    // Native requires the MATCHING content strategy to actually be selected too — Native alone (paired
    // with Raw) does nothing; ClassicMgcb wires classic MGCB, ContentBuilder wires the new Content
    // Builder's own DXC pipeline. Classic MonoGame targets compile via MGCB under Native+ClassicMgcb...
    [InlineData(ExportTarget.MonoGameDesktopGL, ShaderCompileMode.Native, ContentBuildMode.ClassicMgcb, true)]
    [InlineData(ExportTarget.MonoGameWindowsDX, ShaderCompileMode.Native, ContentBuildMode.ClassicMgcb, true)]
    [InlineData(ExportTarget.MonoGameAndroid,   ShaderCompileMode.Native, ContentBuildMode.ClassicMgcb, true)]
    // ...but DX12 has no classic MGCB and stays gated even under Native+ClassicMgcb.
    [InlineData(ExportTarget.MonoGameWindowsDX12, ShaderCompileMode.Native, ContentBuildMode.ClassicMgcb, false)]
    // DesktopVK is wired directly via ShadowDusk (0.12.0+ Vulkan backend, issue #52), so it compiles
    // regardless of shaderMode — Native+ClassicMgcb is a no-op here (DesktopVK isn't classic, see
    // IsMonoGameClassic) and it falls through to ShadowDusk rather than going gated.
    [InlineData(ExportTarget.MonoGameDesktopVK,   ShaderCompileMode.Native,     ContentBuildMode.ClassicMgcb, true)]
    [InlineData(ExportTarget.MonoGameDesktopVK,   ShaderCompileMode.ShadowDusk, ContentBuildMode.Raw,         true)]
    // ShadowDusk is the default under EVERY content strategy now (issue #52 follow-up), not just Raw —
    // Android is wired like DesktopGL (issue #88) regardless of which strategy is picked for assets.
    [InlineData(ExportTarget.MonoGameAndroid,   ShaderCompileMode.ShadowDusk, ContentBuildMode.Raw,         true)]
    [InlineData(ExportTarget.KniAndroid,        ShaderCompileMode.ShadowDusk, ContentBuildMode.Raw,         true)]
    [InlineData(ExportTarget.MonoGameDesktopGL, ShaderCompileMode.ShadowDusk, ContentBuildMode.ClassicMgcb, true)] // new default combo
    [InlineData(ExportTarget.KniDesktopGL,      ShaderCompileMode.Native,     ContentBuildMode.ClassicMgcb, true)] // MGCB ignored (not MonoGame classic); ShadowDusk
    // ContentBuilder + Native closes the DX12 gate (issue #52) on every MonoGame target.
    [InlineData(ExportTarget.MonoGameWindowsDX12, ShaderCompileMode.Native, ContentBuildMode.ContentBuilder, true)]
    [InlineData(ExportTarget.MonoGameDesktopVK,   ShaderCompileMode.Native, ContentBuildMode.ContentBuilder, true)]
    [InlineData(ExportTarget.MonoGameDesktopGL,   ShaderCompileMode.Native, ContentBuildMode.ContentBuilder, true)]
    // ContentBuilder + ShadowDusk (newly possible): still compiles on every target ShadowDusk itself
    // supports — Content Builder no longer auto-compiles shaders just because it was picked for assets.
    [InlineData(ExportTarget.MonoGameDesktopGL,   ShaderCompileMode.ShadowDusk, ContentBuildMode.ContentBuilder, true)]
    [InlineData(ExportTarget.MonoGameDesktopVK,   ShaderCompileMode.ShadowDusk, ContentBuildMode.ContentBuilder, true)]
    // DX12 + ContentBuilder + ShadowDusk: ShadowDusk 0.12.0 has no DirectX12 PlatformTarget yet (its
    // README still lists DX12 as "Not yet" — verified against the NuGet feed and PlatformTarget.cs at
    // implementation time), so unlike DesktopVK this stays gated under the ShadowDusk default. Flip to
    // true once GetShaderExportInfo gains a MonoGameWindowsDX12 case.
    [InlineData(ExportTarget.MonoGameWindowsDX12, ShaderCompileMode.ShadowDusk, ContentBuildMode.ContentBuilder, false)]
    // ...but it's a no-op for non-MonoGame targets (they ignore it and keep their existing strategy).
    [InlineData(ExportTarget.KniDesktopGL,  ShaderCompileMode.ShadowDusk, ContentBuildMode.ContentBuilder, true)]
    [InlineData(ExportTarget.FnaDesktop,    ShaderCompileMode.ShadowDusk, ContentBuildMode.ContentBuilder, true)]
    public void CompilesShippedShaders_ReflectsModeAndTarget(ExportTarget target, ShaderCompileMode shaderMode, ContentBuildMode contentMode, bool expected)
    {
        Assert.Equal(expected, ProjectExporter.CompilesShippedShaders(target, shaderMode, contentMode));
    }

    [Fact]
    public void DX12_ContentBuilder_ShadowDusk_StaysGated_UntilShadowDuskAddsDx12()
    {
        // Dedicated regression guard (beyond the theory row above) for this specific combo, since it's
        // the one case in this whole rework gated by a live external dependency rather than by design:
        // ShadowDusk hasn't shipped a DX12 backend as of 0.12.0. When it does, GetShaderExportInfo gains
        // a MonoGameWindowsDX12 case and this flips to Assert.True with no other code change needed.
        Assert.False(ProjectExporter.CompilesShippedShaders(
            ExportTarget.MonoGameWindowsDX12, ShaderCompileMode.ShadowDusk, ContentBuildMode.ContentBuilder));
    }

    // ── Classic MGCB mode (ContentBuildMode.ClassicMgcb) — shaders ─────────────

    [Fact]
    public void SinglePlatform_ClassicMgcbNative_DesktopGL_BuildsXnbNotShadowDusk()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        // The .fx SOURCE still ships (the pipeline compiles it at build time) plus a Content.mgcb.
        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.Contains("MyGame/Content/Content.mgcb", files.Keys);

        string mgcb = files["MyGame/Content/Content.mgcb"];
        Assert.Contains("/importer:EffectImporter", mgcb);
        Assert.Contains("/processor:EffectProcessor", mgcb);
        Assert.Contains("/build:Grayscale.fx", mgcb);
        Assert.Contains("/platform:DesktopGL", mgcb);
        Assert.Contains("/profile:HiDef", mgcb);

        // The csproj hands the .fx to the content pipeline and ships NO ShadowDusk — a canonical
        // MonoGame project. Every classic-pipeline extension is dropped from the wholesale copy so only
        // the built .xnb lands in the output (single-platform uses Remove because they're SDK-default
        // None items) — .fx plus .png/.wav (ClassicMgcb's asset importers, even though none are shipped
        // in this export) plus Content.mgcb itself, since useClassicMgcbAssets is mode/target-driven,
        // not asset-existence-driven.
        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<MonoGameContentReference Include=""Content\Content.mgcb"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\**\*.fx"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\**\*.png"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\**\*.wav"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\Content.mgcb"" />", csproj);
        Assert.DoesNotContain("ShadowDusk", csproj);

        // No runtime compiler is injected, and the content manager has no ShadowDusk Effect branch —
        // Content.Load<Effect> falls through to the stock pipeline loader reading the built .xnb.
        Assert.DoesNotContain("ShadowDusk", files["MyGame/Program.cs"]);
        string rcm = files["MyGame/RawContentManager.cs"];
        Assert.DoesNotContain("ShadowDusk", rcm);
        Assert.DoesNotContain("typeof(T) == typeof(Effect)", rcm);
    }

    [Fact]
    public void SinglePlatform_ClassicMgcbNative_WindowsDX_UsesWindowsPlatformToken()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameWindowsDX, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        Assert.Contains("/platform:Windows", files["MyGame/Content/Content.mgcb"]);
        Assert.Contains(@"<MonoGameContentReference Include=""Content\Content.mgcb"" />", files["MyGame/MyGame.csproj"]);
    }

    [Fact]
    public void SinglePlatform_Shader_MonoGameDesktopGL_DefaultModeStaysShadowDusk()
    {
        // No shaderCompileMode/contentBuildMode arguments: the defaults must remain ShadowDusk/Raw so
        // existing exports are unchanged. (Regression guard for the new opt-ins not flipping defaults.)
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        Assert.Contains("ShadowDusk.Compiler", files["MyGame/MyGame.csproj"]);
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
        Assert.DoesNotContain("MyGame/Content/Content.mgcb", files.Keys);
    }

    [Fact]
    public void ClassicMgcbNative_NonMonoGameTarget_IgnoresMgcbAndUsesShadowDusk()
    {
        // Native is honored only on classic MonoGame targets; a KNI export must ignore it (and the
        // ClassicMgcb content strategy, which is equally meaningless for KNI) and keep the ShadowDusk
        // wiring (KNI has no MGCB tool).
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        Assert.DoesNotContain("MyGame/Content/Content.mgcb", files.Keys);
        Assert.Contains("ShadowDusk.Compiler", files["MyGame/MyGame.csproj"]);
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
    }

    [Fact]
    public void MultiPlatform_ClassicMgcbNative_SharedMgcbAndNoShadowDuskAnywhere()
    {
        // Two classic MonoGame heads in ClassicMgcb+Native mode → one shared Content.mgcb, each head
        // references it, and the common project is fully canonical (no ShadowDusk.Core, no Effect branch).
        var targets = new List<ExportTarget>
        {
            ExportTarget.MonoGameDesktopGL,
            ExportTarget.MonoGameWindowsDX,
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        // One shared .mgcb at the solution-root Content/, the .fx alongside it.
        Assert.Contains("Content/Content.mgcb", files.Keys);
        Assert.Contains("Content/Grayscale.fx", files.Keys);

        // Each head references the shared .mgcb with the relative path.
        Assert.Contains(@"<MonoGameContentReference Include=""..\Content\Content.mgcb"" />",
            files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"]);
        Assert.Contains(@"<MonoGameContentReference Include=""..\Content\Content.mgcb"" />",
            files["MyGame.WindowsDX/MyGame.WindowsDX.csproj"]);

        // Canonical: the shared library never references ShadowDusk and has no Effect-compiling branch.
        string common = files["MyGameCommon/MyGameCommon.csproj"];
        Assert.DoesNotContain("ShadowDusk", common);
        Assert.DoesNotContain("typeof(T) == typeof(Effect)", files["MyGameCommon/RawContentManager.cs"]);
        Assert.DoesNotContain("ShadowDusk", files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"]);
    }

    [Fact]
    public void MultiPlatform_ClassicMgcbNative_ClassicUsesMgcbWhileDx12Gated()
    {
        // A classic head (MGCB) alongside a DX12 head (no classic MGCB → gated). DX12 ships the .fx but
        // gets neither an MGCB reference nor ShadowDusk; no head uses ShadowDusk, so the common stays clean.
        var targets = new List<ExportTarget>
        {
            ExportTarget.MonoGameDesktopGL,
            ExportTarget.MonoGameWindowsDX12,
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        Assert.Contains(@"<MonoGameContentReference Include=""..\Content\Content.mgcb"" />",
            files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"]);
        // DX12 is gated: no content reference, no ShadowDusk.
        string dx12 = files["MyGame.WindowsDX12/MyGame.WindowsDX12.csproj"];
        Assert.DoesNotContain("MonoGameContentReference", dx12);
        Assert.DoesNotContain("ShadowDusk", dx12);
        Assert.DoesNotContain("ShadowDusk", files["MyGameCommon/MyGameCommon.csproj"]);
    }

    // ── Classic MGCB mode (ContentBuildMode.ClassicMgcb) — assets ───────────────

    [Fact]
    public void ClassicMgcbAssets_PngAndWav_EmitColorKeyDisabledImporterBlocks()
    {
        // Regression guard for the magenta color-key bug: MonoGame's TextureProcessor defaults
        // ColorKeyEnabled to TRUE (color-keys pure magenta to transparent), which would silently punch
        // holes in a shipped .png unless explicitly forced off.
        var assets = new Dictionary<string, byte[]>
        {
            ["sprite.png"] = [1, 2, 3],
            ["sprite"] = [1, 2, 3], // extensionless dedup key InMemoryContentManager also stores
            ["blip.wav"] = [4, 5, 6],
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        string mgcb = files["MyGame/Content/Content.mgcb"];
        Assert.Contains("#begin sprite.png", mgcb);
        Assert.Contains("/importer:TextureImporter", mgcb);
        Assert.Contains("/processor:TextureProcessor", mgcb);
        Assert.Contains("/processorParam:ColorKeyEnabled=False", mgcb);
        Assert.Contains("/processorParam:TextureFormat=Color", mgcb);
        Assert.Contains("/build:sprite.png", mgcb);

        Assert.Contains("#begin blip.wav", mgcb);
        Assert.Contains("/importer:WavImporter", mgcb);
        Assert.Contains("/processor:SoundEffectProcessor", mgcb);
        Assert.Contains("/processorParam:Quality=Best", mgcb);
        Assert.Contains("/build:blip.wav", mgcb);
    }

    [Fact]
    public void ClassicMgcbAssets_MonoGameContentReference_PresentWithZeroNativeShaders()
    {
        // Regression guard for the widened <MonoGameContentReference> condition: ClassicMgcb assets
        // alone — no shaders at all, let alone Native ones — must still get Content.mgcb referenced.
        var assets = new Dictionary<string, byte[]> { ["sprite.png"] = [1, 2, 3] };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<MonoGameContentReference Include=""Content\Content.mgcb"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\**\*.png"" />", csproj);
        Assert.DoesNotContain(@"<None Remove=""Content\**\*.fx"" />", csproj); // no shaders, nothing to compile natively
        Assert.DoesNotContain("ShadowDusk", csproj); // no shader tabs at all in this export
    }

    [Fact]
    public void SinglePlatform_ClassicMgcbAssets_ShadowDuskShaders_NewDefaultCombo()
    {
        // The new default combo: ClassicMgcb compiles assets to .xnb at build time; ShadowDusk still
        // compiles shipped .fx at runtime (issue #52 follow-up — ShadowDusk is the default shader
        // compiler under every content strategy, not just Raw).
        var assets = new Dictionary<string, byte[]> { ["sprite.png"] = [1, 2, 3] };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, shaders: OneShader(), contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        // The .png is compiled via MGCB...
        string mgcb = files["MyGame/Content/Content.mgcb"];
        Assert.Contains("#begin sprite.png", mgcb);
        Assert.DoesNotContain("#begin Grayscale.fx", mgcb); // the shader is NOT native-compiled here

        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<MonoGameContentReference Include=""Content\Content.mgcb"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\**\*.png"" />", csproj);
        Assert.DoesNotContain(@"<None Remove=""Content\**\*.fx"" />", csproj); // .fx still ships raw

        // ...while the .fx still ships raw and recompiles at runtime via ShadowDusk.
        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.Contains("ShadowDusk.Compiler", csproj);
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
    }

    [Fact]
    public void MultiPlatform_ClassicMgcbAssets_MixedHeads_OnlyClassicHeadExcludesPngFromRawCopy()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameWindowsDX12 };
        var assets = new Dictionary<string, byte[]> { ["sprite.png"] = [1, 2, 3] };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            assets: assets, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        // DesktopGL is classic — it excludes the compiled .png (and .wav, mode/target-driven regardless
        // of which extensions are actually shipped) from its raw copy and references the shared .mgcb.
        string desktopGL = files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"];
        Assert.Contains(@"Exclude=""..\Content\**\*.png;..\Content\**\*.wav;..\Content\Content.mgcb""", desktopGL);
        Assert.Contains(@"<MonoGameContentReference Include=""..\Content\Content.mgcb"" />", desktopGL);

        // WindowsDX12 has no classic MGCB — it keeps copying everything raw, .png included.
        string dx12 = files["MyGame.WindowsDX12/MyGame.WindowsDX12.csproj"];
        Assert.DoesNotContain("Exclude=", dx12);
        Assert.DoesNotContain("MonoGameContentReference", dx12);
    }

    // ── MonoGame 3.8.5 GA Content Builder mode (ContentBuildMode.ContentBuilder) ─────

    [Fact]
    public void ContentBuilder_DefaultMode_EmitsNoContentProject()
    {
        // Regression guard: exporting without a contentBuildMode argument must not emit any
        // {projectName}.Content/... entries — the new opt-in must not flip the default.
        var assets = new Dictionary<string, byte[]> { ["x.png"] = [0] };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, shaders: OneShader());
        var files = ExtractTextFiles(zip);

        Assert.DoesNotContain(files.Keys, k => k.Contains("MyGame.Content"));
    }

    [Fact]
    public void SinglePlatform_ContentBuilder_ShadowDuskShaders_RoutesAssetsOnlyShaderStaysRuntime()
    {
        // The default shader compiler is ShadowDusk under EVERY content strategy, including Content
        // Builder (issue #52 follow-up) — Content Builder no longer supersedes shader compilation.
        var assets = new Dictionary<string, byte[]> { ["x.png"] = [1, 2, 3] };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, shaders: OneShader(), contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        // The asset routes into the Content project's Assets/ folder...
        Assert.Contains("MyGame.Content/Assets/x.png", files.Keys);
        // ...but the shader stays in the head's own Content/ and keeps its ShadowDusk wiring.
        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.DoesNotContain(files.Keys, k => k.Contains("MyGame.Content/Assets/Grayscale.fx"));

        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<Import Project=""..\MyGame.Content\BuildContent.targets"" />", csproj);
        Assert.Contains("ShadowDusk.Compiler", csproj);
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
    }

    [Fact]
    public void SinglePlatform_ContentBuilder_ShadowDuskShaders_HeadContentShaderStillWiredForCopyToOutput()
    {
        // Regression guard: under Content Builder mode with the default ShadowDusk shader compiler, the
        // .fx stays in the head's own Content/ (not routed into {projectName}.Content/Assets/), so it
        // still needs the wholesale Content/ copy-to-output wiring — or the shipped .fx never reaches
        // the build output and RawContentManager can't find it to compile at runtime.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            shaders: OneShader(), contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<None Update=""Content\**\*"">", csproj);
        Assert.Contains("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", csproj);
    }

    [Fact]
    public void SinglePlatform_ContentBuilder_NativeShaders_RoutesAssetsAndShadersToSeparateContentProject()
    {
        var assets = new Dictionary<string, byte[]> { ["x.png"] = [1, 2, 3] };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native,
            contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        // The three generated Content Builder project files.
        Assert.Contains("MyGame.Content/MyGame.Content.csproj", files.Keys);
        Assert.Contains("MyGame.Content/BuildContent.targets", files.Keys);
        Assert.Contains("MyGame.Content/Builder/Builder.cs", files.Keys);

        // With Native selected, the asset AND shader both route into the Content project's Assets/
        // folder instead of the head's own Content/, so Builder.cs's WildcardRule("*") picks them up.
        Assert.Contains("MyGame.Content/Assets/x.png", files.Keys);
        Assert.Contains("MyGame.Content/Assets/Grayscale.fx", files.Keys);

        // The head's own Content/ has nothing pipeline-related left in it.
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("MyGame/Content/"));

        // The head csproj imports the Content project's build step and ships no ShadowDusk/MGCB wiring.
        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<Import Project=""..\MyGame.Content\BuildContent.targets"" />", csproj);
        Assert.DoesNotContain("ShadowDusk", csproj);
        Assert.DoesNotContain(@"<None Include=""Content", csproj);
        Assert.DoesNotContain("MonoGameContentReference", csproj);

        Assert.Contains(@"<Project Path=""MyGame.Content\MyGame.Content.csproj"" />", files["MyGame.slnx"]);
    }

    [Fact]
    public void SinglePlatform_ContentBuilder_NativeShaders_ClosesDx12ShaderGate()
    {
        // Issue #52: the Content Builder isn't limited to mgcb.exe's platform list, so DX12 (which
        // still has no ShadowDusk backend, unlike DesktopVK) finally gets a way to compile shipped .fx
        // — but only when Native is explicitly selected; the default ShadowDusk mode leaves it gated
        // (see DX12_ContentBuilder_ShadowDusk_StaysGated_UntilShadowDuskAddsDx12).
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameWindowsDX12, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<Import Project=""..\MyGame.Content\BuildContent.targets"" />", csproj);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.Native"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Runtime.Windows.DX12"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);

        Assert.True(ProjectExporter.CompilesShippedShaders(
            ExportTarget.MonoGameWindowsDX12, ShaderCompileMode.Native, ContentBuildMode.ContentBuilder));
    }

    [Fact]
    public void MultiPlatform_ContentBuilder_NativeShaders_OneSharedContentProjectReferencedByBothHeads()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameWindowsDX };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.Native, contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        // Exactly one shared Content project (a Dictionary key can't repeat, so Contains is sufficient).
        Assert.Contains("MyGame.Content/MyGame.Content.csproj", files.Keys);
        Assert.Contains("MyGame.Content/BuildContent.targets", files.Keys);
        Assert.Contains("MyGame.Content/Builder/Builder.cs", files.Keys);
        Assert.Contains("MyGame.Content/Assets/Grayscale.fx", files.Keys);

        Assert.Contains(@"<Import Project=""..\MyGame.Content\BuildContent.targets"" />",
            files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"]);
        Assert.Contains(@"<Import Project=""..\MyGame.Content\BuildContent.targets"" />",
            files["MyGame.WindowsDX/MyGame.WindowsDX.csproj"]);
        Assert.Contains(@"<Project Path=""MyGame.Content\MyGame.Content.csproj"" />", files["MyGame.slnx"]);
    }

    [Fact]
    public void ContentBuilder_NonMonoGameTargets_AreNoOp()
    {
        // ContentBuildMode.ContentBuilder is honored only on MonoGame targets (UsesContentBuilder
        // requires target.IsMonoGame()); KNI and FNA must build exactly as if it were never passed.
        byte[] kniZip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyGame",
            shaders: OneShader(), contentBuildMode: ContentBuildMode.ContentBuilder);
        var kniFiles = ExtractTextFiles(kniZip);
        Assert.DoesNotContain(kniFiles.Keys, k => k.Contains("MyGame.Content"));
        Assert.Contains("ShadowDusk.Compiler", kniFiles["MyGame/MyGame.csproj"]);

        byte[] fnaZip = ProjectExporter.Export(MinimalCode, ExportTarget.FnaDesktop, "MyGame",
            shaders: OneShader(), contentBuildMode: ContentBuildMode.ContentBuilder);
        var fnaFiles = ExtractTextFiles(fnaZip);
        Assert.DoesNotContain(fnaFiles.Keys, k => k.Contains("MyGame.Content"));
        Assert.Contains("ShadowDusk.Compiler", fnaFiles["MyGame/MyGame.csproj"]);
    }

    [Fact]
    public void ContentBuilder_ClassicTarget_KeepsMgcbToolManifestAlongsideImport()
    {
        // MonoGame.Content.Builder.Task (and its dotnet-tools.json manifest) stays installed on classic
        // targets even in Content Builder mode — Apos.Shapes/Gum ship their own buildTransitive .mgcb,
        // invisible to Builder.cs (which only walks the fiddle's own Assets/ folder). Both pipelines
        // coexist on classic targets; mirrors MonoGame_StableVersion_EmitsMgcbToolManifest.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame/.config/dotnet-tools.json", files.Keys);
        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Content.Builder.Task"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);
        Assert.Contains(@"<Import Project=""..\MyGame.Content\BuildContent.targets"" />", csproj);
    }

    [Fact]
    public void ContentBuilder_ExcludesNonPipelineFormatsFromAssetsFolder()
    {
        // .achx/.fnt (and the rest of NonPipelineAssetExtensions) have no pipeline importer —
        // WildcardRule("*") would choke on them — so they keep shipping raw into the head's own
        // Content/ folder instead of {projectName}.Content/Assets/.
        var assets = new Dictionary<string, byte[]>
        {
            ["sprite.png"] = [1],
            ["sprite"] = [1], // extensionless dedup key InMemoryContentManager also stores
            ["font.fnt"] = [2],
            ["anim.achx"] = [3],
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, contentBuildMode: ContentBuildMode.ContentBuilder);
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame.Content/Assets/sprite.png", files.Keys);
        Assert.DoesNotContain("MyGame.Content/Assets/font.fnt", files.Keys);
        Assert.DoesNotContain("MyGame.Content/Assets/anim.achx", files.Keys);
        Assert.Contains("MyGame/Content/font.fnt", files.Keys);
        Assert.Contains("MyGame/Content/anim.achx", files.Keys);

        // The extensionless dedup key never gets written anywhere.
        Assert.DoesNotContain(files.Keys, k => k.EndsWith("/sprite"));
    }

    [Fact]
    public void ClassicMgcb_NonPipelineFormatsStayRawAlongsidePipelineFormats()
    {
        // NonPipelineAssetExtensions rename regression guard: the same exclusion set also gates
        // ClassicMgcb's asset routing, not just Content Builder's. .png (has a pipeline importer) gets
        // excluded from the raw copy; .fnt (doesn't) keeps copying raw like every other asset.
        var assets = new Dictionary<string, byte[]>
        {
            ["sprite.png"] = [1],
            ["font.fnt"] = [2],
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            assets: assets, contentBuildMode: ContentBuildMode.ClassicMgcb);
        var files = ExtractTextFiles(zip);

        // Both assets physically ship in the same Content/ folder — ClassicMgcb needs no separate
        // Assets/ destination (only the csproj-level copy-to-output exclusion differs per extension).
        Assert.Contains("MyGame/Content/sprite.png", files.Keys);
        Assert.Contains("MyGame/Content/font.fnt", files.Keys);

        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<None Remove=""Content\**\*.png"" />", csproj);
        Assert.DoesNotContain(@"<None Remove=""Content\**\*.fnt"" />", csproj);
    }
}
