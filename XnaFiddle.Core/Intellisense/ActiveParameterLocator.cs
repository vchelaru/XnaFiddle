using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XnaFiddle.Intellisense
{
    /// <summary>
    /// Determines the zero-based active parameter index for a cursor position
    /// inside an argument list. Only top-level comma separators (direct children
    /// of the argument list) are counted, so commas inside nested calls or
    /// parenthesized expressions don't inflate the count.
    /// </summary>
    public static class ActiveParameterLocator
    {
        public static int FindActiveParameter(ArgumentListSyntax argumentList, int position)
        {
            if (argumentList == null) return 0;

            int activeParameter = 0;
            foreach (var sep in argumentList.Arguments.GetSeparators())
            {
                if (sep.SpanStart < position) activeParameter++;
                else break;
            }
            return activeParameter;
        }
    }
}
