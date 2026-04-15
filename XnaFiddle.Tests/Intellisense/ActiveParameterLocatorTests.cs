using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XnaFiddle.Intellisense;

namespace XnaFiddle.Tests.Intellisense;

public class ActiveParameterLocatorTests
{
    private static (ArgumentListSyntax argList, string source) ParseInvocation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        return (invocation.ArgumentList, source);
    }

    [Fact]
    public void CursorImmediatelyAfterOpenParen_ReturnsZero()
    {
        const string src = "class C { void X() { M(); } void M(int a, int b) {} }";
        var (argList, _) = ParseInvocation(src);
        int pos = argList.OpenParenToken.Span.End;
        Assert.Equal(0, ActiveParameterLocator.FindActiveParameter(argList, pos));
    }

    [Fact]
    public void CursorAfterOneComma_ReturnsOne()
    {
        const string src = "class C { void X() { M(1, 2); } void M(int a, int b) {} }";
        var (argList, _) = ParseInvocation(src);
        // Position just after the first (and only) comma at top level.
        var firstComma = argList.Arguments.GetSeparators().First();
        int pos = firstComma.Span.End;
        Assert.Equal(1, ActiveParameterLocator.FindActiveParameter(argList, pos));
    }

    [Fact]
    public void CursorAfterTwoCommas_ReturnsTwo()
    {
        const string src = "class C { void X() { M(1, 2, 3); } void M(int a, int b, int c) {} }";
        var (argList, _) = ParseInvocation(src);
        var separators = argList.Arguments.GetSeparators().ToList();
        int pos = separators[1].Span.End;
        Assert.Equal(2, ActiveParameterLocator.FindActiveParameter(argList, pos));
    }

    [Fact]
    public void NestedCallCommas_AreNotCounted()
    {
        // Outer M has two arguments: f(1, 2) and some other arg. The inner commas
        // inside f(1, 2) must NOT inflate the active parameter index for M.
        const string src = "class C { int f(int a, int b) => 0; void X() { M(f(1, 2), 3); } void M(int a, int b) {} }";
        // Grab the outer invocation (M), not the inner f.
        var tree = CSharpSyntaxTree.ParseText(src);
        var root = tree.GetRoot();
        var outerInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression is IdentifierNameSyntax id && id.Identifier.Text == "M");
        var argList = outerInvocation.ArgumentList;

        // Cursor just after the top-level comma (between f(1,2) and 3).
        var topLevelSeparator = argList.Arguments.GetSeparators().First();
        int pos = topLevelSeparator.Span.End;

        // Only one top-level comma has been crossed, so active param == 1.
        Assert.Equal(1, ActiveParameterLocator.FindActiveParameter(argList, pos));
    }

    [Fact]
    public void CursorJustBeforeOuterCommaButAfterInnerCommas_StillZero()
    {
        // Cursor placed inside f's arg list after its inner comma — but we ask relative
        // to the OUTER argument list. The inner comma is not a separator of the outer
        // argument list, so the count must still be 0.
        const string src = "class C { int f(int a, int b) => 0; void X() { M(f(1, 2), 3); } void M(int a, int b) {} }";
        var tree = CSharpSyntaxTree.ParseText(src);
        var root = tree.GetRoot();
        var outerInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression is IdentifierNameSyntax id && id.Identifier.Text == "M");
        var innerInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression is IdentifierNameSyntax id && id.Identifier.Text == "f");

        var innerComma = innerInvocation.ArgumentList.Arguments.GetSeparators().First();
        int pos = innerComma.Span.End;

        Assert.Equal(0, ActiveParameterLocator.FindActiveParameter(outerInvocation.ArgumentList, pos));
    }
}
