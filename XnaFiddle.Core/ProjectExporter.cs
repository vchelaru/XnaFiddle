using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XnaFiddle
{
    public enum ExportTarget
    {
        KniDesktopGL,
        KniWindowsDX,
        KniAndroid,
        KniBlazorGL,
        MonoGameDesktopGL,
        MonoGameWindowsDX,
        MonoGameAndroid,
        // MonoGame 3.8.5+ next-gen backends: DirectX 12 (Windows) and Vulkan (cross-platform).
        // Both use the unified managed framework (MonoGame.Framework.Native) plus native-binary
        // MonoGame.Runtime.* package(s), not the per-platform MonoGame.Framework.* + MGCB path.
        // Preview-only — the packages ship only as 3.8.5 previews, so the UI gates them behind the
        // preview version.
        MonoGameWindowsDX12,
        MonoGameDesktopVK,
        // FNA is a third runtime category, neither KNI nor MonoGame. It is exported
        // via the FNA.NET NuGet package and is a single desktop target only.
        FnaDesktop
    }

    /// <summary>
    /// Generates a complete, buildable project (zip) from the user's fiddle code.
    /// NuGet package versions come from <see cref="PackageVersions"/>, which is
    /// auto-generated from MSBuild properties in XnaFiddle.BlazorGL.csproj.
    /// To update a library version, change it in the csproj — it flows here automatically.
    /// </summary>
    public static class ProjectExporter
    {
        struct NuGetPackage
        {
            public string Id;
            public string Version;
        }

        // The 10 core KNI framework assemblies, each published as a separate NuGet package.
        static readonly string[] KniFrameworkPackages =
        [
            "nkast.Xna.Framework",
            "nkast.Xna.Framework.Content",
            "nkast.Xna.Framework.Graphics",
            "nkast.Xna.Framework.Audio",
            "nkast.Xna.Framework.Media",
            "nkast.Xna.Framework.Input",
            "nkast.Xna.Framework.Game",
            "nkast.Xna.Framework.Devices",
            "nkast.Xna.Framework.Storage",
            "nkast.Xna.Framework.XR",
        ];

        static string GetPlatformSuffix(ExportTarget target) => target switch
        {
            ExportTarget.KniDesktopGL      => "DesktopGL",
            ExportTarget.KniWindowsDX      => "WindowsDX",
            ExportTarget.KniAndroid        => "Android",
            ExportTarget.KniBlazorGL       => "BlazorGL",
            ExportTarget.MonoGameDesktopGL   => "DesktopGL",
            ExportTarget.MonoGameWindowsDX   => "WindowsDX",
            ExportTarget.MonoGameAndroid     => "Android",
            ExportTarget.MonoGameWindowsDX12 => "WindowsDX12",
            ExportTarget.MonoGameDesktopVK   => "DesktopVK",
            ExportTarget.FnaDesktop          => "Desktop",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        // ── Runtime shader (.fx) export (issue #39) ──────────────────────────────
        // Exported projects ship the .fx SOURCE and compile it to .mgfx at runtime via ShadowDusk,
        // exactly like the in-browser editor — no XNB, no MGCB. The shared/common project compiles
        // against ShadowDusk.Core's IShaderCompiler interface; each per-platform project supplies the
        // concrete compiler and the backend. Targets absent from GetShaderExportInfo are *gated*: the
        // .fx still ships but no compiler is wired — Android/iOS (no ShadowDusk device backend) and
        // MonoGame DX12/Vulkan are tracked in issue #52.
        struct ShaderExportInfo
        {
            public bool Supported;
            public string Package;            // concrete per-platform package: ShadowDusk.Compiler / ShadowDusk.Wasm
            public string CompilerNamespace;  // namespace of CompilerType (for the entry-point using)
            public string CompilerType;       // EffectCompiler (desktop) / WasmShaderCompiler (browser)
            public string PlatformTarget;     // ShadowDusk.Core.PlatformTarget enum value: OpenGL / DirectX / Fna
            public bool IsBrowser;            // browser compiler must be InitializeAsync()'d before the sync Compile()
        }

        static ShaderExportInfo GetShaderExportInfo(ExportTarget target) => target switch
        {
            // Desktop GL and DX share ShadowDusk.Compiler (net8.0, native backends); only the
            // PlatformTarget differs — a GL runtime needs GLSL .mgfx, a DX runtime needs DXBC .mgfx.
            ExportTarget.KniDesktopGL      => DesktopShaderInfo("OpenGL"),
            ExportTarget.MonoGameDesktopGL => DesktopShaderInfo("OpenGL"),
            ExportTarget.KniWindowsDX      => DesktopShaderInfo("DirectX"),
            ExportTarget.MonoGameWindowsDX => DesktopShaderInfo("DirectX"),
            // FNA also uses the desktop ShadowDusk.Compiler, but emits legacy D3D9 fx_2_0 .fxb
            // (MojoShader-readable) via PlatformTarget.Fna instead of an .mgfx container — FNA's
            // Effect(gd, bytes) ctor reads that raw .fxb directly (issue #54).
            ExportTarget.FnaDesktop        => DesktopShaderInfo("Fna"),
            // Browser: ShadowDusk.Wasm (net8.0-browser, [JSImport] WASM modules). GL only.
            ExportTarget.KniBlazorGL => new ShaderExportInfo
            {
                Supported = true,
                Package = "ShadowDusk.Wasm",
                CompilerNamespace = "ShadowDusk.Wasm",
                CompilerType = "WasmShaderCompiler",
                PlatformTarget = "OpenGL",
                IsBrowser = true,
            },
            _ => default,   // gated: Android, MonoGame DX12/VK (issue #52)
        };

        static ShaderExportInfo DesktopShaderInfo(string platformTarget) => new ShaderExportInfo
        {
            Supported = true,
            Package = "ShadowDusk.Compiler",
            CompilerNamespace = "ShadowDusk.Compiler",
            CompilerType = "EffectCompiler",
            PlatformTarget = platformTarget,
        };

        /// <summary>
        /// True if the exported project for <paramref name="target"/> can compile shipped <c>.fx</c>
        /// shaders at runtime via ShadowDusk (issue #39). The export dialog uses this to message which
        /// selected platforms are gated. Single source of truth for the supported-target set.
        /// </summary>
        public static bool SupportsRuntimeShaders(ExportTarget target) => GetShaderExportInfo(target).Supported;

        // True when shaders ship AND at least one target compiles them at runtime, so the generated
        // content manager gets the Effect-compiling branch and the common project references ShadowDusk.Core.
        static bool AnyShaderTargetSupported(IReadOnlyList<ExportTarget> targets)
        {
            for (int i = 0; i < targets.Count; i++)
                if (GetShaderExportInfo(targets[i]).Supported)
                    return true;
            return false;
        }

        // Writes each open shader's .fx SOURCE into the export's content folder. The exported project
        // recompiles it at runtime; the bare name (minus .fx) is the Content.Load<Effect> key.
        static void WriteShaderSources(ZipArchive archive, string contentDir, IReadOnlyDictionary<string, string> shaders)
        {
            foreach (var kvp in shaders)
            {
                string fxName = kvp.Key.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) ? kvp.Key : kvp.Key + ".fx";
                AddTextEntry(archive, $"{contentDir}/{fxName}", kvp.Value ?? "");
            }
        }

        /// <summary>
        /// Exports a multi-platform solution when multiple targets are specified.
        /// Delegates to the single-target <see cref="Export(string, ExportTarget, string, IReadOnlyDictionary{string, byte[]})"/>
        /// when only one target is given.
        /// </summary>
        public static byte[] Export(
            string expandedSource,
            IReadOnlyList<ExportTarget> targets,
            string projectName = "MyFiddle",
            IReadOnlyDictionary<string, byte[]> assets = null,
            LibraryRegistry libraryRegistry = null,
            string monoGameVersion = null,
            IReadOnlyDictionary<string, string> shaders = null)
        {
            if (targets == null || targets.Count == 0)
                throw new ArgumentException("At least one export target is required.", nameof(targets));

            if (targets.Count == 1)
                return Export(expandedSource, targets[0], projectName, assets, libraryRegistry, monoGameVersion, shaders);

            // FNA is single-target only — it must never reach the multi-platform common-project
            // path (GenerateCommonCsproj's isMonoGame framework-reference logic assumes KNI/MonoGame).
            if (targets.Contains(ExportTarget.FnaDesktop))
                throw new ArgumentException("FnaDesktop cannot be combined with other export targets.", nameof(targets));

            return ExportMultiPlatform(expandedSource, targets, projectName, assets, libraryRegistry, monoGameVersion, shaders);
        }

        // monoGameVersion, when non-null, overrides the MonoGame framework + content-builder package
        // version (the version selector's preview option). Null preserves the stable default; KNI and
        // FNA targets ignore it entirely.
        public static byte[] Export(
            string expandedSource,
            ExportTarget target,
            string projectName = "MyFiddle",
            IReadOnlyDictionary<string, byte[]> assets = null,
            LibraryRegistry libraryRegistry = null,
            string monoGameVersion = null,
            IReadOnlyDictionary<string, string> shaders = null)
        {
            // hasShaders gates shipping the .fx; includeShaderLoader (target must be supported) gates
            // the runtime-compile wiring (Effect branch, ShadowDusk reference, entry-point injection).
            bool hasShaders = shaders != null && shaders.Count > 0;
            bool includeShaderLoader = hasShaders && GetShaderExportInfo(target).Supported;

            List<NuGetPackage> packages = BuildPackageList(expandedSource, target, libraryRegistry, monoGameVersion);
            string slnx = GenerateSlnx(projectName, target);
            // Shaders are content too: the .fx must be copied to the build output so the runtime
            // compiler can read it, so the content-linking csproj block fires for shaders as well.
            bool hasContent = (assets != null && assets.Count > 0) || hasShaders;
            string csproj = GenerateCsproj(projectName, target, packages, hasContent,
                includeShaders: includeShaderLoader);
            string gameCs = GenerateGameClass(projectName, expandedSource);

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddTextEntry(archive, $"{projectName}.slnx", slnx);
                AddTextEntry(archive, $"{projectName}/{projectName}.csproj", csproj);

                // Emit the dotnet-tools manifest so `dotnet mgcb` resolves at build time
                // (mirrors how BuildPackageList resolves the MonoGame version).
                string mgVersion = monoGameVersion ?? PackageVersions.MonoGameFramework;
                if (NeedsMgcbToolManifest(target))
                    AddTextEntry(archive, $"{projectName}/.config/dotnet-tools.json", GenerateMgcbToolManifest(mgVersion));

                // Each platform has its own entry-point and hosting structure.
                if (target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid)
                {
                    AddTextEntry(archive, $"{projectName}/Activity1.cs", GenerateAndroidActivity(projectName));
                    AddTextEntry(archive, $"{projectName}/AndroidManifest.xml", GenerateAndroidManifest(projectName));
                    AddAndroidResources(archive, $"{projectName}", projectName);
                }
                else if (target == ExportTarget.KniBlazorGL)
                {
                    AddTextEntry(archive, $"{projectName}/Program.cs", GenerateBlazorProgram(projectName));
                    AddTextEntry(archive, $"{projectName}/App.razor", GenerateBlazorAppRazor());
                    AddTextEntry(archive, $"{projectName}/MainLayout.razor", GenerateBlazorMainLayoutRazor());
                    AddTextEntry(archive, $"{projectName}/_Imports.razor", GenerateBlazorImportsRazor(projectName));
                    AddTextEntry(archive, $"{projectName}/Pages/Index.razor", GenerateBlazorIndexRazor(includeShaderLoader));
                    AddTextEntry(archive, $"{projectName}/wwwroot/index.html", GenerateBlazorIndexHtml(projectName));
                }
                else
                {
                    AddTextEntry(archive, $"{projectName}/Program.cs", GenerateProgram(projectName, target, includeShaderLoader));
                }

                AddTextEntry(archive, $"{projectName}/Game1.cs", gameCs);

                // Neither KNI nor MonoGame reliably load raw image files via Content.Load
                // on all platforms (e.g. Android packs assets in the APK). Include a shim
                // ContentManager that handles raw textures via FromStream (and, when shaders ship,
                // compiles .fx to Effect at runtime — issue #39).
                AddTextEntry(archive, $"{projectName}/RawContentManager.cs", GenerateRawContentManager(projectName, UsesAnimationChain(packages), includeShaderLoader));

                // FNA.NET bundles win-x64 and osx natives, but on linux-x64 it ships only
                // libtheorafile — SDL3/FNA3D/FAudio are expected from system packages.
                if (target == ExportTarget.FnaDesktop)
                {
                    AddTextEntry(archive, $"{projectName}/README.txt", GenerateFnaReadme());
                    // Bridges MonoGame/KNI API conveniences that FNA's strict XNA4 surface lacks, so
                    // fiddle code authored against the in-browser KNI runtime compiles on FNA unchanged.
                    AddTextEntry(archive, $"{projectName}/FnaCompat.cs", GenerateFnaCompat());
                }

                // BlazorGL serves content from wwwroot/; all other targets use Content/
                string contentDir = target == ExportTarget.KniBlazorGL
                    ? $"{projectName}/wwwroot/Content"
                    : $"{projectName}/Content";

                if (assets != null)
                {
                    foreach (var kvp in assets)
                    {
                        // Skip keys that are extensionless duplicates (InMemoryContentManager stores both)
                        if (string.IsNullOrEmpty(Path.GetExtension(kvp.Key)))
                            continue;
                        var entry = archive.CreateEntry($"{contentDir}/{kvp.Key}", CompressionLevel.Optimal);
                        using var stream = entry.Open();
                        stream.Write(kvp.Value, 0, kvp.Value.Length);
                    }
                }

                if (hasShaders)
                    WriteShaderSources(archive, contentDir, shaders);
            }

            return memoryStream.ToArray();
        }

        static byte[] ExportMultiPlatform(
            string expandedSource,
            IReadOnlyList<ExportTarget> targets,
            string projectName,
            IReadOnlyDictionary<string, byte[]> assets,
            LibraryRegistry libraryRegistry,
            string monoGameVersion,
            IReadOnlyDictionary<string, string> shaders = null)
        {
            bool hasAssets = assets != null && assets.Count > 0;
            // hasShaders ships the .fx; includeShaderLoader (any target supported) gates the shared
            // Effect-compiling content manager and the common project's ShadowDusk.Core reference.
            bool hasShaders = shaders != null && shaders.Count > 0;
            bool includeShaderLoader = hasShaders && AnyShaderTargetSupported(targets);
            // Shaders ship under Content/ and must be linked/copied to output like raw assets.
            bool hasContent = hasAssets || hasShaders;
            string commonName = $"{projectName}Common";
            string gameCs = GenerateGameClass(projectName, expandedSource);

            // The common project holds Game1.cs, RawContentManager.cs and shared code.
            // Each platform project references the common project and adds its entry point.
            List<NuGetPackage> commonPackages = BuildPackageList(expandedSource, targets[0], libraryRegistry, monoGameVersion);
            string commonCsproj = GenerateCommonCsproj(projectName, commonName, commonPackages, includeShaderLoader);
            string slnx = GenerateSlnx(projectName, commonName, targets);

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddTextEntry(archive, $"{projectName}.slnx", slnx);

                // Common project
                AddTextEntry(archive, $"{commonName}/{commonName}.csproj", commonCsproj);
                AddTextEntry(archive, $"{commonName}/Game1.cs", gameCs);
                AddTextEntry(archive, $"{commonName}/RawContentManager.cs", GenerateRawContentManager(projectName, UsesAnimationChain(commonPackages), includeShaderLoader));

                // Per-platform projects
                foreach (var target in targets)
                {
                    string suffix = GetPlatformSuffix(target);
                    string platformDir = $"{projectName}.{suffix}";
                    // Only platforms whose runtime can compile .fx get the concrete ShadowDusk package
                    // and the entry-point compiler injection; gated platforms (e.g. Android) build but
                    // leave the content manager's compiler unset (issue #39).
                    bool wireShaders = hasShaders && GetShaderExportInfo(target).Supported;
                    List<NuGetPackage> packages = BuildPackageList(expandedSource, target, libraryRegistry, monoGameVersion);
                    string csproj = GenerateCsproj(projectName, target, packages, hasContent, isMultiPlatform: true, commonProjectName: commonName,
                        includeShaders: wireShaders);

                    AddTextEntry(archive, $"{platformDir}/{platformDir}.csproj", csproj);

                    // Emit the dotnet-tools manifest per MonoGame project so `dotnet mgcb`
                    // resolves at build time without affecting sibling KNI projects.
                    string mgVersion = monoGameVersion ?? PackageVersions.MonoGameFramework;
                    if (NeedsMgcbToolManifest(target))
                        AddTextEntry(archive, $"{platformDir}/.config/dotnet-tools.json", GenerateMgcbToolManifest(mgVersion));

                    // Platform-specific entry points
                    if (target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid)
                    {
                        AddTextEntry(archive, $"{platformDir}/Activity1.cs", GenerateAndroidActivity(projectName));
                        AddTextEntry(archive, $"{platformDir}/AndroidManifest.xml", GenerateAndroidManifest(projectName));
                        AddAndroidResources(archive, platformDir, projectName);
                    }
                    else if (target == ExportTarget.KniBlazorGL)
                    {
                        AddTextEntry(archive, $"{platformDir}/Program.cs", GenerateBlazorProgram(projectName));
                        AddTextEntry(archive, $"{platformDir}/App.razor", GenerateBlazorAppRazor());
                        AddTextEntry(archive, $"{platformDir}/MainLayout.razor", GenerateBlazorMainLayoutRazor());
                        AddTextEntry(archive, $"{platformDir}/_Imports.razor", GenerateBlazorImportsRazor(projectName));
                        AddTextEntry(archive, $"{platformDir}/Pages/Index.razor", GenerateBlazorIndexRazor(wireShaders));
                        AddTextEntry(archive, $"{platformDir}/wwwroot/index.html", GenerateBlazorIndexHtml(projectName));
                    }
                    else
                    {
                        AddTextEntry(archive, $"{platformDir}/Program.cs", GenerateProgram(projectName, target, wireShaders));
                    }
                }

                // Shared content at solution root Content/ folder
                if (assets != null)
                {
                    foreach (var kvp in assets)
                    {
                        if (string.IsNullOrEmpty(Path.GetExtension(kvp.Key)))
                            continue;
                        var entry = archive.CreateEntry($"Content/{kvp.Key}", CompressionLevel.Optimal);
                        using var stream = entry.Open();
                        stream.Write(kvp.Value, 0, kvp.Value.Length);
                    }
                }

                if (hasShaders)
                    WriteShaderSources(archive, "Content", shaders);
            }

            return memoryStream.ToArray();
        }

        /// <summary>
        /// Generates a common (shared) .csproj for multi-platform exports.
        /// Contains only platform-agnostic packages; platform-specific packages
        /// (Platform SDKs, content pipeline, Blazor hosting) belong in each
        /// platform project's csproj instead.
        /// </summary>
        static string GenerateCommonCsproj(string projectName, string commonName, List<NuGetPackage> packages, bool includeShaderCore = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<Project Sdk=""Microsoft.NET.Sdk"">");
            sb.AppendLine();
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine("    <OutputType>Library</OutputType>");
            sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
            sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
            sb.AppendLine($"    <AssemblyName>{commonName}</AssemblyName>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");

            // MonoGame bundles the framework into platform-specific packages (e.g.
            // MonoGame.Framework.DesktopGL). The common library still needs a framework
            // reference to compile against, so we pick DesktopGL as the default.
            // Each platform project brings its own correct package at runtime.
            bool isMonoGame = packages.Exists(p => p.Id.StartsWith("MonoGame.Framework."));
            if (isMonoGame)
            {
                var mgPkg = packages.Find(p => p.Id.StartsWith("MonoGame.Framework."));
                sb.AppendLine($@"    <PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""{mgPkg.Version}"" PrivateAssets=""All"" />");
            }

            foreach (var pkg in packages)
            {
                // Skip platform-specific packages — they belong in per-platform projects.
                if (pkg.Id.Contains("Platform"))
                    continue;
                if (pkg.Id.StartsWith("MonoGame.Framework."))
                    continue;
                // DX12's native-binary runtime package is platform-specific — it belongs in the
                // per-platform project, not the shared common library.
                if (pkg.Id.StartsWith("MonoGame.Runtime."))
                    continue;
                if (pkg.Id == "nkast.Xna.Framework.Content.Pipeline.Builder")
                    continue;
                if (pkg.Id == "MonoGame.Content.Builder.Task")
                    continue;
                if (pkg.Id == "Microsoft.AspNetCore.Components.WebAssembly")
                    continue;
                if (pkg.Id == "Microsoft.AspNetCore.Components.WebAssembly.DevServer")
                    continue;

                sb.AppendLine($@"    <PackageReference Include=""{pkg.Id}"" Version=""{pkg.Version}"" />");
            }

            // The shared content manager compiles against ShadowDusk.Core's IShaderCompiler interface
            // (net8.0, no native deps). The concrete compiler is referenced per-platform instead, so
            // the net8.0 common lib never pulls the browser-only ShadowDusk.Wasm package (issue #39).
            if (includeShaderCore)
                sb.AppendLine($@"    <PackageReference Include=""ShadowDusk.Core"" Version=""{PackageVersions.ShadowDusk}"" />");

            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine();
            sb.AppendLine("</Project>");
            return sb.ToString();
        }

        /// <summary>
        /// Generates a .slnx for multi-platform exports that includes
        /// the common project and one project per target platform.
        /// </summary>
        static string GenerateSlnx(string projectName, string commonName, IReadOnlyList<ExportTarget> targets)
        {
            bool needsDeploy = targets.Any(t =>
                t == ExportTarget.KniAndroid || t == ExportTarget.MonoGameAndroid);
            string deployConfig = needsDeploy
                ? @"
  <Configuration Solution=""*|*"" Project=""*|*|Deploy"" />"
                : "";

            var sb = new StringBuilder();
            sb.AppendLine($@"<Solution>{deployConfig}");
            sb.AppendLine($@"  <Project Path=""{commonName}\{commonName}.csproj"" />");
            foreach (var target in targets)
            {
                string suffix = GetPlatformSuffix(target);
                string platformDir = $"{projectName}.{suffix}";
                sb.AppendLine($@"  <Project Path=""{platformDir}\{platformDir}.csproj"" />");
            }
            sb.AppendLine("</Solution>");
            return sb.ToString();
        }

        static List<NuGetPackage> BuildPackageList(string source, ExportTarget target, LibraryRegistry libraryRegistry, string monoGameVersion = null)
        {
            var packages = new List<NuGetPackage>();
            bool isKni = target.IsKni();

            // Base framework packages.
            // FNA is a third runtime category (neither KNI nor MonoGame): a single FNA.NET
            // package supplies the framework and bundles native libs. It has no content
            // pipeline (RawContentManager handles raw assets) and no separate platform package.
            if (target == ExportTarget.FnaDesktop)
            {
                packages.Add(new NuGetPackage { Id = "FNA.NET", Version = PackageVersions.Fna });
            }
            else if (isKni)
            {
                // KNI: 10 individual framework packages + 1 platform package + content pipeline builder
                foreach (string pkg in KniFrameworkPackages)
                    packages.Add(new NuGetPackage { Id = pkg, Version = PackageVersions.KniFramework });

                // Platform-specific package
                (string platformPkg, string platformVer) = target switch
                {
                    ExportTarget.KniDesktopGL => ("nkast.Kni.Platform.SDL2.GL", PackageVersions.KniPlatformDesktopGL),
                    ExportTarget.KniWindowsDX => ("nkast.Kni.Platform.WinForms.DX11", PackageVersions.KniPlatformWindowsDX),
                    ExportTarget.KniAndroid   => ("nkast.Kni.Platform.Android.GL", PackageVersions.KniPlatformAndroid),
                    ExportTarget.KniBlazorGL  => ("nkast.Kni.Platform.Blazor.GL", PackageVersions.KniPlatformBlazorGL),
                    _ => ("nkast.Kni.Platform.SDL2.GL", PackageVersions.KniPlatformDesktopGL),
                };
                packages.Add(new NuGetPackage { Id = platformPkg, Version = platformVer });

                // Content pipeline builder
                packages.Add(new NuGetPackage { Id = "nkast.Xna.Framework.Content.Pipeline.Builder", Version = PackageVersions.KniContentPipeline });

                // BlazorGL requires the Blazor WebAssembly hosting packages
                if (target == ExportTarget.KniBlazorGL)
                {
                    packages.Add(new NuGetPackage { Id = "Microsoft.AspNetCore.Components.WebAssembly", Version = "8.0.17" });
                    packages.Add(new NuGetPackage { Id = "Microsoft.AspNetCore.Components.WebAssembly.DevServer", Version = "8.0.17" });
                }
            }
            else if (target == ExportTarget.MonoGameWindowsDX12 || target == ExportTarget.MonoGameDesktopVK)
            {
                // The MonoGame 3.8.5+ DirectX 12 and Vulkan backends use the unified managed
                // framework (MonoGame.Framework.Native) plus one or more native-binary runtime
                // packages. The shared game code compiles against any MonoGame framework and binds
                // to Native at runtime (both ship an assembly named MonoGame.Framework). There is no
                // MGCB content-builder task here — the legacy MGCB tool has no WindowsDX12/DesktopVK
                // platform; the new backends use a separate content pipeline (out of scope for
                // fiddles, which load no compiled content except a library's own shader). These
                // packages ship only as 3.8.5 previews, so pin to the preview line.
                string previewVersion = monoGameVersion ?? PackageVersions.MonoGameFrameworkPreview;
                packages.Add(new NuGetPackage { Id = "MonoGame.Framework.Native", Version = previewVersion });
                if (target == ExportTarget.MonoGameWindowsDX12)
                {
                    packages.Add(new NuGetPackage { Id = "MonoGame.Runtime.Windows.DX12", Version = previewVersion });
                }
                else
                {
                    // DesktopVK is cross-platform Vulkan — ship the native runtime for all three desktop OSes.
                    packages.Add(new NuGetPackage { Id = "MonoGame.Runtime.Windows.Vulkan", Version = previewVersion });
                    packages.Add(new NuGetPackage { Id = "MonoGame.Runtime.Linux.Vulkan", Version = previewVersion });
                    packages.Add(new NuGetPackage { Id = "MonoGame.Runtime.Mac.Vulkan", Version = previewVersion });
                }
            }
            else
            {
                string monoGamePkg = target switch
                {
                    ExportTarget.MonoGameDesktopGL => "MonoGame.Framework.DesktopGL",
                    ExportTarget.MonoGameWindowsDX => "MonoGame.Framework.WindowsDX",
                    ExportTarget.MonoGameAndroid   => "MonoGame.Framework.Android",
                    _ => "MonoGame.Framework.DesktopGL",
                };
                // The framework and content-builder packages ship in lockstep, so both take the
                // chosen version (the version selector's preview option) or fall back to stable.
                string mgVersion = monoGameVersion ?? PackageVersions.MonoGameFramework;
                packages.Add(new NuGetPackage { Id = monoGamePkg, Version = mgVersion });
                packages.Add(new NuGetPackage { Id = "MonoGame.Content.Builder.Task", Version = mgVersion });
            }

            // Third-party libraries — detected by scanning the source for namespace/type names
            // via registered IExportableLibrary plugins.
            if (libraryRegistry != null)
            {
                IReadOnlyList<ILibraryPlugin> plugins = libraryRegistry.Plugins;
                for (int i = 0; i < plugins.Count; i++)
                {
                    if (plugins[i] is IExportableLibrary exportable && exportable.IsUsedInSource(source))
                    {
                        List<ExportPackage> exportPackages = exportable.GetExportPackages(target, source);
                        for (int j = 0; j < exportPackages.Count; j++)
                            packages.Add(new NuGetPackage { Id = exportPackages[j].Id, Version = exportPackages[j].Version });
                    }
                }
            }

            return packages;
        }

        static string GenerateSlnx(string projectName, ExportTarget target)
        {
            bool needsDeploy = target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid;
            string deployConfig = needsDeploy
                ? @"
  <Configuration Solution=""*|*"" Project=""*|*|Deploy"" />"
                : "";

            return $@"<Solution>{deployConfig}
  <Project Path=""{projectName}\{projectName}.csproj"" />
</Solution>
";
        }

        static string GenerateCsproj(string projectName, ExportTarget target, List<NuGetPackage> packages, bool hasAssets,
            bool isMultiPlatform = false, string commonProjectName = null, bool includeShaders = false)
        {
            var sb = new StringBuilder();

            // In multi-platform mode, filter to platform-specific packages only.
            // Framework packages belong in the common project.
            if (isMultiPlatform)
            {
                packages = packages.FindAll(p =>
                    p.Id.Contains("Platform") ||
                    p.Id.StartsWith("MonoGame.Framework.") ||
                    p.Id.StartsWith("MonoGame.Runtime.") ||
                    p.Id == "nkast.Xna.Framework.Content.Pipeline.Builder" ||
                    p.Id == "MonoGame.Content.Builder.Task" ||
                    p.Id == "Microsoft.AspNetCore.Components.WebAssembly" ||
                    p.Id == "Microsoft.AspNetCore.Components.WebAssembly.DevServer");
            }

            // BlazorGL uses the Blazor SDK; all others use the standard SDK
            string sdk = target == ExportTarget.KniBlazorGL
                ? "Microsoft.NET.Sdk.BlazorWebAssembly"
                : "Microsoft.NET.Sdk";
            sb.AppendLine($@"<Project Sdk=""{sdk}"">");
            sb.AppendLine();
            sb.AppendLine("  <PropertyGroup>");

            // A project must target net8.0-browser (instead of net8.0) when it references a
            // browser-only package — i.e. one doing native [JSImport]/wasm interop, which is
            // NU1201-incompatible with a plain net8.0 reference. Today the only such dependency is the
            // runtime shader compiler (ShadowDusk.Wasm), but this is kept as a general "needs the
            // browser TFM" flag: future browser-native references should OR into it rather than adding
            // another feature check to the per-target TFM logic. It is conditional (not always
            // net8.0-browser) on purpose — net8.0-browser + [JSImport] pulls in the wasm-tools
            // workload, whereas a project without such a dependency builds with just `dotnet restore`
            // on net8.0 (the export contract). Only the Blazor target can honor this; the desktop/
            // Android targets pin their own TFM regardless.
            bool needsBrowserTarget = includeShaders;

            // OutputType and TargetFramework vary by platform
            switch (target)
            {
                case ExportTarget.KniDesktopGL:
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <KniPlatform>DesktopGL</KniPlatform>");
                    break;

                case ExportTarget.KniWindowsDX:
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0-windows</TargetFramework>");
                    sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <KniPlatform>Windows</KniPlatform>");
                    break;

                case ExportTarget.KniAndroid:
                    sb.AppendLine("    <OutputType>Exe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net9.0-android</TargetFramework>");
                    sb.AppendLine("    <SupportedOSPlatformVersion>21</SupportedOSPlatformVersion>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <KniPlatform>Android</KniPlatform>");
                    sb.AppendLine($"    <ApplicationId>com.companyname.{projectName}</ApplicationId>");
                    sb.AppendLine("    <ApplicationVersion>1</ApplicationVersion>");
                    sb.AppendLine("    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>");
                    break;

                case ExportTarget.KniBlazorGL:
                    // net8.0-browser only when a browser-only dependency requires it (see
                    // needsBrowserTarget above); otherwise plain net8.0 keeps the restore-only path.
                    sb.AppendLine(needsBrowserTarget
                        ? "    <TargetFramework>net8.0-browser</TargetFramework>"
                        : "    <TargetFramework>net8.0</TargetFramework>");
                    sb.AppendLine("    <Nullable>disable</Nullable>");
                    sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <KniPlatform>BlazorGL</KniPlatform>");
                    break;

                case ExportTarget.MonoGameDesktopGL:
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <MonoGamePlatform>DesktopGL</MonoGamePlatform>");
                    break;

                case ExportTarget.MonoGameWindowsDX:
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0-windows</TargetFramework>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <MonoGamePlatform>Windows</MonoGamePlatform>");
                    break;

                case ExportTarget.MonoGameAndroid:
                    sb.AppendLine("    <OutputType>Exe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net9.0-android</TargetFramework>");
                    sb.AppendLine("    <SupportedOSPlatformVersion>21</SupportedOSPlatformVersion>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <MonoGamePlatform>Android</MonoGamePlatform>");
                    sb.AppendLine("    <MonoGameSplashScreen>false</MonoGameSplashScreen>");
                    sb.AppendLine($"    <ApplicationId>com.companyname.{projectName}</ApplicationId>");
                    sb.AppendLine("    <ApplicationVersion>1</ApplicationVersion>");
                    sb.AppendLine("    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>");
                    break;

                case ExportTarget.MonoGameWindowsDX12:
                    // net8.0 is intentional and verified: the DX12 native runtime
                    // (MonoGame.Runtime.Windows.DX12) is netstandard2.1 and MonoGame.Framework.Native
                    // ships a net8.0 assembly, so DX12 builds and runs at net8.0 — keeping it uniform
                    // with the other MonoGame desktop targets and able to share the net8.0 common
                    // project unchanged. MonoGame's own templates default to net9.0, but it is not required.
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <MonoGamePlatform>WindowsDX12</MonoGamePlatform>");
                    break;

                case ExportTarget.MonoGameDesktopVK:
                    // net8.0 verified to build and run on the Vulkan backend (see WindowsDX12 note).
                    // DesktopVK is cross-platform (Windows/Linux/macOS); the three Vulkan runtime
                    // packages supply the per-OS native binaries.
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <MonoGamePlatform>DesktopVK</MonoGamePlatform>");
                    break;

                case ExportTarget.FnaDesktop:
                    // FNA is neither KNI nor MonoGame: no KniPlatform/MonoGamePlatform property.
                    // The FNA.NET package brings the framework and bundles native libs for win/macOS.
                    sb.AppendLine("    <OutputType>WinExe</OutputType>");
                    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    break;
            }

            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in packages)
                sb.AppendLine($@"    <PackageReference Include=""{pkg.Id}"" Version=""{pkg.Version}"" />");

            // Concrete ShadowDusk compiler for runtime shader compilation (issue #39): ShadowDusk.Compiler
            // (net8.0) on desktop, ShadowDusk.Wasm (net8.0-browser) for Blazor. Platform-specific, so it
            // belongs in the per-platform project, not the common library. Only emitted for supported targets.
            if (includeShaders)
            {
                ShaderExportInfo shaderInfo = GetShaderExportInfo(target);
                sb.AppendLine($@"    <PackageReference Include=""{shaderInfo.Package}"" Version=""{PackageVersions.ShadowDusk}"" />");
            }

            sb.AppendLine("  </ItemGroup>");

            // In multi-platform mode, reference the common project.
            if (isMultiPlatform && commonProjectName != null)
            {
                sb.AppendLine();
                sb.AppendLine("  <ItemGroup>");
                sb.AppendLine($@"    <ProjectReference Include=""..\{commonProjectName}\{commonProjectName}.csproj"" />");
                sb.AppendLine("  </ItemGroup>");
            }

            if (hasAssets)
            {
                if (isMultiPlatform)
                {
                    // Multi-platform: content lives at solution root, reference with relative paths.
                    sb.AppendLine();
                    sb.AppendLine(@"  <ItemGroup>");
                    if (target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid)
                    {
                        sb.AppendLine(@"    <AndroidAsset Include=""..\Content\**\*"" Link=""Assets\Content\%(RecursiveDir)%(Filename)%(Extension)"" />");
                    }
                    else if (target == ExportTarget.KniBlazorGL)
                    {
                        // Blazor serves static files only from wwwroot/. Handled by
                        // a pre-build target below that copies shared content there.
                    }
                    else
                    {
                        sb.AppendLine(@"    <None Include=""..\Content\**\*"" Link=""Content\%(RecursiveDir)%(Filename)%(Extension)"">");
                        sb.AppendLine(@"      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>");
                        sb.AppendLine(@"    </None>");
                    }
                    sb.AppendLine(@"  </ItemGroup>");
                }
                else if (target != ExportTarget.KniBlazorGL)
                {
                    // Single-platform: BlazorGL content lives in wwwroot/ and is served automatically — no csproj entry needed.
                    sb.AppendLine();
                    sb.AppendLine(@"  <ItemGroup>");
                    if (target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid)
                    {
                        sb.AppendLine(@"    <AndroidAsset Include=""Content\**\*.*"" />");
                    }
                    else
                    {
                        sb.AppendLine(@"    <None Update=""Content\**\*"">");
                        sb.AppendLine(@"      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>");
                        sb.AppendLine(@"    </None>");
                    }
                    sb.AppendLine(@"  </ItemGroup>");
                }
            }

            // BlazorGL multi-platform: add a pre-build target to copy shared content into wwwroot/
            if (isMultiPlatform && target == ExportTarget.KniBlazorGL && hasAssets)
            {
                sb.AppendLine();
                sb.AppendLine(@"  <Target Name=""CopySharedContent"" AfterTargets=""Build"">");
                sb.AppendLine(@"    <ItemGroup>");
                sb.AppendLine(@"      <_SharedContent Include=""..\Content\**\*"" />");
                sb.AppendLine(@"    </ItemGroup>");
                sb.AppendLine(@"    <Copy SourceFiles=""@(_SharedContent)"" DestinationFiles=""@(_SharedContent->'wwwroot\Content\%(RecursiveDir)%(Filename)%(Extension)')"" SkipUnchangedFiles=""true"" />");
                sb.AppendLine(@"  </Target>");
            }

            sb.AppendLine();
            sb.AppendLine("</Project>");
            return sb.ToString();
        }

        static string GenerateProgram(string projectName, ExportTarget target, bool includeShaders)
        {
            ShaderExportInfo info = includeShaders ? GetShaderExportInfo(target) : default;
            // Browser shaders are wired in Index.razor (it must await InitializeAsync), not here.
            bool wireShaders = info.Supported && !info.IsBrowser;

            string shaderUsings = wireShaders
                ? $"using {info.CompilerNamespace};\nusing ShadowDusk.Core;\n"
                : "";

            // Inject the concrete ShadowDusk compiler + backend so Content.Load<Effect> can compile the
            // shipped .fx at runtime. On desktop InitializeAsync is a no-op, so the synchronous Compile
            // inside Content.Load runs directly — no startup await needed (issue #39).
            string contentSetup = wireShaders
                ? $@"        var content = new RawContentManager(game.Services, ""Content"");
        content.ShaderCompiler = new {info.CompilerType}();
        content.ShaderTarget = PlatformTarget.{info.PlatformTarget};
        game.Content = content;"
                : @"        game.Content = new RawContentManager(game.Services, ""Content"");";

            return $@"using System;
{shaderUsings}
namespace {projectName};

public static class Program
{{
    [STAThread]
    static void Main()
    {{
        using var game = new Game1();
{contentSetup}
        game.Run();
    }}
}}
";
        }

        static string GenerateFnaReadme()
        {
            return @"This project targets FNA via the FNA.NET NuGet package — an opinionated
third-party fork of FNA that bundles the required native libraries (SDL3, FNA3D,
FAudio, etc.) and is distributed via NuGet, so it builds and runs as-is via
`dotnet restore` && `dotnet run`.

If you prefer upstream FNA instead, replace the FNA.NET PackageReference in the
.csproj with a project/source reference to FNA.
";
        }

        // FNA-only source-compat shim. XnaFiddle runs fiddles on the in-browser KNI runtime, which —
        // like MonoGame — collapsed SpriteBatch.Begin into a single all-optional-parameter method.
        // FNA keeps XNA4's discrete Begin overloads with no optional/named parameters, so a fiddle
        // calling e.g. Begin(blendState: x, effect: y) fails to compile on FNA (CS1501). This adds the
        // optional-parameter Begin back via an extension method, so the same fiddle code exports to FNA
        // unchanged. Emitted only into FNA exports. Deliberately minimal — grow it only when a real
        // example/common pattern surfaces another gap, not speculatively (issues #48/#54).
        static string GenerateFnaCompat()
        {
            return @"using Microsoft.Xna.Framework;

namespace Microsoft.Xna.Framework.Graphics
{
    // An extension method is only consulted when no instance overload matches, so explicit positional
    // Begin(...) calls keep binding to FNA's own overloads — and the forwarding calls below bind to
    // those instance overloads too, so there is no recursion back into this method.
    internal static class FnaSpriteBatchCompat
    {
        public static void Begin(this SpriteBatch spriteBatch,
            SpriteSortMode sortMode = SpriteSortMode.Deferred,
            BlendState blendState = null,
            SamplerState samplerState = null,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            Effect effect = null,
            Matrix? transformMatrix = null)
        {
            if (transformMatrix.HasValue)
                spriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix.Value);
            else
                spriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect);
        }
    }
}
";
        }

        // Streams the Android template resources (icons, splash PNGs, styles.xml,
        // ic_launcher_background.xml) embedded in this assembly into the exported zip,
        // and emits a project-named strings.xml so @string/app_name resolves.
        static void AddAndroidResources(ZipArchive archive, string projectDir, string projectName)
        {
            // Resource LogicalNames are set explicitly in the csproj to preserve hyphens
            // and folder structure: "AndroidTemplate/Resources/drawable-hdpi/icon.png".
            var assembly = typeof(ProjectExporter).Assembly;
            const string prefix = "AndroidTemplate/";
            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(prefix)) continue;
                string relativePath = resourceName.Substring(prefix.Length);
                string zipPath = $"{projectDir}/{relativePath}";

                using Stream source = assembly.GetManifestResourceStream(resourceName);
                var entry = archive.CreateEntry(zipPath, CompressionLevel.Optimal);
                using Stream dest = entry.Open();
                source.CopyTo(dest);
            }

            AddTextEntry(archive, $"{projectDir}/Resources/Values/strings.xml",
                $@"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
  <string name=""app_name"">{projectName}</string>
</resources>
");
        }

        static string GenerateAndroidManifest(string projectName)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" package=""com.companyname.{projectName}"" android:versionCode=""1"" android:versionName=""1.0"">
	<uses-feature android:glEsVersion=""0x00020000"" android:required=""true"" />
	<uses-sdk android:minSdkVersion=""21"" android:targetSdkVersion=""36"" />
	<uses-permission android:name=""android.permission.ACCESS_NETWORK_STATE"" />
	<uses-permission android:name=""android.permission.INTERNET"" />
	<uses-permission android:name=""android.permission.ACCESS_WIFI_STATE"" />
	<application
		android:hardwareAccelerated=""true""
		android:icon=""@drawable/icon""
		android:isGame=""true""
		android:label=""@string/app_name""
		android:theme=""@style/MainTheme""
		/>
