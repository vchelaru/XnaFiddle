using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XnaFiddle
{
    public class SnippetRevertResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsGum { get; set; }
        public bool IsAposShapes { get; set; }
        public bool IsMonoGameExtended { get; set; }
        public List<string> ExtraUsings { get; set; } = new();
        // null = no user content detected for that section
        public string Members { get; set; }
        public string Initialize { get; set; }
        public string LoadContent { get; set; }
        public string Update { get; set; }
        public string Draw { get; set; }
    }

    public static class SnippetReverter
    {
        static readonly HashSet<string> DefaultUsings = new(StringComparer.Ordinal)
        {
            "System", "Microsoft.Xna.Framework",
            "Microsoft.Xna.Framework.Graphics", "Microsoft.Xna.Framework.Input"
        };
        static readonly string[] GumUsings            = { "Gum", "Gum.GueDeriving", "Gum", "Gum.GueDeriving", "Gum.Forms", "Gum.Forms.Controls", "Gum.Mvvm" };
        static readonly string[] AposShapesUsings     = { "Apos.Shapes" };
        static readonly string[] MonoGameExtUsings    = { "MonoGame.Extended" };

        // Exact texts of members injected by scaffold/presets — must never appear in snippet.Members
        static readonly HashSet<string> InjectedMembers = new(StringComparer.Ordinal)
        {
            "GraphicsDeviceManager graphics;",
            "GumService GumUI => GumService.Default;",
            "ShapeBatch _shapeBatch;",
            "SpriteBatch _spriteBatch;",
        };

        public static SnippetRevertResult Revert(string code)
        {
            var result = new SnippetRevertResult();
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetCompilationUnitRoot();

                // ── Detect preset flags from using directives ────────────────────
                var allUsings = root.Usings
                    .Select(u => u.Name?.ToString())
                    .Where(n => n != null)
                    .ToHashSet(StringComparer.Ordinal);

                result.IsGum              = GumUsings.Any(allUsings.Contains);
                result.IsAposShapes       = AposShapesUsings.Any(allUsings.Contains);
                result.IsMonoGameExtended = MonoGameExtUsings.Any(allUsings.Contains);

                var knownUsings = new HashSet<string>(DefaultUsings, StringComparer.Ordinal);
                foreach (var u in GumUsings)         knownUsings.Add(u);
                foreach (var u in AposShapesUsings)  knownUsings.Add(u);
                foreach (var u in MonoGameExtUsings) knownUsings.Add(u);
                result.ExtraUsings = allUsings.Where(u => !knownUsings.Contains(u)).ToList();

                // ── Find the Game subclass ───────────────────────────────────────
                var classDecl = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.BaseList?.Types.Any(t =>
                    {
                        var s = t.Type.ToString();
                        return s == "Game" || s == "Microsoft.Xna.Framework.Game";
                    }) == true);

                if (classDecl == null)
                {
                    result.ErrorMessage = "No class extending Game found.";
                    return result;
                }

                // ── Members: fields, properties, helper methods (not injected) ──
                var memberLines = new List<string>();
                foreach (var member in classDecl.Members)
                {
                    if (member is ConstructorDeclarationSyntax) continue;
                    if (member is MethodDeclarationSyntax m &&
                        m.Identifier.Text is "Initialize" or "LoadContent" or "Update" or "Draw") continue;

                    string text = member.ToString().Trim();
                    if (InjectedMembers.Contains(text)) continue;
                    memberLines.Add(DedentNode(member));
                }
                result.Members = memberLines.Count > 0 ? string.Join("\n", memberLines) : null;

                // ── Extract each method body ─────────────────────────────────────
                var methods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                    .ToDictionary(m => m.Identifier.Text, m => m, StringComparer.Ordinal);

                result.Initialize = ExtractBody(methods, "Initialize", result, IsInjectedInitialize);
                result.LoadContent = ExtractBody(methods, "LoadContent", result, IsInjectedLoadContent);
                result.Update      = ExtractBody(methods, "Update",      result, IsInjectedUpdate);
                result.Draw        = ExtractBody(methods, "Draw",        result, IsInjectedDraw);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            return result;
        }

        // ── Injected-statement predicates ────────────────────────────────────────

        static bool IsInjectedInitialize(StatementSyntax stmt, SnippetRevertResult r)
        {
            string t = stmt.ToString().Trim();
            if (t == "base.Initialize();") return true;
            if (r.IsGum && t.StartsWith("GumUI.Initialize(", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool IsInjectedLoadContent(StatementSyntax stmt, SnippetRevertResult r)
        {
            string t = stmt.ToString().Trim();
            if (r.IsAposShapes       && t.StartsWith("_shapeBatch = new ShapeBatch(",   StringComparison.Ordinal)) return true;
            if (r.IsMonoGameExtended && t.StartsWith("_spriteBatch = new SpriteBatch(", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool IsInjectedUpdate(StatementSyntax stmt, SnippetRevertResult r)
        {
            string t = stmt.ToString().Trim();
            if (r.IsGum && t.StartsWith("GumUI.Update(", StringComparison.Ordinal)) return true;
            if (t.StartsWith("base.Update(", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool IsInjectedDraw(StatementSyntax stmt, SnippetRevertResult r)
        {
            string t = stmt.ToString().Trim();
            if (t.StartsWith("GraphicsDevice.Clear(",  StringComparison.Ordinal)) return true;
            if (t.StartsWith("base.Draw(",             StringComparison.Ordinal)) return true;
            if (r.IsAposShapes && t == "_shapeBatch.Begin();") return true;
            if (r.IsAposShapes && t == "_shapeBatch.End();")   return true;
            if (r.IsGum        && t == "GumUI.Draw();")        return true;
            return false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        static string ExtractBody(
            Dictionary<string, MethodDeclarationSyntax> methods,
            string name,
            SnippetRevertResult result,
            Func<StatementSyntax, SnippetRevertResult, bool> isInjected)
        {
            if (!methods.TryGetValue(name, out var method)) return null;
            var body = method.Body;
            if (body == null) return null;

            var kept = new List<string>();
            foreach (var stmt in body.Statements)
            {
                if (!isInjected(stmt, result))
                    kept.Add(DedentNode(stmt));
            }
            return kept.Count > 0 ? string.Join("\n", kept) : null;
        }

        /// <summary>
        /// Returns the text of a syntax node with consistent indentation.
        /// SyntaxNode.ToString() omits the leading trivia of the first token, so lines[0]
        /// has no leading whitespace. Inner lines of compound statements still carry their
        /// original absolute indentation — this method strips the common minimum.
        /// </summary>
        static string DedentNode(SyntaxNode node)
        {
            string text = node.ToString()
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .TrimEnd();

            var lines = text.Split('\n');
            if (lines.Length == 1) return lines[0].Trim();

            // lines[0] has no leading spaces; lines[1+] have absolute source indentation.
            // Find minimum indent among non-empty inner lines.
            int minIndent = int.MaxValue;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                int sp = 0;
                while (sp < lines[i].Length && lines[i][sp] == ' ') sp++;
                if (sp < minIndent) minIndent = sp;
            }
            if (minIndent == int.MaxValue) minIndent = 0;

            var sb = new StringBuilder();
            sb.Append(lines[0].Trim());
            for (int i = 1; i < lines.Length; i++)
            {
                sb.Append('\n');
                var line = lines[i];
                sb.Append(line.Length >= minIndent ? line.Substring(minIndent) : line.TrimStart());
            }
            return sb.ToString();
        }
    }
}
