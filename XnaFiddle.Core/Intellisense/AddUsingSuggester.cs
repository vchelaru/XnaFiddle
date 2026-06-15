using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XnaFiddle.Intellisense
{
    /// <summary>
    /// Pure-logic helpers for the "Add using" quick-fix code action. The orchestration
    /// lives in <c>IntellisenseService.GetCodeActionsAsync</c>; this file holds the
    /// parts that don't depend on a Roslyn workspace so they stay testable outside WASM.
    ///
    /// Design note: we deliberately do NOT use Roslyn's
    /// <c>CSharpAddImportCodeFixProvider</c>. Pulling in
    /// <c>Microsoft.CodeAnalysis.(CSharp.)Features</c> would balloon the WASM payload,
    /// require reflection/IVT access to internal types, and — worse — drag in the full
    /// MEF composition of unrelated code-fixes and refactorings we do NOT want to
    /// surface in this editor (generate constructor, convert to switch, etc.). The
    /// allowlisted, hand-rolled approach keeps the surface tight and the payload small.
    /// </summary>
    public static class AddUsingSuggester
    {
        /// <summary>
        /// Namespaces the "Add using" quick fix is allowed to suggest from. Kept intentionally
        /// tight to avoid suggestion noise (e.g. Vector2 ambiguity between System.Numerics and
        /// Microsoft.Xna.Framework) and to keep first-invocation symbol-search cost bounded on
        /// WASM's single thread. Expand deliberately when users hit a concrete missing case.
        /// See docs/intellisense skill section "Add using quick fix" for rationale.
        /// </summary>
        public static readonly string[] NamespaceAllowlist =
        {
            "Microsoft.Xna.Framework",
            "Gum",
        };

        /// <summary>
        /// Returns true if <paramref name="ns"/> exactly matches or is a child namespace of
        /// one of the entries in <paramref name="allowlist"/>. A plain <c>StartsWith</c>
        /// would wrongly match <c>GumFoo</c> against <c>Gum</c>; we require either exact
        /// equality or a trailing <c>'.'</c>.
        /// </summary>
        public static bool IsAllowedNamespace(string ns, IReadOnlyList<string> allowlist)
        {
            if (string.IsNullOrEmpty(ns)) return false;
            if (allowlist == null) return false;
            for (int i = 0; i < allowlist.Count; i++)
            {
                var prefix = allowlist[i];
                if (string.IsNullOrEmpty(prefix)) continue;
                if (ns == prefix) return true;
                if (ns.Length > prefix.Length
                    && ns[prefix.Length] == '.'
                    && ns.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the zero-based character offset at which a new <c>using NS;</c> line
        /// should be inserted, along with the text to insert (always ending in "\n").
        ///
        /// Rules:
        /// - If the file has one or more top-level <c>using</c> directives, insert
        ///   immediately after the last one (preserves existing sort/group).
        /// - Otherwise, if the file has a file-scoped namespace declaration
        ///   (<c>namespace Foo;</c>), insert before it (usings must precede it).
        /// - Otherwise, insert at offset 0.
        ///
        /// The caller is responsible for turning this offset into a Monaco
        /// <c>{line, column}</c> pair.
        /// </summary>
        public static (int offset, string text) ComputeInsertion(SyntaxNode root, string namespaceToImport)
        {
            string line = "using " + namespaceToImport + ";\n";
            if (root is not CompilationUnitSyntax compilationUnit)
            {
                return (0, line);
            }

            if (compilationUnit.Usings.Count > 0)
            {
                var lastUsing = compilationUnit.Usings[compilationUnit.Usings.Count - 1];
                // FullSpan.End includes the trailing end-of-line trivia, so the
                // new line slots cleanly after the final existing using directive.
                return (lastUsing.FullSpan.End, line);
            }

            var fileScoped = compilationUnit.Members
                .OfType<FileScopedNamespaceDeclarationSyntax>()
                .FirstOrDefault();
            if (fileScoped != null)
            {
                // Insert before the file-scoped namespace (including its leading trivia)
                // so the using ends up above it.
                int insertAt = fileScoped.FullSpan.Start;
                return (insertAt, line);
            }

            return (0, line);
        }

        /// <summary>
        /// Walks up from the token at <paramref name="position"/> to the smallest simple
        /// name node (<see cref="IdentifierNameSyntax"/> or <see cref="GenericNameSyntax"/>)
        /// whose span contains it. Returns null if no such node is found. Used to locate
        /// the identifier the user wants a <c>using</c> imported for.
        /// </summary>
        public static SimpleNameSyntax FindNameAtPosition(SyntaxNode root, int position)
        {
            if (root == null) return null;

            // FindToken's behavior at the end of a token span is "next token"; clamp so
            // that a cursor immediately after an identifier still hits it.
            int probe = Math.Max(0, Math.Min(position, root.FullSpan.End));
            if (probe > 0 && probe == root.FullSpan.End) probe = root.FullSpan.End - 1;

            SyntaxToken token;
            try
            {
                token = root.FindToken(probe);
            }
            catch
            {
                return null;
            }

            for (var node = token.Parent; node != null; node = node.Parent)
            {
                if (node is IdentifierNameSyntax id) return id;
                if (node is GenericNameSyntax gn) return gn;
                // Stop once we walk out of name syntax entirely — no sense walking up
                // into statements/blocks, that would pull in the wrong identifier.
                if (node is StatementSyntax) return null;
                if (node is MemberDeclarationSyntax) return null;
            }
            return null;
        }
    }
}
