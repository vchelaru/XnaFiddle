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
        MonoGameAndroid
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

        static bool IsKni(ExportTarget target) => target switch
        {
            ExportTarget.KniDesktopGL => true,
            ExportTarget.KniWindowsDX => true,
            ExportTarget.KniAndroid => true,
            ExportTarget.KniBlazorGL => true,
            _ => false,
        };


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
            ExportTarget.MonoGameDesktopGL => "DesktopGL",
            ExportTarget.MonoGameWindowsDX => "WindowsDX",
            ExportTarget.MonoGameAndroid   => "Android",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        /// <summary>
        /// Exports a multi-platform solution when multiple targets are specified.
        /// Delegates to the single-target <see cref="Export(string, ExportTarget, string, IReadOnlyDictionary{string, byte[]})"/>
        /// when only one target is given.
        /// </summary>
        public static byte[] Export(
            string expandedSource,
            IReadOnlyList<ExportTarget> targets,
            string projectName = "MyFiddle",
            IReadOnlyDictionary<string, byte[]> assets = null)
        {
            if (targets == null || targets.Count == 0)
                throw new ArgumentException("At least one export target is required.", nameof(targets));

            if (targets.Count == 1)
                return Export(expandedSource, targets[0], projectName, assets);

            return ExportMultiPlatform(expandedSource, targets, projectName, assets);
        }

        public static byte[] Export(
            string expandedSource,
            ExportTarget target,
            string projectName = "MyFiddle",
            IReadOnlyDictionary<string, byte[]> assets = null)
        {
            List<NuGetPackage> packages = BuildPackageList(expandedSource, target);
            string slnx = GenerateSlnx(projectName, target);
            string csproj = GenerateCsproj(projectName, target, packages, assets != null && assets.Count > 0);
            string gameCs = GenerateGameClass(projectName, expandedSource);

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddTextEntry(archive, $"{projectName}.slnx", slnx);
                AddTextEntry(archive, $"{projectName}/{projectName}.csproj", csproj);

                // Each platform has its own entry-point and hosting structure.
                if (target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid)
                {
                    AddTextEntry(archive, $"{projectName}/Activity1.cs", GenerateAndroidActivity(projectName));
                }
                else if (target == ExportTarget.KniBlazorGL)
                {
                    AddTextEntry(archive, $"{projectName}/Program.cs", GenerateBlazorProgram(projectName));
                    AddTextEntry(archive, $"{projectName}/App.razor", GenerateBlazorAppRazor());
                    AddTextEntry(archive, $"{projectName}/MainLayout.razor", GenerateBlazorMainLayoutRazor());
                    AddTextEntry(archive, $"{projectName}/_Imports.razor", GenerateBlazorImportsRazor(projectName));
                    AddTextEntry(archive, $"{projectName}/Pages/Index.razor", GenerateBlazorIndexRazor());
                    AddTextEntry(archive, $"{projectName}/wwwroot/index.html", GenerateBlazorIndexHtml(projectName));
                }
                else
                {
                    AddTextEntry(archive, $"{projectName}/Program.cs", GenerateProgram(projectName));
                }

                AddTextEntry(archive, $"{projectName}/Game1.cs", gameCs);

                // Neither KNI nor MonoGame reliably load raw image files via Content.Load
                // on all platforms (e.g. Android packs assets in the APK). Include a shim
                // ContentManager that handles raw textures via FromStream.
                AddTextEntry(archive, $"{projectName}/RawContentManager.cs", GenerateRawContentManager(projectName));

                if (assets != null)
                {
                    // BlazorGL serves content from wwwroot/; all other targets use Content/
                    string contentDir = target == ExportTarget.KniBlazorGL
                        ? $"{projectName}/wwwroot/Content"
                        : $"{projectName}/Content";

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
            }

            return memoryStream.ToArray();
        }

        static byte[] ExportMultiPlatform(
            string expandedSource,
            IReadOnlyList<ExportTarget> targets,
            string projectName,
            IReadOnlyDictionary<string, byte[]> assets)
        {
            bool hasAssets = assets != null && assets.Count > 0;
            string commonName = $"{projectName}Common";
            string gameCs = GenerateGameClass(projectName, expandedSource);

            // The common project holds Game1.cs, RawContentManager.cs and shared code.
            // Each platform project references the common project and adds its entry point.
            List<NuGetPackage> commonPackages = BuildPackageList(expandedSource, targets[0]);
            string commonCsproj = GenerateCommonCsproj(projectName, commonName, commonPackages);
            string slnx = GenerateSlnx(projectName, commonName, targets);

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddTextEntry(archive, $"{projectName}.slnx", slnx);

                // Common project
                AddTextEntry(archive, $"{commonName}/{commonName}.csproj", commonCsproj);
                AddTextEntry(archive, $"{commonName}/Game1.cs", gameCs);
                AddTextEntry(archive, $"{commonName}/RawContentManager.cs", GenerateRawContentManager(projectName));

                // Per-platform projects
                foreach (var target in targets)
                {
                    string suffix = GetPlatformSuffix(target);
                    string platformDir = $"{projectName}.{suffix}";
                    List<NuGetPackage> packages = BuildPackageList(expandedSource, target);
                    string csproj = GenerateCsproj(projectName, target, packages, hasAssets, isMultiPlatform: true, commonProjectName: commonName);

                    AddTextEntry(archive, $"{platformDir}/{platformDir}.csproj", csproj);

                    // Platform-specific entry points
                    if (target == ExportTarget.KniAndroid || target == ExportTarget.MonoGameAndroid)
                    {
                        AddTextEntry(archive, $"{platformDir}/Activity1.cs", GenerateAndroidActivity(projectName));
                    }
                    else if (target == ExportTarget.KniBlazorGL)
                    {
                        AddTextEntry(archive, $"{platformDir}/Program.cs", GenerateBlazorProgram(projectName));
                        AddTextEntry(archive, $"{platformDir}/App.razor", GenerateBlazorAppRazor());
                        AddTextEntry(archive, $"{platformDir}/MainLayout.razor", GenerateBlazorMainLayoutRazor());
                        AddTextEntry(archive, $"{platformDir}/_Imports.razor", GenerateBlazorImportsRazor(projectName));
                        AddTextEntry(archive, $"{platformDir}/Pages/Index.razor", GenerateBlazorIndexRazor());
                        AddTextEntry(archive, $"{platformDir}/wwwroot/index.html", GenerateBlazorIndexHtml(projectName));
                    }
                    else
                    {
                        AddTextEntry(archive, $"{platformDir}/Program.cs", GenerateProgram(projectName));
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
            }

            return memoryStream.ToArray();
        }

        /// <summary>
        /// Generates a common (shared) .csproj for multi-platform exports.
        /// Contains only platform-agnostic packages; platform-specific packages
        /// (Platform SDKs, content pipeline, Blazor hosting) belong in each
        /// platform project's csproj instead.
        /// </summary>
        static string GenerateCommonCsproj(string projectName, string commonName, List<NuGetPackage> packages)
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

        static List<NuGetPackage> BuildPackageList(string source, ExportTarget target)
        {
            var packages = new List<NuGetPackage>();
            bool isKni = IsKni(target);

            // Base framework packages
            if (isKni)
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
            else
            {
                string monoGamePkg = target switch
                {
                    ExportTarget.MonoGameDesktopGL => "MonoGame.Framework.DesktopGL",
                    ExportTarget.MonoGameWindowsDX => "MonoGame.Framework.WindowsDX",
                    ExportTarget.MonoGameAndroid   => "MonoGame.Framework.Android",
                    _ => "MonoGame.Framework.DesktopGL",
                };
                packages.Add(new NuGetPackage { Id = monoGamePkg, Version = PackageVersions.MonoGameFramework });
                packages.Add(new NuGetPackage { Id = "MonoGame.Content.Builder.Task", Version = PackageVersions.MonoGameFramework });
            }

            // Third-party libraries — detected by scanning the source for namespace/type names.
            // No "using" prefix required; works with fully qualified references too.

            if (source.Contains("MonoGameGum") || source.Contains("Gum."))
            {
                packages.Add(new NuGetPackage
                {
                    Id = isKni ? "Gum.KNI" : "Gum.MonoGame",
                    Version = PackageVersions.Gum
                });
            }

            if (source.Contains("Apos.Shapes"))
            {
                packages.Add(new NuGetPackage
                {
                    Id = isKni ? "Apos.Shapes.KNI" : "Apos.Shapes",
                    Version = PackageVersions.AposShapes
                });
            }

            if (source.Contains("MonoGame.Extended"))
            {
                packages.Add(new NuGetPackage
                {
                    Id = isKni ? "KNI.Extended" : "MonoGame.Extended",
                    Version = PackageVersions.KniExtended
                });
            }

            if (source.Contains("FontStashSharp"))
            {
                packages.Add(new NuGetPackage
                {
                    Id = isKni ? "FontStashSharp.Kni" : "FontStashSharp.MonoGame",
                    Version = PackageVersions.FontStashSharp
                });
            }

            // Aether.Physics2D — using-directive scan (nkast = KNI fork, tainicom = original)
            if (source.Contains("Aether.Physics2D"))
            {
                packages.Add(new NuGetPackage
                {
                    Id = isKni ? "Aether.Physics2D.KNI" : "Aether.Physics2D",
                    Version = PackageVersions.AetherPhysics
                });
            }

            if (source.Contains("KernSmith"))
            {
                packages.Add(new NuGetPackage { Id = "KernSmith", Version = PackageVersions.KernSmith });
                packages.Add(new NuGetPackage { Id = isKni ? "KernSmith.KniGum" : "KernSmith.MonoGameGum", Version = PackageVersions.KernSmith });
                packages.Add(new NuGetPackage { Id = "KernSmith.Rasterizers.StbTrueType", Version = PackageVersions.KernSmith });
            }

            if (source.Contains("MLEM"))
            {
                // we always add MLEM base in case only that one is used
                packages.Add(new NuGetPackage {Id = isKni ? "MLEM.KNI" : "MLEM", Version = PackageVersions.Mlem});

                if (source.Contains("MLEM.Ui"))
                {
                    packages.Add(new NuGetPackage {Id = isKni ? "MLEM.Ui.KNI" : "MLEM.Ui", Version = PackageVersions.Mlem});
                }
                if (source.Contains("MLEM.Extended"))
                {
                    packages.Add(new NuGetPackage {Id = isKni ? "MLEM.Extended.KNI" : "MLEM.Extended", Version = PackageVersions.Mlem});
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
            bool isMultiPlatform = false, string commonProjectName = null)
        {
            var sb = new StringBuilder();

            // In multi-platform mode, filter to platform-specific packages only.
            // Framework packages belong in the common project.
            if (isMultiPlatform)
            {
                packages = packages.FindAll(p =>
                    p.Id.Contains("Platform") ||
                    p.Id.StartsWith("MonoGame.Framework.") ||
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
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <KniPlatform>Android</KniPlatform>");
                    sb.AppendLine($"    <ApplicationId>com.myfiddle.{projectName.ToLowerInvariant()}</ApplicationId>");
                    sb.AppendLine("    <ApplicationVersion>1</ApplicationVersion>");
                    sb.AppendLine("    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>");
                    break;

                case ExportTarget.KniBlazorGL:
                    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
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
                    sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
                    sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
                    sb.AppendLine("    <MonoGamePlatform>Android</MonoGamePlatform>");
                    sb.AppendLine($"    <ApplicationId>com.myfiddle.{projectName.ToLowerInvariant()}</ApplicationId>");
                    sb.AppendLine("    <ApplicationVersion>1</ApplicationVersion>");
                    sb.AppendLine("    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>");
                    break;
            }

            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in packages)
                sb.AppendLine($@"    <PackageReference Include=""{pkg.Id}"" Version=""{pkg.Version}"" />");
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

        static string GenerateProgram(string projectName)
        {
            return $@"using System;

namespace {projectName};

public static class Program
{{
    [STAThread]
    static void Main()
    {{
        using var game = new Game1();
        game.Content = new RawContentManager(game.Services, ""Content"");
        game.Run();
    }}
}}
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

        static string GenerateBlazorIndexRazor()
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

        static string GenerateRawContentManager(string projectName)
        {
            return $@"using System;
using System.IO;
using Microsoft.Xna.Framework;
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

    readonly IGraphicsDeviceService _graphics;

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
                if (IsDesktop)
                {{
                    if (!File.Exists(path))
                        continue;
                    using var stream = File.OpenRead(path);
                    return (T)(object)Texture2D.FromStream(_graphics.GraphicsDevice, stream);
                }}
                else
                {{
                    try
                    {{
                        using var stream = TitleContainer.OpenStream(path);
                        return (T)(object)Texture2D.FromStream(_graphics.GraphicsDevice, stream);
                    }}
                    catch (FileNotFoundException) {{ }}
                }}
            }}
        }}

        if (typeof(T) == typeof(SoundEffect))
        {{
            foreach (var ext in AudioExtensions)
            {{
                string path = Path.Combine(RootDirectory, assetName + ext);
                if (IsDesktop)
                {{
                    if (!File.Exists(path))
                        continue;
                    using var stream = File.OpenRead(path);
                    return (T)(object)SoundEffect.FromStream(stream);
                }}
                else
                {{
                    try
                    {{
                        using var stream = TitleContainer.OpenStream(path);
                        return (T)(object)SoundEffect.FromStream(stream);
                    }}
                    catch (FileNotFoundException) {{ }}
                }}
            }}
        }}

        return base.Load<T>(assetName);
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
