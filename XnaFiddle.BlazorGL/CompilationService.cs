using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MetadataReferenceService.Abstractions.Types;
using MetadataReferenceService.BlazorWasm;

namespace XnaFiddle
{
    public class CompilationService
    {
        private readonly NavigationManager _navigationManager;
        private BlazorWasmMetadataReferenceService _referenceService;

        public CompilationService(NavigationManager navigationManager)
        {
            _navigationManager = navigationManager;
        }

        // BCL assemblies that users may need but aren't loaded by XnaFiddle's own code.
        // Without these, Roslyn can't resolve types forwarded to these assemblies.
        private static readonly string[] BclAssemblyNames =
        [
            "System.ObjectModel",          // ObservableCollection<T>
            "System.Linq.Expressions",     // Expression trees (used by some binding APIs)
        ];

        // KNI assemblies that exist in _framework/ but may not be loaded into the AppDomain
        // yet due to lazy loading (no game is running when the user first compiles).
        private static readonly string[] KniAssemblyNames =
        [
            "Xna.Framework",
            "Xna.Framework.Graphics",
            "Xna.Framework.Content",
            "Xna.Framework.Audio",
            "Xna.Framework.Media",
            "Xna.Framework.Input",
            "Xna.Framework.Game",
            "Xna.Framework.Devices",
            "Xna.Framework.Storage",
            "Xna.Framework.XR",
            "Kni.Platform",
            "nkast.Wasm.JSInterop",
            "nkast.Wasm.Dom",
            "nkast.Wasm.Canvas",
            "nkast.Wasm.Audio",
            "nkast.Wasm.XHR",
            "nkast.Wasm.XR",
            "nkast.Wasm.Clipboard",
            "KniGum",
            "GumCommon",
            "FlatRedBall.InterpolationCore",
            "TextCopy",
            "Apos.Shapes.KNI",
            "FontStashSharp.Kni",
            "FontStashSharp.Base",
            "FontStashSharp.Rasterizers.StbTrueTypeSharp",
            "KNI.Extended",
            "Aether.Physics2D",
            "KernSmith",
            "KernSmith.GumCommon",
            "KernSmith.KniGum",
            "MLEM.KNI",
            "MLEM.Ui.KNI",
            "MLEM.Extended.KNI"
        ];

        public class DiagnosticInfo
        {
            public int StartLine { get; set; }
            public int StartCol { get; set; }
            public int EndLine { get; set; }
            public int EndCol { get; set; }
            public string Message { get; set; }
            public string Severity { get; set; } // "error", "warning", "info"
        }

        public class CompilationResult
        {
            public byte[] ILBytes { get; set; }
            public string Log { get; set; }
            public bool Success { get; set; }
            public List<DiagnosticInfo> Diagnostics { get; set; } = [];
            public List<string> FailedAssemblies { get; set; } = [];
            public string VersionInfo { get; set; }
        }

        public async Task<CompilationResult> CompileAsync(string sourceCode, Action<int, int> onProgress = null, CancellationToken cancellationToken = default)
        {
            // Always create a fresh service so cached failure results from a previous
            // compile don't permanently hide assemblies that are now loaded.
            _referenceService = new(_navigationManager);
            string log = "";

            // Parse
            string[] preprocessorSymbols = ["BLAZORGL"];
            CSharpParseOptions parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.LatestMajor)
                .WithPreprocessorSymbols(preprocessorSymbols);

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);

            // Force-load optional assemblies into the AppDomain.
            // Blazor WASM lazy-loads assemblies; without this they may not be present when
            // a #code= link is opened on a fresh page, causing silent metadata-fetch failures.
            for (int i = 0; i < KniAssemblyNames.Length; i++)
            {
                try { Assembly.Load(KniAssemblyNames[i]); }
                catch { /* already loaded, or genuinely absent — handled below */ }
            }
            for (int i = 0; i < BclAssemblyNames.Length; i++)
            {
                try { Assembly.Load(BclAssemblyNames[i]); }
                catch { /* already loaded, or genuinely absent — handled below */ }
            }

            // Collect library version info for display in the diagnostics panel
            (string Label, string[] AsmNames)[] versionTargets =
            [
                ("KNI",              ["Kni.Platform"]),
                ("Gum.KNI",          ["GumCommon", "KniGum"]),
                ("KNI.Extended",     ["KNI.Extended"]),
                ("Apos.Shapes.KNI",  ["Apos.Shapes.KNI"]),
                ("FontStashSharp.Kni", ["FontStashSharp.Kni", "FontStashSharp.Base"]),
                ("Aether.Physics2D",  ["Aether.Physics2D"]),
                ("KernSmith.KniGum",  ["KernSmith.KniGum", "KernSmith.GumCommon", "KernSmith"]),
                ("MLEM",              ["MLEM.KNI", "MLEM.Ui.KNI", "MLEM.Extended.KNI"])
            ];
            string versionInfo = string.Join("  ·  ",
                versionTargets.Select(t => $"{t.Label} {t.AsmNames.Select(GetAssemblyVersion).FirstOrDefault(v => v != "?" && v != "0.0.0.0" && v != "0.0.0") ?? GetAssemblyVersion(t.AsmNames[0])}"));

