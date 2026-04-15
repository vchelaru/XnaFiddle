using System.Text;
using Microsoft.CodeAnalysis;

namespace XnaFiddle.Intellisense
{
    /// <summary>
    /// Builds a human-readable signature label for a method symbol, along with
    /// per-parameter character offsets within the label. Monaco uses the offsets
    /// to bold the active parameter unambiguously — string-substring fallbacks are
    /// brittle when two parameters share a type.
    /// </summary>
    public static class SignatureFormatter
    {
        /// <summary>
        /// Format for a single parameter within a signature label. Intentionally minimal:
        /// type + name + default value + modifiers (ref/out/params/this). Type qualification
        /// is driven by <see cref="ISymbol.ToMinimalDisplayString"/>, which respects in-scope
        /// usings so e.g. <c>BlendState</c> renders unqualified when
        /// <c>using Microsoft.Xna.Framework.Graphics;</c> is present.
        /// </summary>
        public static readonly SymbolDisplayFormat SigHelpParamFormat = new SymbolDisplayFormat(
            parameterOptions: SymbolDisplayParameterOptions.IncludeType
                | SymbolDisplayParameterOptions.IncludeName
                | SymbolDisplayParameterOptions.IncludeDefaultValue
                | SymbolDisplayParameterOptions.IncludeParamsRefOut
                | SymbolDisplayParameterOptions.IncludeExtensionThis,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        public readonly struct ParameterRange
        {
            public int Start { get; }
            public int End { get; }
            public ParameterRange(int start, int end) { Start = start; End = end; }
        }

        public readonly struct FormattedSignature
        {
            public string Label { get; }
            public ParameterRange[] ParameterRanges { get; }
            public FormattedSignature(string label, ParameterRange[] ranges)
            {
                Label = label;
                ParameterRanges = ranges;
            }
        }

        public static FormattedSignature Format(IMethodSymbol method, SemanticModel semanticModel, int position)
        {
            var sb = new StringBuilder();
            if (method.MethodKind == MethodKind.Constructor)
            {
                sb.Append(method.ContainingType.ToMinimalDisplayString(semanticModel, position));
            }
            else
            {
                sb.Append(method.ReturnType.ToMinimalDisplayString(semanticModel, position));
                sb.Append(' ');
                sb.Append(method.ContainingType.ToMinimalDisplayString(semanticModel, position));
                sb.Append('.');
                sb.Append(method.Name);
                if (method.IsGenericMethod && method.TypeArguments.Length > 0)
                {
                    sb.Append('<');
                    for (int t = 0; t < method.TypeArguments.Length; t++)
                    {
                        if (t > 0) sb.Append(", ");
                        sb.Append(method.TypeArguments[t].ToMinimalDisplayString(semanticModel, position));
                    }
                    sb.Append('>');
                }
            }
            sb.Append('(');
            var ranges = new ParameterRange[method.Parameters.Length];
            for (int p = 0; p < method.Parameters.Length; p++)
            {
                if (p > 0) sb.Append(", ");
                int start = sb.Length;
                var param = method.Parameters[p];
                sb.Append(param.ToMinimalDisplayString(semanticModel, position, SigHelpParamFormat));
                int end = sb.Length;
                ranges[p] = new ParameterRange(start, end);
            }
            sb.Append(')');

            return new FormattedSignature(sb.ToString(), ranges);
        }
    }
}
