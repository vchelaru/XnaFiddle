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
        private readonly LibraryRegistry _libraryRegistry;
        private BlazorWasmMetadataReferenceService _referenceService;

        // Persistent success-only cache of resolved metadata references, keyed by assembly
        // name. Parsing each assembly's PE metadata is the dominant cost of a recompile in
        // single-threaded WASM, so once an assembly resolves we keep its MetadataReference
        // for all subsequent compiles. Only successes are cached; failures are left out so
        // they are retried (an assembly absent on one compile may be loaded by the next).
        private readonly Dictionary<string, MetadataReference> _referenceCache = [];

        public CompilationService(NavigationManager navigationManager, LibraryRegistry libraryRegistry)
        {
            _navigationManager = navigationManager;
            _libraryRegistry = libraryRegistry;
        }

        // BCL assemblies that users may need but aren't loaded by XnaFiddle's own code.
        // Without these, Roslyn can't resolve types forwarded to these assemblies.
        private static readonly string[] BclAssemblyNames =
        [
            "System.ObjectModel",          // ObservableCollection<T>
            "System.Linq.Expressions",     // Expression trees (used by some binding APIs)
        ];

        // Core KNI platform assemblies that exist in _framework/ but may not be loaded
        // into the AppDomain yet due to lazy loading. Library-specific assemblies are
        // contributed by ILibraryPlugin.RequiredAssemblies via the LibraryRegistry.
        private static readonly string[] KniCoreAssemblyNames =
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
            "TextCopy",
        ];

        public class CompilationResult
        {
            public byte[] ILBytes { get; set; }
            public string Log { get; set; }
            public bool Success { get; set; }
            public List<DiagnosticInfo> Diagnostics { get; set; } = [];
            public List<string> FailedAssemblies { get; set; } = [];
            public string VersionInfo { get; set; }
        }

        /// <summary>
        /// Builds and returns the full metadata reference list for the current user
        /// environment, resolving via BlazorWasmMetadataReferenceService. Used by
        /// CompileAsync and shared with IntellisenseService so the completion workspace
        /// sees the same BCL/KNI/plugin surface the compile will see.
        /// </summary>
        public async Task<(List<MetadataReference> References, List<string> FailedAssemblies, string VersionInfo)>
            GetMetadataReferencesAsync(Action<int, int> onProgress = null, CancellationToken cancellationToken = default)
        {
            ForceLoadAssemblies();

            IReadOnlyList<ILibraryPlugin> plugins = _libraryRegistry.Plugins;

            // Collect library version info for display in the diagnostics panel.
            var versionParts = new List<string>();
            versionParts.Add($"KNI {GetAssemblyVersion("Kni.Platform")}");
            for (int i = 0; i < plugins.Count; i++)
            {
                var info = plugins[i].VersionInfo;
                if (info.Label.Length == 0) continue;
                string version = info.AssemblyNames.Select(GetAssemblyVersion)
                    .FirstOrDefault(v => v != "?" && v != "0.0.0.0" && v != "0.0.0")
                    ?? GetAssemblyVersion(info.AssemblyNames[0]);
                versionParts.Add($"{info.Label} {version}");
            }
            string versionInfo = string.Join("  ·  ", versionParts);

            HashSet<string> assembliesRequired = [];
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                if (loadedAssemblies[i].IsDynamic) continue;
                string assemblyName = loadedAssemblies[i].GetName().Name;
                if (!string.IsNullOrEmpty(assemblyName) && assemblyName != "UserAssembly")
                    assembliesRequired.Add(assemblyName);
            }

            for (int i = 0; i < KniCoreAssemblyNames.Length; i++)
                assembliesRequired.Add(KniCoreAssemblyNames[i]);

            for (int i = 0; i < plugins.Count; i++)
            {
                string[] assemblies = plugins[i].RequiredAssemblies;
                for (int j = 0; j < assemblies.Length; j++)
                    assembliesRequired.Add(assemblies[j]);
            }

            for (int i = 0; i < BclAssemblyNames.Length; i++)
                assembliesRequired.Add(BclAssemblyNames[i]);

            List<MetadataReference> metadataReferences = [];
            List<string> failedAssemblies = [];
            int resolved = 0;
            int total = assembliesRequired.Count;

            // First pass: serve everything already in the success cache and collect the
            // misses. After warm-up this typically leaves zero misses, so no PE metadata
            // is re-parsed and no reference service is even created.
            List<string> missingAssemblies = [];
            foreach (string assemblyName in assembliesRequired)
            {
                if (_referenceCache.TryGetValue(assemblyName, out MetadataReference cached))
                {
                    metadataReferences.Add(cached);
                    onProgress?.Invoke(++resolved, total);
                }
                else
                {
                    missingAssemblies.Add(assemblyName);
                }
            }

            // Second pass: resolve only the misses. Create a fresh service so a stale
            // failure cached inside the service can't permanently hide an assembly that
            // is now loaded; the success cache (not the service) is what makes warm
            // compiles skip resolution entirely.
            if (missingAssemblies.Count > 0)
            {
                _referenceService = new(_navigationManager);
                for (int i = 0; i < missingAssemblies.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string assemblyName = missingAssemblies[i];
                    AssemblyDetails assemblyDetails = new() { Name = assemblyName };
                    try
                    {
                        MetadataReference metadataReference = await _referenceService.CreateAsync(assemblyDetails);
                        if (metadataReference != null)
                        {
                            metadataReferences.Add(metadataReference);
                            _referenceCache[assemblyName] = metadataReference; // cache successes only
                        }
                        else
                        {
                            failedAssemblies.Add(assemblyName);
                        }
                    }
                    catch
                    {
                        failedAssemblies.Add(assemblyName);
                    }
                    onProgress?.Invoke(++resolved, total);
                }
            }

            if (failedAssemblies.Count > 0)
                Console.WriteLine($"[XnaFiddle] {failedAssemblies.Count} assembl{(failedAssemblies.Count == 1 ? "y" : "ies")} failed to resolve: {string.Join(", ", failedAssemblies)}");

            return (metadataReferences, failedAssemblies, versionInfo);
        }

        private void ForceLoadAssemblies()
        {
            // Force-load optional assemblies into the AppDomain.
            // Blazor WASM lazy-loads assemblies; without this they may not be present when
            // a #code= link is opened on a fresh page, causing silent metadata-fetch failures.
            for (int i = 0; i < KniCoreAssemblyNames.Length; i++)
            {
                try { Assembly.Load(KniCoreAssemblyNames[i]); }
                catch { /* already loaded, or genuinely absent — handled below */ }
            }
            IReadOnlyList<ILibraryPlugin> plugins = _libraryRegistry.Plugins;
            for (int i = 0; i < plugins.Count; i++)
            {
                string[] assemblies = plugins[i].RequiredAssemblies;
                for (int j = 0; j < assemblies.Length; j++)
                {
                    try { Assembly.Load(assemblies[j]); }
                    catch { /* already loaded, or genuinely absent — handled below */ }
                }
            }
            for (int i = 0; i < BclAssemblyNames.Length; i++)
            {
                try { Assembly.Load(BclAssemblyNames[i]); }
                catch { /* already loaded, or genuinely absent — handled below */ }
            }
        }

        public async Task<CompilationResult> CompileAsync(string sourceCode, Action<int, int> onProgress = null, CancellationToken cancellationToken = default)
        {
            string log = "";

            // Parse
            string[] preprocessorSymbols = ["BLAZORGL"];
            CSharpParseOptions parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.LatestMajor)
                .WithPreprocessorSymbols(preprocessorSymbols);

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);

            (List<MetadataReference> metadataReferences, List<string> failedAssemblies, string versionInfo) =
                await GetMetadataReferencesAsync(onProgress, cancellationToken);

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
