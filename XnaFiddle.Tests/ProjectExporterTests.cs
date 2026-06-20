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

    // ── MonoGame DX12 (3.8.5 preview) export ─────────────────────────────────

    [Fact]
    public void MonoGameDX12_SinglePlatform_UsesNativeFrameworkAndDx12Runtime()
    {
        var targets = new List<ExportTarget> { ExportTarget.MonoGameWindowsDX12 };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
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
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
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
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
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
    public void CommonCsproj_MonoGame_PreviewVersion_UsesNativeFramework()
    {
        // MonoGame 3.8.5+ shared libraries compile against the renderer-agnostic
        // MonoGame.Framework.Native (the mg2dstartkit convention), not DesktopGL.
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameWindowsDX };
        byte[] zip = ProjectExporter.Export(
            MinimalCode, targets, "MyGame",
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.Native"" Version=""{PackageVersions.MonoGameFrameworkPreview}"" PrivateAssets=""All"" />",
            common);
        // The common project must not pin a concrete backend as its framework reference.
        Assert.DoesNotContain("MonoGame.Framework.DesktopGL", common);
    }

    [Fact]
    public void CommonCsproj_MonoGame_DefaultVersion_UsesDesktopGLFramework()
    {
        // Stable 3.8.4 keeps the legacy DesktopGL shared reference (no .Native package yet).
        var targets = new List<ExportTarget> { ExportTarget.MonoGameDesktopGL, ExportTarget.MonoGameWindowsDX };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame");
        var files = ExtractTextFiles(zip);
        string common = files["MyGameCommon/MyGameCommon.csproj"];

        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""{PackageVersions.MonoGameFramework}"" PrivateAssets=""All"" />",
            common);
        Assert.DoesNotContain("MonoGame.Framework.Native", common);
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
        // No monoGameVersion argument → stable default for both lockstep packages.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame");
        var files = ExtractTextFiles(zip);
        string csproj = files["MyGame/MyGame.csproj"];

        Assert.Equal("3.8.4.1", PackageVersions.MonoGameFramework);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Content.Builder.Task"" Version=""{PackageVersions.MonoGameFramework}"" />",
            csproj);
    }

    [Fact]
    public void MonoGame_PreviewVersion_PinsPreviewForFrameworkAndBuilder()
    {
        // Passing the preview version must move BOTH the framework package and the content
        // builder package in lockstep onto the prerelease.
        byte[] zip = ProjectExporter.Export(
            MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
        var files = ExtractTextFiles(zip);
        string csproj = files["MyGame/MyGame.csproj"];

        Assert.Equal("3.8.5-preview.6", PackageVersions.MonoGameFrameworkPreview);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""{PackageVersions.MonoGameFrameworkPreview}"" />",
            csproj);
        Assert.Contains(
            $@"<PackageReference Include=""MonoGame.Content.Builder.Task"" Version=""{PackageVersions.MonoGameFrameworkPreview}"" />",
            csproj);
        // The stable version must not leak into the preview export.
        Assert.DoesNotContain(PackageVersions.MonoGameFramework + @"""", csproj);
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
    public void MonoGame_PreviewVersion_EmitsMgcbToolManifest()
    {
        // The 3.8.5 preview still uses the legacy content-builder task (dotnet-mgcb exists at
        // 3.8.5-preview.6), so the manifest is emitted for all MonoGame versions and pins the
        // tool to the preview version in lockstep with the framework/builder packages.
        byte[] zip = ProjectExporter.Export(
            MinimalCode, ExportTarget.MonoGameDesktopGL, "MyFiddle",
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyFiddle/.config/dotnet-tools.json", files.Keys);
        string manifest = files["MyFiddle/.config/dotnet-tools.json"];
        Assert.Contains("dotnet-mgcb", manifest);
        Assert.Contains(PackageVersions.MonoGameFrameworkPreview, manifest);
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
    public void SupportsRuntimeShaders_TrueForWiredTargets(ExportTarget target)
    {
        Assert.True(ProjectExporter.SupportsRuntimeShaders(target));
    }

    [Theory]
    [InlineData(ExportTarget.KniAndroid)]
    [InlineData(ExportTarget.MonoGameAndroid)]
    [InlineData(ExportTarget.MonoGameWindowsDX12)]
    [InlineData(ExportTarget.MonoGameDesktopVK)]
    public void SupportsRuntimeShaders_FalseForGatedTargets(ExportTarget target)
    {
        Assert.False(ProjectExporter.SupportsRuntimeShaders(target));
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
        // A mixed export: two supported GL/DX/Web targets plus a gated one (Android).
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

        // The gated platform builds but wires no compiler.
        Assert.DoesNotContain("ShadowDusk", files["MyGame.Android/MyGame.Android.csproj"]);

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
    public void SinglePlatform_GatedTarget_Shader_ShipsFxButWiresNoCompiler()
    {
        // Android is gated: the .fx still ships (harmless), but no compiler is referenced or injected.
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniAndroid, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        Assert.Contains("MyGame/Content/Grayscale.fx", files.Keys);
        Assert.DoesNotContain("ShadowDusk", files["MyGame/MyGame.csproj"]);
        Assert.DoesNotContain("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
    }

    // ── MGCB shader mode (ShaderCompileMode.ContentPipeline) ──────────────────

    [Theory]
    // ContentPipeline mode compiles classic MonoGame targets via MGCB (so they're not gated)...
    [InlineData(ExportTarget.MonoGameDesktopGL, ShaderCompileMode.ContentPipeline, true)]
    [InlineData(ExportTarget.MonoGameWindowsDX, ShaderCompileMode.ContentPipeline, true)]
    [InlineData(ExportTarget.MonoGameAndroid,   ShaderCompileMode.ContentPipeline, true)]
    // ...but DX12/VK have no classic MGCB and stay gated even in ContentPipeline mode.
    [InlineData(ExportTarget.MonoGameWindowsDX12, ShaderCompileMode.ContentPipeline, false)]
    [InlineData(ExportTarget.MonoGameDesktopVK,   ShaderCompileMode.ContentPipeline, false)]
    // In ShadowDusk mode, Android is gated (no device backend) but DesktopGL is not.
    [InlineData(ExportTarget.MonoGameAndroid,   ShaderCompileMode.ShadowDusk, false)]
    [InlineData(ExportTarget.MonoGameDesktopGL, ShaderCompileMode.ShadowDusk, true)]
    [InlineData(ExportTarget.KniDesktopGL,      ShaderCompileMode.ContentPipeline, true)] // MGCB ignored; ShadowDusk
    public void CompilesShippedShaders_ReflectsModeAndTarget(ExportTarget target, ShaderCompileMode mode, bool expected)
    {
        Assert.Equal(expected, ProjectExporter.CompilesShippedShaders(target, mode));
    }

    [Fact]
    public void SinglePlatform_Shader_MonoGameDesktopGL_ContentPipeline_BuildsXnbNotShadowDusk()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.ContentPipeline);
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
        // MonoGame project. The .fx/.mgcb are dropped from the wholesale copy so only the built .xnb
        // is output (single-platform uses Remove because the .fx are SDK-default None items).
        string csproj = files["MyGame/MyGame.csproj"];
        Assert.Contains(@"<MonoGameContentReference Include=""Content\Content.mgcb"" />", csproj);
        Assert.Contains(@"<None Remove=""Content\**\*.fx"" />", csproj);
        Assert.DoesNotContain("ShadowDusk", csproj);

        // No runtime compiler is injected, and the content manager has no ShadowDusk Effect branch —
        // Content.Load<Effect> falls through to the stock pipeline loader reading the built .xnb.
        Assert.DoesNotContain("ShadowDusk", files["MyGame/Program.cs"]);
        string rcm = files["MyGame/RawContentManager.cs"];
        Assert.DoesNotContain("ShadowDusk", rcm);
        Assert.DoesNotContain("typeof(T) == typeof(Effect)", rcm);
    }

    [Fact]
    public void SinglePlatform_Shader_MonoGameWindowsDX_ContentPipeline_UsesWindowsPlatformToken()
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameWindowsDX, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.ContentPipeline);
        var files = ExtractTextFiles(zip);

        Assert.Contains("/platform:Windows", files["MyGame/Content/Content.mgcb"]);
        Assert.Contains(@"<MonoGameContentReference Include=""Content\Content.mgcb"" />", files["MyGame/MyGame.csproj"]);
    }

    [Fact]
    public void SinglePlatform_Shader_MonoGameDesktopGL_DefaultModeStaysShadowDusk()
    {
        // No shaderCompileMode argument: the default must remain the runtime ShadowDusk path so existing
        // exports are unchanged. (Regression guard for the new opt-in not flipping the default.)
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.MonoGameDesktopGL, "MyGame", shaders: OneShader());
        var files = ExtractTextFiles(zip);

        Assert.Contains("ShadowDusk.Compiler", files["MyGame/MyGame.csproj"]);
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
        Assert.DoesNotContain("MyGame/Content/Content.mgcb", files.Keys);
    }

    [Fact]
    public void ContentPipelineMode_NonMonoGameTarget_IgnoresMgcbAndUsesShadowDusk()
    {
        // ContentPipeline is honored only on classic MonoGame targets; a KNI export must ignore it and
        // keep the ShadowDusk wiring (KNI has no MGCB tool).
        byte[] zip = ProjectExporter.Export(MinimalCode, ExportTarget.KniDesktopGL, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.ContentPipeline);
        var files = ExtractTextFiles(zip);

        Assert.DoesNotContain("MyGame/Content/Content.mgcb", files.Keys);
        Assert.Contains("ShadowDusk.Compiler", files["MyGame/MyGame.csproj"]);
        Assert.Contains("typeof(T) == typeof(Effect)", files["MyGame/RawContentManager.cs"]);
    }

    [Fact]
    public void MultiPlatform_Shader_ContentPipeline_SharedMgcbAndNoShadowDuskAnywhere()
    {
        // Two classic MonoGame heads in ContentPipeline mode → one shared Content.mgcb, each head
        // references it, and the common project is fully canonical (no ShadowDusk.Core, no Effect branch).
        var targets = new List<ExportTarget>
        {
            ExportTarget.MonoGameDesktopGL,
            ExportTarget.MonoGameWindowsDX,
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.ContentPipeline);
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
    public void MultiPlatform_Shader_ContentPipeline_ClassicUsesMgcbWhileDx12Gated()
    {
        // A classic head (MGCB) alongside a DX12 head (no classic MGCB → gated). DX12 ships the .fx but
        // gets neither an MGCB reference nor ShadowDusk; no head uses ShadowDusk, so the common stays clean.
        var targets = new List<ExportTarget>
        {
            ExportTarget.MonoGameDesktopGL,
            ExportTarget.MonoGameWindowsDX12,
        };
        byte[] zip = ProjectExporter.Export(MinimalCode, targets, "MyGame",
            shaders: OneShader(), shaderCompileMode: ShaderCompileMode.ContentPipeline,
            monoGameVersion: PackageVersions.MonoGameFrameworkPreview);
        var files = ExtractTextFiles(zip);

        Assert.Contains(@"<MonoGameContentReference Include=""..\Content\Content.mgcb"" />",
            files["MyGame.DesktopGL/MyGame.DesktopGL.csproj"]);
        // DX12 is gated: no content reference, no ShadowDusk.
        string dx12 = files["MyGame.WindowsDX12/MyGame.WindowsDX12.csproj"];
        Assert.DoesNotContain("MonoGameContentReference", dx12);
        Assert.DoesNotContain("ShadowDusk", dx12);
        Assert.DoesNotContain("ShadowDusk", files["MyGameCommon/MyGameCommon.csproj"]);
    }
}