            // Collect assembly names from loaded assemblies + known KNI assemblies
            HashSet<string> assembliesRequired = [];
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                // Skip dynamic assemblies — they exist only in memory (e.g. "Anonymously Hosted
                // DynamicMethods Assembly") and have no .dll file to fetch metadata from.
                if (loadedAssemblies[i].IsDynamic) continue;
                string assemblyName = loadedAssemblies[i].GetName().Name;
                // Skip our own previously compiled assembly — it's in-memory and has no .dll to fetch
                if (!string.IsNullOrEmpty(assemblyName) && assemblyName != "UserAssembly")
                    assembliesRequired.Add(assemblyName);
            }

            // Add KNI assemblies that may not be loaded yet due to lazy loading
            for (int i = 0; i < KniAssemblyNames.Length; i++)
                assembliesRequired.Add(KniAssemblyNames[i]);

            // Add BCL assemblies needed for type-forwarding resolution
            for (int i = 0; i < BclAssemblyNames.Length; i++)
                assembliesRequired.Add(BclAssemblyNames[i]);

            // Fetch metadata references
            List<MetadataReference> metadataReferences = [];
            List<string> failedAssemblies = [];
            int resolved = 0;
            int total = assembliesRequired.Count;

            foreach (string assemblyName in assembliesRequired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssemblyDetails assemblyDetails = new() { Name = assemblyName };
                try
                {
                    MetadataReference metadataReference = await _referenceService.CreateAsync(assemblyDetails);
                    if (metadataReference != null)
                        metadataReferences.Add(metadataReference);
                    else
                        failedAssemblies.Add(assemblyName);
                }
                catch
                {
                    failedAssemblies.Add(assemblyName);
                }
                onProgress?.Invoke(++resolved, total);
            }

            if (failedAssemblies.Count > 0)
                Console.WriteLine($"[XnaFiddle] {failedAssemblies.Count} assembl{(failedAssemblies.Count == 1 ? "y" : "ies")} failed to resolve: {string.Join(", ", failedAssemblies)}");

            // Compile
            CSharpCompilationOptions compilationOptions = new(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                reportSuppressedDiagnostics: true,
                metadataImportOptions: MetadataImportOptions.Public,
                allowUnsafe: false,
                optimizationLevel: OptimizationLevel.Release,
                concurrentBuild: false  // WASM is single-threaded; parallel workers deadlock on Monitor.Wait
            );

            CSharpCompilation compilation = CSharpCompilation.Create(
                "UserAssembly", [syntaxTree], metadataReferences.ToArray(), compilationOptions);

            // Security check: reject forbidden APIs before emitting.
            List<DiagnosticInfo> securityErrors = SecurityChecker.Check(compilation, syntaxTree);
            if (securityErrors.Count > 0)
            {
                return new CompilationResult
                {
                    ILBytes = null,
                    Log = string.Join("\n", securityErrors.Select(e => e.Message)),
                    Success = false,
                    Diagnostics = securityErrors,
                    FailedAssemblies = failedAssemblies,
                    VersionInfo = versionInfo
                };
            }

            using MemoryStream ILMemoryStream = new();
            EmitResult emitResult = compilation.Emit(ILMemoryStream);

            List<DiagnosticInfo> diagnosticInfos = [];
            for (int i = 0; i < emitResult.Diagnostics.Length; i++)
            {
                Diagnostic diagnostic = emitResult.Diagnostics[i];
                if (diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                        log += diagnostic.ToString() + "\n";

                    var lineSpan = diagnostic.Location.GetMappedLineSpan();
                    diagnosticInfos.Add(new DiagnosticInfo
                    {
                        StartLine = lineSpan.StartLinePosition.Line + 1,
                        StartCol = lineSpan.StartLinePosition.Character + 1,
                        EndLine = lineSpan.EndLinePosition.Line + 1,
                        EndCol = lineSpan.EndLinePosition.Character + 1,
                        Message = diagnostic.GetMessage(),
                        Severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning"
                    });
                }
            }

            byte[] ilBytes = null;
            if (emitResult.Success)
                ilBytes = ILMemoryStream.ToArray();

            return new CompilationResult
            {
                ILBytes = ilBytes,
                Log = log,
                Success = emitResult.Success,
                Diagnostics = diagnosticInfos,
                FailedAssemblies = failedAssemblies,
                VersionInfo = versionInfo
            };
        }

        private static string GetAssemblyVersion(string assemblyName)
        {
            Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == assemblyName);
            if (asm == null) return "?";

            string infoVersion = asm
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
            {
                // Strip build metadata suffix (+commithash) common in NuGet packages
                int plus = infoVersion.IndexOf('+');
                return plus >= 0 ? infoVersion[..plus] : infoVersion;
            }

            return asm.GetName().Version?.ToString() ?? "?";
        }

    }
}
