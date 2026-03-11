using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XnaFiddle
{
    public static class SecurityChecker
    {
        // Namespaces entirely forbidden in user code.
        public static readonly IReadOnlyList<string> ForbiddenNamespaces =
        [
            "System.Reflection",
            "System.Reflection.Emit",
            "System.Linq.Expressions",
            "System.Runtime.InteropServices.JavaScript",
            "System.Runtime.InteropServices",
            "System.Net",
            "System.Security",
            "Microsoft.CodeAnalysis",
            "Microsoft.JSInterop",
            "nkast.Wasm.Dom",
            "nkast.Wasm.XHR",
            "nkast.Wasm.JSInterop",
            "nkast.Wasm.WebClipboard",
            "nkast.Wasm.Canvas",
            "nkast.Wasm.Audio",
            "nkast.Wasm.Media",
            "nkast.Wasm.XR",
            "TextCopy",
            "Kni.Platform",
        ];

        // Specific types forbidden in user code (namespace not fully blocked).
        public static readonly IReadOnlyList<string> ForbiddenTypes =
        [
            "System.IO.File",
            "System.IO.Directory",
            "System.IO.FileStream",
            "System.Diagnostics.Process",
            "System.AppDomain",
            "System.Runtime.Loader.AssemblyLoadContext",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Threading.Thread",
            "System.Threading.Timer",
            "System.Threading.SynchronizationContext",
            "System.Timers.Timer",
            "System.Threading.Tasks.Parallel",
            "System.Diagnostics.StackTrace",
            "System.Environment",
            "System.Activator",
        ];

        // Specific methods forbidden in user code (type not fully blocked).
        public static readonly IReadOnlyList<string> ForbiddenMethods =
        [
            "System.Threading.Tasks.Task.Run",
            "System.Threading.Tasks.Task.Start",
            "System.Threading.Tasks.TaskFactory.StartNew",
            "System.Threading.ThreadPool.QueueUserWorkItem",
            "System.Threading.ThreadPool.UnsafeQueueUserWorkItem",
        ];

        // Walks the syntax tree using the semantic model and returns errors for any
        // forbidden namespace or type references found in the user's source code.
        public static List<CompilationService.DiagnosticInfo> Check(CSharpCompilation compilation, SyntaxTree syntaxTree)
        {
            var errors = new List<CompilationService.DiagnosticInfo>();
            SemanticModel model = compilation.GetSemanticModel(syntaxTree);

            foreach (SyntaxNode node in syntaxTree.GetRoot().DescendantNodes())
            {
                // Detect 'dynamic' keyword — symbol is null for dynamic so must be caught before the null-guard.
                // TypeKind.Dynamic is set by Roslyn on the IdentifierNameSyntax for the 'dynamic' contextual keyword.
                if (node is IdentifierNameSyntax { Identifier.Text: "dynamic" })
                {
                    if (model.GetTypeInfo(node).Type?.TypeKind == TypeKind.Dynamic)
                    {
                        FileLinePositionSpan dynSpan = node.GetLocation().GetMappedLineSpan();
                        errors.Add(new CompilationService.DiagnosticInfo
                        {
                            StartLine = dynSpan.StartLinePosition.Line + 1,
                            StartCol = dynSpan.StartLinePosition.Character + 1,
                            EndLine = dynSpan.EndLinePosition.Line + 1,
                            EndCol = dynSpan.EndLinePosition.Character + 1,
                            Message = "Use of 'dynamic' is not permitted in XnaFiddle.",
                            Severity = "error"
                        });
                        continue;
                    }
                }

                // Only check leaf name nodes — each identifier is visited exactly once.
                if (node is not IdentifierNameSyntax and not GenericNameSyntax)
                    continue;

                ISymbol symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol == null) continue;

                string message = GetForbiddenMessage(symbol);
                if (message == null) continue;

                FileLinePositionSpan lineSpan = node.GetLocation().GetMappedLineSpan();
                errors.Add(new CompilationService.DiagnosticInfo
                {
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    StartCol = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndCol = lineSpan.EndLinePosition.Character + 1,
                    Message = message,
                    Severity = "error"
                });
            }

            return errors;
        }

        private static string GetForbiddenMessage(ISymbol symbol)
        {
            // Resolve the namespace: for namespace symbols use themselves, otherwise ContainingNamespace.
            string ns = symbol is INamespaceSymbol nsSym
                ? nsSym.ToDisplayString()
                : symbol.ContainingNamespace?.ToDisplayString() ?? "";

            foreach (string forbidden in ForbiddenNamespaces)
            {
                if (ns == forbidden || ns.StartsWith(forbidden + ".", System.StringComparison.Ordinal))
                    return $"Use of '{forbidden}' is not permitted in XnaFiddle.";
            }

            // Check the type itself or its containing type against the forbidden types list.
            ITypeSymbol typeToCheck = symbol as ITypeSymbol ?? symbol.ContainingType;
            if (typeToCheck != null)
            {
                string fullName = typeToCheck.ToDisplayString();
                // Strip generic parameters before comparing (e.g. List<T> → List).
                int genericIdx = fullName.IndexOf('<');
                if (genericIdx >= 0) fullName = fullName[..genericIdx];

                foreach (string forbidden in ForbiddenTypes)
                {
                    if (fullName == forbidden)
                        return $"Use of '{fullName}' is not permitted in XnaFiddle.";
                }
            }

            // Check for forbidden method calls (e.g. Task.Run).
            if (symbol is IMethodSymbol method)
            {
                string containingType = method.ContainingType?.ToDisplayString() ?? "";
                int genericIdx2 = containingType.IndexOf('<');
                if (genericIdx2 >= 0) containingType = containingType[..genericIdx2];
                string fullMethodName = $"{containingType}.{method.Name}";
                foreach (string forbidden in ForbiddenMethods)
                {
                    if (fullMethodName == forbidden)
                        return $"Use of '{forbidden}' is not permitted in XnaFiddle.";
                }
            }

            return null;
        }
    }
}
