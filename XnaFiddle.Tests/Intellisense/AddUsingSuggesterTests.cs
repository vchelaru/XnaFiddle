using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XnaFiddle.Intellisense;

namespace XnaFiddle.Tests.Intellisense;

public class AddUsingSuggesterTests
{
    private static readonly string[] Allowlist =
    {
        "Microsoft.Xna.Framework",
        "Gum",
    };

    [Theory]
    [InlineData("Microsoft.Xna.Framework", true)]
    [InlineData("Microsoft.Xna.Framework.Graphics", true)]
    [InlineData("Microsoft.Xna.Framework.Input.Touch", true)]
    [InlineData("Gum", true)]
    [InlineData("Gum.Wireframe", true)]
    [InlineData("Gum.GueDeriving", true)]
    [InlineData("Gum.Forms", true)]
    public void AllowedNamespaces_ReturnTrue(string ns, bool expected)
    {
        Assert.Equal(expected, AddUsingSuggester.IsAllowedNamespace(ns, Allowlist));
    }

    [Theory]
    // The prefix-match rule must NOT treat "GumFoo" as a child of "Gum".
    [InlineData("GumFoo")]
    [InlineData("GumFoo.Bar")]
    [InlineData("System")]
    [InlineData("System.Collections.Generic")]
    [InlineData("Microsoft.Xna")]
    [InlineData("Microsoft.XnaFramework")]
    [InlineData("MonoGameGum")]
    [InlineData("MonoGameGum.Forms")]
    [InlineData("")]
    public void DisallowedNamespaces_ReturnFalse(string ns)
    {
        Assert.False(AddUsingSuggester.IsAllowedNamespace(ns, Allowlist));
    }

    [Fact]
    public void NullNamespace_ReturnsFalse()
    {
        Assert.False(AddUsingSuggester.IsAllowedNamespace(null, Allowlist));
    }

    [Fact]
    public void InsertionOffset_AfterLastUsing_WhenUsingsPresent()
    {
        const string src = "using System;\nusing System.IO;\n\nclass C {}\n";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        var (offset, text) = AddUsingSuggester.ComputeInsertion(root, "Microsoft.Xna.Framework");

        // Should sit immediately after "using System.IO;\n".
        int expected = src.IndexOf("using System.IO;\n") + "using System.IO;\n".Length;
        Assert.Equal(expected, offset);
        Assert.Equal("using Microsoft.Xna.Framework;\n", text);
    }

    [Fact]
    public void InsertionOffset_TopOfFile_WhenNoUsingsAndNoFileScopedNamespace()
    {
        const string src = "class C {}\n";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        var (offset, text) = AddUsingSuggester.ComputeInsertion(root, "Gum");

        Assert.Equal(0, offset);
        Assert.Equal("using Gum;\n", text);
    }

    [Fact]
    public void InsertionOffset_BeforeFileScopedNamespace_WhenNoUsings()
    {
        const string src = "namespace MyApp;\n\nclass C {}\n";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        var (offset, text) = AddUsingSuggester.ComputeInsertion(root, "Microsoft.Xna.Framework");

        // Insertion must come before the file-scoped namespace declaration
        // (file-scoped namespaces require all usings to precede them).
        Assert.Equal(0, offset);
        Assert.Equal("using Microsoft.Xna.Framework;\n", text);
    }

    [Fact]
    public void InsertionOffset_AfterLastUsing_WhenFileScopedNamespaceAlsoPresent()
    {
        const string src = "using System;\n\nnamespace MyApp;\n\nclass C {}\n";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        var (offset, _) = AddUsingSuggester.ComputeInsertion(root, "Gum");

        int expected = src.IndexOf("using System;\n") + "using System;\n".Length;
        Assert.Equal(expected, offset);
    }

    [Fact]
    public void FindNameAtPosition_ReturnsIdentifier_InMethodBody()
    {
        const string src = "class C { void M() { var x = SpriteBatch; } }";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        int position = src.IndexOf("SpriteBatch") + 3;

        var node = AddUsingSuggester.FindNameAtPosition(root, position);

        Assert.NotNull(node);
        Assert.IsType<IdentifierNameSyntax>(node);
        Assert.Equal("SpriteBatch", ((IdentifierNameSyntax)node).Identifier.Text);
    }

    [Fact]
    public void FindNameAtPosition_ReturnsGenericName_ForGenericIdentifier()
    {
        const string src = "class C { void M() { var x = new List<int>(); } }";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        int position = src.IndexOf("List<int>") + 2;

        var node = AddUsingSuggester.FindNameAtPosition(root, position);

        Assert.NotNull(node);
        var generic = Assert.IsType<GenericNameSyntax>(node);
        Assert.Equal("List", generic.Identifier.Text);
    }

    [Fact]
    public void FindNameAtPosition_ReturnsNull_WhenNotOnIdentifier()
    {
        const string src = "class C { void M() {     } }";
        var root = CSharpSyntaxTree.ParseText(src).GetRoot();
        int position = src.IndexOf("    ") + 2; // inside whitespace in the method body

        var node = AddUsingSuggester.FindNameAtPosition(root, position);

        // Whitespace isn't inside any name syntax; caller should treat as "no action".
        Assert.Null(node);
    }
}