</manifest>
";
        }

        static string GenerateAndroidActivity(string projectName)
        {
            return $@"using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;

namespace {projectName};

[Activity(
    Label = ""{projectName}"",
    MainLauncher = true,
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.FullSensor,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize
)]
public class Activity1 : AndroidGameActivity
{{
    protected override void OnCreate(Bundle bundle)
    {{
        base.OnCreate(bundle);
        var game = new Game1();
        game.Content = new RawContentManager(game.Services, ""Content"");
        SetContentView((View)game.Services.GetService(typeof(View)));
        game.Run();
    }}
}}
";
        }

        static string GenerateGameClass(string projectName, string expandedSource)
        {
            // Find the actual game class name (could be FiddleGame, Game1, MyGame, etc.)
            // and rename it to Game1 so it matches Program.cs / Activity1.cs.
            var classMatch = Regex.Match(expandedSource, @"public\s+class\s+(\w+)\s*:\s*Game\b");
            string source = expandedSource;
            if (classMatch.Success)
            {
                string originalName = classMatch.Groups[1].Value;
                if (originalName != "Game1")
                    source = Regex.Replace(source, @"\b" + Regex.Escape(originalName) + @"\b", "Game1");
            }

            // Collect ALL using lines (they may be interspersed with blank lines in the
            // expanded source) and separate them from the class body.
            var usings = new List<string>();
            var bodyLines = new List<string>();
            var lines = source.Split('\n');

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("using "))
                    usings.Add(line);
                else if (bodyLines.Count > 0 || !string.IsNullOrWhiteSpace(line))
                    bodyLines.Add(line);
            }

            var sb = new StringBuilder();
            foreach (var u in usings)
                sb.AppendLine(u);
            sb.AppendLine();

            // File-scoped namespace
            sb.AppendLine($"namespace {projectName};");
            sb.AppendLine();

            foreach (var line in bodyLines)
                sb.AppendLine(line);

            return sb.ToString();
        }

        static string GenerateBlazorProgram(string projectName)
        {
            return $@"using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace {projectName};

internal class Program
{{
    private static async Task Main(string[] args)
    {{
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>(""#app"");
        builder.Services.AddScoped(sp => new HttpClient()
        {{
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
        }});

        await builder.Build().RunAsync();
    }}
}}
";
        }

        static string GenerateBlazorAppRazor()
        {
            return @"<Router AppAssembly=""@typeof(App).Assembly"">
    <Found Context=""routeData"">
        <RouteView RouteData=""@routeData"" DefaultLayout=""@typeof(MainLayout)"" />
    </Found>
    <NotFound>
        <LayoutView Layout=""@typeof(MainLayout)"">
            <p>Not found</p>
        </LayoutView>
    </NotFound>
</Router>
";
        }

        static string GenerateBlazorMainLayoutRazor()
        {
            return @"@inherits LayoutComponentBase

@Body
";
        }

        static string GenerateBlazorImportsRazor(string projectName)
        {
            return $@"@using System.Net.Http
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using {projectName}
";
        }

        static string GenerateBlazorIndexRazor(bool includeShaders)
        {
            if (!includeShaders)
            {
                return @"@page ""/""
@inject IJSRuntime JsRuntime

<div id=""canvasHolder"" style=""position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: #000;"">
    <canvas id=""theCanvas"" style=""touch-action:none;""></canvas>
</div>

@code {
    Microsoft.Xna.Framework.Game _game;

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            JsRuntime.InvokeAsync<object>(""initRenderJS"", DotNetObjectReference.Create(this));
        }
    }

    [JSInvokable]
    public void TickDotNet()
    {
        if (_game == null)
        {
            _game = new Game1();
            _game.Content = new RawContentManager(_game.Services, ""Content"");
            _game.Run();
        }
        _game.Tick();
    }
}
";
            }

            // Shader-enabled Blazor host (issue #39): the browser ShadowDusk compiler must finish loading
            // its WASM modules before the synchronous Content.Load<Effect> path runs, so await
            // InitializeAsync BEFORE starting the render loop (initRenderJS) — the first Tick, which
            // creates the game and triggers LoadContent, only happens after init completes.
            return @"@page ""/""
