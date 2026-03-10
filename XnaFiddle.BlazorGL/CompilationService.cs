using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MetadataReferenceService.Abstractions.Types;
using MetadataReferenceService.BlazorWasm;

namespace XnaFiddle
{
    public static class CompilationService
    {
        private static BlazorWasmMetadataReferenceService _referenceService;

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
        }

        public static async Task<CompilationResult> CompileAsync(string sourceCode)
        {
            _referenceService ??= new(Program.NavigationManager);
            string log = "";

            // Parse
            string[] preprocessorSymbols = ["BLAZORGL"];
            CSharpParseOptions parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.LatestMajor)
                .WithPreprocessorSymbols(preprocessorSymbols);

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);

            // Collect assembly names from loaded assemblies + known KNI assemblies
            HashSet<string> assembliesRequired = [];
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                string assemblyName = loadedAssemblies[i].GetName().Name;
                if (!string.IsNullOrEmpty(assemblyName))
                    assembliesRequired.Add(assemblyName);
            }

            // Add KNI assemblies that may not be loaded yet due to lazy loading
            for (int i = 0; i < KniAssemblyNames.Length; i++)
                assembliesRequired.Add(KniAssemblyNames[i]);

            // Fetch metadata references
            List<MetadataReference> metadataReferences = [];
            int failedCount = 0;

            foreach (string assemblyName in assembliesRequired)
            {
                AssemblyDetails assemblyDetails = new() { Name = assemblyName };
                try
                {
                    MetadataReference metadataReference = await _referenceService.CreateAsync(assemblyDetails);
                    if (metadataReference != null)
                        metadataReferences.Add(metadataReference);
                    else
                        failedCount++;
                }
                catch
                {
                    failedCount++;
                }
            }

            log += "Resolved " + metadataReferences.Count + "/" + assembliesRequired.Count + " assembly references.\n";

            // Compile
            CSharpCompilationOptions compilationOptions = new(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                reportSuppressedDiagnostics: true,
                metadataImportOptions: MetadataImportOptions.Public,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release
            );

            CSharpCompilation compilation = CSharpCompilation.Create(
                "UserAssembly", [syntaxTree], metadataReferences.ToArray(), compilationOptions);

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
                Diagnostics = diagnosticInfos
            };
        }
    }
}
