using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XnaFiddle.Intellisense;

namespace XnaFiddle.Tests.Intellisense;

public class SignatureFormatterTests
{
    private static (IMethodSymbol method, SemanticModel model, int position) CompileAndGetMethod(
        string source, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        MemberDeclarationSyntax decl =
            root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodName)
            ?? (MemberDeclarationSyntax)root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == methodName);

        Assert.NotNull(decl);
        var symbol = model.GetDeclaredSymbol(decl) as IMethodSymbol;
        Assert.NotNull(symbol);

        // Position: use the start of the declaration (inside the type).
        return (symbol!, model, decl.SpanStart);
    }

    [Fact]
    public void SimpleMethod_LabelAndParameterRanges()
    {
        const string src = "class C { void M(int x, string y) {} }";
        var (method, model, pos) = CompileAndGetMethod(src, "M");

        var result = SignatureFormatter.Format(method, model, pos);

        Assert.Equal("void C.M(int x, string y)", result.Label);
        Assert.Equal(2, result.ParameterRanges.Length);

        var p0 = result.ParameterRanges[0];
        var p1 = result.ParameterRanges[1];
        Assert.Equal("int x", result.Label.Substring(p0.Start, p0.End - p0.Start));
        Assert.Equal("string y", result.Label.Substring(p1.Start, p1.End - p1.Start));
    }

    [Fact]
    public void Constructor_NoReturnType_LabelStartsWithTypeName()
    {
        const string src = "class Widget { public Widget(int x) {} }";
        var (method, model, pos) = CompileAndGetMethod(src, "Widget");

        var result = SignatureFormatter.Format(method, model, pos);

        Assert.StartsWith("Widget(", result.Label);
        Assert.DoesNotContain("void", result.Label);
        Assert.Single(result.ParameterRanges);
    }

    [Fact]
    public void ParamsArray_ShowsParamsModifier()
    {
        const string src = "class C { void M(params int[] xs) {} }";
        var (method, model, pos) = CompileAndGetMethod(src, "M");

        var result = SignatureFormatter.Format(method, model, pos);

        Assert.Contains("params", result.Label);
    }

    [Fact]
    public void DefaultValue_IsShown()
    {
        const string src = "class C { void M(int x = 5) {} }";
        var (method, model, pos) = CompileAndGetMethod(src, "M");

        var result = SignatureFormatter.Format(method, model, pos);

        Assert.Contains("= 5", result.Label);
    }

    [Fact]
    public void GenericMethod_IncludesTypeParameters()
    {
        const string src = "class C { void M<T>(T value) {} void Caller() { M<int>(1); } }";
        // Pull the symbol out of the invocation so TypeArguments is concrete.
        var tree = CSharpSyntaxTree.ParseText(src);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create("T", new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        var symbol = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;

        var result = SignatureFormatter.Format(symbol, model, invocation.SpanStart);

        Assert.Contains("<int>", result.Label);
    }

    [Fact]
    public void MinimalQualification_UsesShortNameWhenNamespaceImported()
    {
        // Define a type in a namespace, then call a method that takes it — with `using` in scope.
        const string src = @"
using N;
namespace N { public class Widget {} }
class C { void M(N.Widget w) {} }";
        var (method, model, _) = CompileAndGetMethod(src, "M");

        // Position: inside the C class body, where `using N;` is in scope.
        var tree = model.SyntaxTree;
        var classC = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "C");
        int inScopePos = classC.OpenBraceToken.Span.End;

        var result = SignatureFormatter.Format(method, model, inScopePos);

        Assert.Contains("Widget w", result.Label);
        Assert.DoesNotContain("N.Widget", result.Label);
    }
}
