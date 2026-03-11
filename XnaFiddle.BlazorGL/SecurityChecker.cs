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
            "System.Environment",
            "System.Activator",
        ];

        // Walks the syntax tree using the semantic model and returns errors for any
        // forbidden namespace or type references found in the user's source code.
        public static List<CompilationService.DiagnosticInfo> Check(CSharpCompilation compilation, SyntaxTree syntaxTree)
        {
            var errors = new List<CompilationService.DiagnosticInfo>();
            SemanticModel model = compilation.GetSemanticModel(syntaxTree);

            foreach (SyntaxNode node in syntaxTree.GetRoot().DescendantNodes())
            {
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

            return null;
        }
    }
}
