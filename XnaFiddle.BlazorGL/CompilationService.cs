using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace XnaFiddle
{
    public class CompilationService
    {
        private readonly LibraryRegistry _libraryRegistry;

        // Persistent success-only cache of resolved metadata references, keyed by assembly
        // name. Parsing each assembly's PE metadata is the dominant cost of a recompile in
        // single-threaded WASM, so once an assembly resolves we keep its MetadataReference
        // for all subsequent compiles. Only successes are cached; failures are left out so
        // they are retried (an assembly absent on one compile may be loaded by the next).
        private readonly Dictionary<string, MetadataReference> _referenceCache = [];

        public CompilationService(LibraryRegistry libraryRegistry)
        {
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
            // nkast.Wasm.Clipboard is intentionally absent: the KniSB submodule bump to
            // 10.0.2 dropped it as a KNI dependency (XnaFiddle never referenced it directly —
            // CopyToClipboard in Index.razor.cs calls navigator.clipboard via JsRuntime).
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
        /// environment, resolving each assembly's MetadataReference from its already-loaded
        /// in-process Assembly (see TryCreateMetadataReference). Used by CompileAsync and
        /// shared with IntellisenseService so the completion workspace sees the same
        /// BCL/KNI/plugin surface the compile will see.
        /// </summary>
        // Not actually async anymore: every assembly is resolved from the already-loaded
        // in-process Assembly (see the second-pass loop below), so there is nothing left to
        // await. Kept Task-returning to avoid touching the CompileAsync/IntellisenseService
        // call sites, which still `await` this.
        public Task<(List<MetadataReference> References, List<string> FailedAssemblies, string VersionInfo)>
            GetMetadataReferencesAsync(Action<int, int> onProgress = null, bool includeShadowDusk = false, CancellationToken cancellationToken = default)
        {
            ForceLoadAssemblies();

            IReadOnlyList<ILibraryPlugin> plugins = _libraryRegistry.Plugins;

            // Collect library version info for display in the diagnostics panel.
            var versionParts = new List<string>();
            versionParts.Add($"KNI {GetAssemblyVersion("Kni.Platform")}");
            for (int i = 0; i < plugins.Count; i++)
            {
                string[] assemblyNames = plugins[i].VersionAssemblies;
                if (assemblyNames.Length == 0) continue;
                string version = assemblyNames.Select(GetAssemblyVersion)
                    .FirstOrDefault(v => v != "?" && v != "0.0.0.0" && v != "0.0.0")
                    ?? GetAssemblyVersion(assemblyNames[0]);
                versionParts.Add($"{plugins[i].Name} {version}");
            }
            if (includeShadowDusk)
                versionParts.Add($"ShadowDusk {PackageVersions.ShadowDusk}");
            string versionInfo = string.Join("  ·  ", versionParts);

            HashSet<string> assembliesRequired = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> assembliesToExpand = new(StringComparer.OrdinalIgnoreCase);
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                if (loadedAssemblies[i].IsDynamic) continue;
                string assemblyName = loadedAssemblies[i].GetName().Name;
                if (!string.IsNullOrEmpty(assemblyName) && assemblyName != "UserAssembly")
                    assembliesRequired.Add(assemblyName);
            }

            for (int i = 0; i < KniCoreAssemblyNames.Length; i++)
            {
                assembliesRequired.Add(KniCoreAssemblyNames[i]);
                assembliesToExpand.Add(KniCoreAssemblyNames[i]);
            }

            for (int i = 0; i < plugins.Count; i++)
            {
                string[] assemblies = plugins[i].RequiredAssemblies;
                for (int j = 0; j < assemblies.Length; j++)
                {
                    assembliesRequired.Add(assemblies[j]);
                    assembliesToExpand.Add(assemblies[j]);
                }
            }

            for (int i = 0; i < BclAssemblyNames.Length; i++)
            {
                assembliesRequired.Add(BclAssemblyNames[i]);
                assembliesToExpand.Add(BclAssemblyNames[i]);
            }

            AddReferencedAssemblyNames(assembliesRequired, loadedAssemblies, assembliesToExpand);

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

            // Second pass: resolve only the misses. Every assembly XnaFiddle needs a reference
            // for is, by construction, already loaded into this WASM process (the compiled user
            // code has to run against those same loaded assemblies), so no HTTP fetch is needed —
            // Assembly.Load here just gets/triggers-load of the in-process Assembly object
            // (covers names not in ForceLoadAssemblies's fixed list, e.g. transitively-referenced
            // BCL assemblies found via AddReferencedAssemblyNames), then its raw PE metadata is
            // read directly out of the loaded module via TryCreateMetadataReference.
            for (int i = 0; i < missingAssemblies.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string assemblyName = missingAssemblies[i];
                try
                {
                    Assembly assembly = Assembly.Load(assemblyName);
                    if (TryCreateMetadataReference(assembly, out MetadataReference metadataReference))
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

            if (failedAssemblies.Count > 0)
                Console.WriteLine($"[XnaFiddle] {failedAssemblies.Count} assembl{(failedAssemblies.Count == 1 ? "y" : "ies")} failed to resolve: {string.Join(", ", failedAssemblies)}");

            return Task.FromResult((metadataReferences, failedAssemblies, versionInfo));
        }

        private static void AddReferencedAssemblyNames(
            HashSet<string> assembliesRequired,
            Assembly[] loadedAssemblies,
            HashSet<string> assembliesToExpand)
        {
            var loadedByName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                if (loadedAssemblies[i].IsDynamic) continue;
                string name = loadedAssemblies[i].GetName().Name;
                if (string.IsNullOrEmpty(name)) continue;
                loadedByName[name] = loadedAssemblies[i];
            }

            Queue<string> queue = new(assembliesToExpand);
            HashSet<string> visited = new(assembliesToExpand, StringComparer.OrdinalIgnoreCase);
            while (queue.Count > 0)
            {
                string assemblyName = queue.Dequeue();
                if (!loadedByName.TryGetValue(assemblyName, out Assembly loadedAssembly)) continue;

                AssemblyName[] references;
                try
                {
                    references = loadedAssembly.GetReferencedAssemblies();
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < references.Length; i++)
                {
                    string referencedAssemblyName = references[i].Name;
                    if (string.IsNullOrEmpty(referencedAssemblyName)) continue;
                    if (!visited.Add(referencedAssemblyName)) continue;

                    assembliesRequired.Add(referencedAssemblyName);
                    queue.Enqueue(referencedAssemblyName);
                }
            }
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

        // Wraps an already-loaded in-process Assembly as a Roslyn MetadataReference by reading
        // its raw PE metadata directly out of the loaded module via TryGetRawMetadata, instead
        // of fetching the assembly's bytes over HTTP. This works because every assembly Roslyn
        // needs a reference for is, by construction, already loaded into this WASM process — the
        // compiled user code has to execute against those same loaded assemblies. The returned
        // blob pointer is stable for the process lifetime (it points into the assembly's own
        // metadata, not a copy), so no further ownership handling is needed.
        private static bool TryCreateMetadataReference(Assembly assembly, out MetadataReference reference)
        {
            reference = null;
            try
            {
                unsafe
                {
                    if (!assembly.TryGetRawMetadata(out byte* blob, out int length))
                        return false;
                    ModuleMetadata moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                    AssemblyMetadata assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
                    reference = assemblyMetadata.GetReference();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<CompilationResult> CompileAsync(string sourceCode, Action<int, int> onProgress = null, bool includeShadowDusk = false, CancellationToken cancellationToken = default)
        {
            string log = "";

            // Parse
            string[] preprocessorSymbols = ["BLAZORGL"];
            CSharpParseOptions parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.LatestMajor)
                .WithPreprocessorSymbols(preprocessorSymbols);

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);

            (List<MetadataReference> metadataReferences, List<string> failedAssemblies, string versionInfo) =
                await GetMetadataReferencesAsync(onProgress, includeShadowDusk, cancellationToken);

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