@inject IJSRuntime JsRuntime

<div id=""canvasHolder"" style=""position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: #000;"">
    <canvas id=""theCanvas"" style=""touch-action:none;""></canvas>
</div>

@code {
    Microsoft.Xna.Framework.Game _game;
    ShadowDusk.Wasm.WasmShaderCompiler _shaderCompiler;

    protected override async void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            _shaderCompiler = new ShadowDusk.Wasm.WasmShaderCompiler();
            await _shaderCompiler.InitializeAsync();
            await JsRuntime.InvokeAsync<object>(""initRenderJS"", DotNetObjectReference.Create(this));
        }
    }

    [JSInvokable]
    public void TickDotNet()
    {
        if (_game == null)
        {
            _game = new Game1();
            var content = new RawContentManager(_game.Services, ""Content"");
            content.ShaderCompiler = _shaderCompiler;
            content.ShaderTarget = ShadowDusk.Core.PlatformTarget.OpenGL;
            _game.Content = content;
            _game.Run();
        }
        _game.Tick();
    }
}
";
        }

        static string GenerateBlazorIndexHtml(string projectName)
        {
            string jsVer = PackageVersions.KniWasmJs;
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>{projectName}</title>
    <base href=""./"" />
</head>
<body>
    <div id=""app"">Loading...</div>
    <div id=""blazor-error-ui"" style=""display:none; position:fixed; bottom:0; left:0; width:100%; padding:0.6rem 1.25rem; background:lightyellow; z-index:1000;"">
        An unhandled error has occurred.
        <a href="""" class=""reload"">Reload</a>
        <a class=""dismiss"" style=""cursor:pointer; position:absolute; right:0.75rem; top:0.5rem;"">x</a>
    </div>

    <script src=""_framework/blazor.webassembly.js""></script>

    <script src=""_content/nkast.Wasm.JSInterop/js/JSObject.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Dom/js/Window.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Dom/js/Document.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Dom/js/Navigator.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Dom/js/Gamepad.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Dom/js/Media.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.XHR/js/XHR.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Canvas/js/Canvas.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Canvas/js/CanvasGLContext.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.Audio/js/Audio.{jsVer}.js""></script>
    <script src=""_content/nkast.Wasm.XR/js/XR.{jsVer}.js""></script>

    <script>
        function tickJS() {{
            window.theInstance.invokeMethod('TickDotNet');
            window.requestAnimationFrame(tickJS);
        }}

        window.initRenderJS = (instance) => {{
            window.theInstance = instance;
            var canvas = document.getElementById('theCanvas');
            var holder = document.getElementById('canvasHolder');
            canvas.width = holder.clientWidth;
            canvas.height = holder.clientHeight;
            canvas.addEventListener(""contextmenu"", e => e.preventDefault());
            window.requestAnimationFrame(tickJS);
        }};
    </script>
</body>
</html>
";
        }

        // Detects whether an export references a FlatRedBall.AnimationChain package
        // (.KNI or .MonoGame). When it does, the generated RawContentManager gains an
        // AnimationChainList branch; otherwise that branch is omitted so projects that
        // don't reference the package still compile.
        static bool UsesAnimationChain(List<NuGetPackage> packages)
        {
            for (int i = 0; i < packages.Count; i++)
                if (packages[i].Id.StartsWith("FlatRedBall.AnimationChain", StringComparison.Ordinal))
                    return true;
            return false;
        }

        static string GenerateRawContentManager(string projectName, bool includeAnimationChain, bool includeShaderLoader = false)
        {
            // System.Collections.Generic is needed by the .achx branch (HashSet) and the shader
            // branch (the Effect cache); emit it once if either feature is on.
            string genericsUsing = includeAnimationChain || includeShaderLoader
                ? "using System.Collections.Generic;\n"
                : "";

            // The .achx branch references types from FlatRedBall.AnimationChain, which is only
            // referenced when the source uses it, so emit these pieces conditionally.
            string achxUsings = includeAnimationChain
                ? "using FlatRedBall.AnimationChain;\n"
                : "";

            // The shader branch compiles against ShadowDusk.Core's IShaderCompiler interface (issue #39).
            string shaderUsings = includeShaderLoader
                ? "using ShadowDusk.Core;\n"
                : "";

            string achxField = includeAnimationChain
                ? "    AchxLoader _achxLoader;\n"
                : "";

            // The per-platform entry point injects the concrete ShadowDusk compiler + backend; the
            // synchronous Compile inside Load<Effect> turns shipped .fx into an Effect on demand.
            // Compiled effects are cached so repeated loads don't recompile (issue #39).
            string shaderField = includeShaderLoader
                ? @"    public IShaderCompiler ShaderCompiler { get; set; }
    public PlatformTarget ShaderTarget { get; set; }
    readonly Dictionary<string, Effect> _effectCache = new Dictionary<string, Effect>(StringComparer.OrdinalIgnoreCase);
"
                : "";

            string achxLoadBranch = includeAnimationChain
                ? @"        if (typeof(T) == typeof(AnimationChainList))
        {
            string achxName = assetName.EndsWith("".achx"", StringComparison.OrdinalIgnoreCase) ? assetName : assetName + "".achx"";
            string achxPath = Path.Combine(RootDirectory, achxName);
            _achxLoader ??= new AchxLoader(_graphics.GraphicsDevice);

            // AchxLoader asks for the .achx file and each referenced texture by relative path;
            // resolve against RootDirectory and bare filename so all platforms find them.
            Stream OpenStreamOrNull(string path)
            {
                string[] candidates = { path, Path.Combine(RootDirectory, path), Path.Combine(RootDirectory, Path.GetFileName(path)) };
                foreach (string candidate in candidates)
                {
                    byte[] data = TryReadAllBytes(candidate);
                    if (data != null) return new MemoryStream(data, false);
                }
                return null;
            }

            AnimationChainList chainList = _achxLoader.Load(achxPath, OpenStreamOrNull, OpenStreamOrNull);
            SanitizeFrames(chainList);
            return (T)(object)chainList;
        }

"
                : "";

            // Effect branch: read the shipped .fx text and compile it to a runtime Effect via the
            // injected ShadowDusk compiler. A null compiler means this platform is gated (issue #52);
            // throw a clear message instead of a NullReferenceException.
            string shaderLoadBranch = includeShaderLoader
                ? @"        if (typeof(T) == typeof(Effect))
        {
            if (_effectCache.TryGetValue(assetName, out Effect cachedEffect))
                return (T)(object)cachedEffect;

            string fxName = assetName.EndsWith("".fx"", StringComparison.OrdinalIgnoreCase) ? assetName : assetName + "".fx"";
            byte[] fxBytes = TryReadAllBytes(Path.Combine(RootDirectory, fxName));
            if (fxBytes != null)
            {
                if (ShaderCompiler == null)
                    throw new InvalidOperationException(
                        ""Cannot compile shader '"" + fxName + ""': runtime shader compilation is not supported on this platform."");

                string hlsl = System.Text.Encoding.UTF8.GetString(fxBytes);
                var result = ShaderCompiler.Compile(hlsl, new CompilerOptions { Target = ShaderTarget, SourceFileName = fxName });
                if (result.IsFailure)
                    throw new InvalidOperationException(
                        ""Shader compilation failed for '"" + fxName + ""': "" +
                        string.Join("" | "", Array.ConvertAll(result.Error, e => e.FxcFormattedMessage)));

                Effect effect = new Effect(_graphics.GraphicsDevice, result.Value.Data);
                _effectCache[assetName] = effect;
                return (T)(object)effect;
            }
        }

"
                : "";

            string achxMethods = includeAnimationChain
                ? @"
    // .achx frames can contain negative or out-of-bounds pixel coordinates (e.g.
    // LeftCoordinate=-1), which produce Rectangles that raise GL_INVALID_OPERATION
    // (0x0502) on WebGL when submitted to SpriteBatch. Clamp every frame to texture
    // bounds, and premultiply alpha on KNI (AchxLoader's FromStream returns straight alpha).
    void SanitizeFrames(AnimationChainList chainList)
    {
        var premultiplied = new HashSet<Texture2D>();
        foreach (AnimationChain chain in chainList)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                AnimationFrame frame = chain[i];
                if (frame.Texture == null) continue;

                if (NeedsPremultiply && premultiplied.Add(frame.Texture))
                    PremultiplyAlpha(frame.Texture);

                if (!frame.SourceRectangle.HasValue) continue;

                Rectangle r = frame.SourceRectangle.Value;
                int texW = frame.Texture.Width;
                int texH = frame.Texture.Height;

                if (r.Width < 0) { r.X += r.Width; r.Width = -r.Width; }
                if (r.Height < 0) { r.Y += r.Height; r.Height = -r.Height; }
                if (r.X < 0) { r.Width += r.X; r.X = 0; }
                if (r.Y < 0) { r.Height += r.Y; r.Y = 0; }

                if (r.X >= texW || r.Y >= texH || r.Width <= 0 || r.Height <= 0)
                {
                    frame.SourceRectangle = null;
                    continue;
                }

                if (r.Right > texW) r.Width = texW - r.X;
                if (r.Bottom > texH) r.Height = texH - r.Y;

                frame.SourceRectangle = r;
            }
        }
    }

    public override void Unload()
    {
        _achxLoader?.Dispose();
        _achxLoader = null;
        base.Unload();
    }
"
                : "";

            return $@"using System;
using System.IO;
{genericsUsing}{achxUsings}{shaderUsings}using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace {projectName};

/// <summary>
/// A ContentManager that supports loading raw asset files (images and audio)
/// without the content pipeline. MonoGame's built-in ContentManager only
/// supports pipeline-processed .xnb files; this shim bridges the gap so
/// code written for XnaFiddle or KNI works unchanged.
/// Uses TitleContainer.OpenStream for cross-platform compatibility (desktop,
/// Android, and web all resolve content paths differently).
/// </summary>
public class RawContentManager : ContentManager
{{
    static readonly string[] ImageExtensions = {{ "".png"", "".jpg"", "".jpeg"", "".bmp"" }};
    static readonly string[] AudioExtensions = {{ "".wav"" }};
    static readonly bool IsDesktop = !OperatingSystem.IsAndroid() && !OperatingSystem.IsBrowser();
    // KNI's and FNA's Texture2D.FromStream return straight (non-premultiplied) alpha, but
    // XNA-style code (SpriteBatch + BlendState.AlphaBlend) expects premultiplied. MonoGame's
    // FromStream already premultiplies, so we only do it on KNI and FNA. Detected at runtime
    // by assembly name (KNI is Xna.Framework.*, FNA is FNA.NET) to avoid pushing a #if /
    // csproj DefineConstants flag down to every exported project. MonoGame is left false.
    // Fragile: fails open (skips premultiply) if either framework ever renames its graphics
    // assembly away from these names.
    static readonly bool NeedsPremultiply =
        typeof(Texture2D).Assembly.GetName().Name is string asmName &&
        (asmName.StartsWith(""Xna.Framework"") || asmName == ""FNA.NET"");

    readonly IGraphicsDeviceService _graphics;
{achxField}{shaderField}
    static void PremultiplyAlpha(Texture2D texture)
    {{
        Color[] pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        for (int i = 0; i < pixels.Length; i++)
        {{
            byte a = pixels[i].A;
            pixels[i] = new Color((byte)(pixels[i].R * a / 255), (byte)(pixels[i].G * a / 255), (byte)(pixels[i].B * a / 255), a);
        }}
        texture.SetData(pixels);
    }}

    public RawContentManager(IServiceProvider services, string rootDirectory)
        : base(services, rootDirectory)
    {{
        _graphics = (IGraphicsDeviceService)services.GetService(typeof(IGraphicsDeviceService));
    }}

    public override T Load<T>(string assetName)
    {{
        if (typeof(T) == typeof(Texture2D))
        {{
            foreach (var ext in ImageExtensions)
            {{
                string path = Path.Combine(RootDirectory, assetName + ext);
                byte[] bytes = TryReadAllBytes(path);
                if (bytes == null) continue;
                // If the user dropped an .xnb in disguise (or any XNB-headered blob), skip
                // raw decode and fall through to the pipeline-based base.Load<T>.
                if (IsXnb(bytes)) break;
                using var stream = new MemoryStream(bytes);
                var tex = Texture2D.FromStream(_graphics.GraphicsDevice, stream);
                if (NeedsPremultiply) PremultiplyAlpha(tex);
                return (T)(object)tex;
            }}
        }}

        if (typeof(T) == typeof(SoundEffect))
        {{
            foreach (var ext in AudioExtensions)
            {{
                string path = Path.Combine(RootDirectory, assetName + ext);
                byte[] bytes = TryReadAllBytes(path);
                if (bytes == null) continue;
                if (IsXnb(bytes)) break;
                using var stream = new MemoryStream(bytes);
                return (T)(object)SoundEffect.FromStream(stream);
            }}
        }}

{shaderLoadBranch}{achxLoadBranch}        return base.Load<T>(assetName);
    }}

    static bool IsXnb(byte[] bytes) =>
        bytes != null && bytes.Length >= 3 && bytes[0] == (byte)'X' && bytes[1] == (byte)'N' && bytes[2] == (byte)'B';

    static byte[] TryReadAllBytes(string path)
    {{
        if (IsDesktop)
        {{
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }}
        try
        {{
            using var stream = TitleContainer.OpenStream(path);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }}
        catch (FileNotFoundException) {{ return null; }}
    }}
{achxMethods}}}
";
        }

        // The MonoGame content pipeline compiles content (e.g. Apos.Shapes' shader, injected via its
        // buildTransitive MonoGameContentReference) by shelling out to the `dotnet mgcb` local tool.
        // That tool only resolves when the project ships a .config/dotnet-tools.json manifest; without
        // it the build fails with MSB3073. The legacy content-builder task (MonoGame.Content.Builder.Task
        // + dotnet-mgcb + MonoGameContentReference) is the path the export uses on both 3.8.4 stable and
        // the 3.8.5 preview — it's mechanically identical and dotnet-mgcb ships at both versions — so the
        // manifest is emitted for all MonoGame targets. The caller pins dotnet-mgcb to mgVersion so the
        // restored tool stays in lockstep with the framework/builder packages. KNI/FNA use different
        // tooling and are out of scope.
        static bool NeedsMgcbToolManifest(ExportTarget target) =>
            target == ExportTarget.MonoGameDesktopGL
            || target == ExportTarget.MonoGameWindowsDX
            || target == ExportTarget.MonoGameAndroid;

        // dotnet-mgcb is pinned to the same version as the framework/content-builder packages so the
        // restored local tool stays in lockstep with MonoGame.Content.Builder.Task.
        static string GenerateMgcbToolManifest(string mgVersion)
        {
            return $@"{{
  ""version"": 1,
  ""isRoot"": true,
  ""tools"": {{
    ""dotnet-mgcb"": {{
      ""version"": ""{mgVersion}"",
      ""commands"": [ ""mgcb"" ]
    }}
  }}
}}
";
        }

        static void AddTextEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
