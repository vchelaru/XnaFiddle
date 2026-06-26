using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XnaFiddle;

namespace XnaFiddle.Tests;

/// <summary>
/// Round-trip coverage for the snippet codec's handling of a user-defined constructor (issue #83).
/// Before the fix, <see cref="SnippetReverter"/> dropped the constructor outright and
/// <see cref="SnippetExpander"/> always emitted its own scaffold constructor + a GDM field named
/// `graphics`, so a fiddle that sets up graphics in its constructor lost that setup when shared as
/// a snippet (its own GDM field was never assigned → NullReferenceException at first render).
/// </summary>
public class SnippetRoundTripTests
{
    // Mirrors the issue's AutoPong shape: a custom-named GDM field (_graphics), a constructor that
    // sizes the back buffer, and a helper that dereferences _graphics.GraphicsDevice.
    const string AutoPongCode = """
        using System;
        using Microsoft.Xna.Framework;
        using Microsoft.Xna.Framework.Graphics;
        using Microsoft.Xna.Framework.Input;

        public class AutoPongGame : Game
        {
            GraphicsDeviceManager _graphics;
            Vector2 _ballPosition;

            public AutoPongGame()
            {
                _graphics = new GraphicsDeviceManager(this);
                _graphics.PreferredBackBufferWidth = 800;
                _graphics.PreferredBackBufferHeight = 480;
                IsMouseVisible = true;
            }

            void ResetBall()
            {
                var viewport = _graphics.GraphicsDevice.Viewport;
                _ballPosition = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
            }

            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Color.Black);
                base.Draw(gameTime);
            }
        }
        """;

    // A scaffold-shaped fiddle with no user intent in the constructor (the exact constructor the
    // expander emits). Should revert to Constructor == null so re-sharing stays compact.
    const string ScaffoldConstructorCode = """
        using Microsoft.Xna.Framework;
        using Microsoft.Xna.Framework.Graphics;

        public class FiddleGame : Game
        {
            GraphicsDeviceManager graphics;

            public FiddleGame()
            {
                graphics = new GraphicsDeviceManager(this);
                if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
                    graphics.GraphicsProfile = GraphicsProfile.HiDef;
                IsMouseVisible = true;
                Window.AllowUserResizing = true;
            }

            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Color.CornflowerBlue);
                base.Draw(gameTime);
            }
        }
        """;

    // A fiddle with no constructor at all — only a Draw body. Also compact (Constructor == null).
    const string NoConstructorCode = """
        using Microsoft.Xna.Framework;
        using Microsoft.Xna.Framework.Graphics;

        public class FiddleGame : Game
        {
            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Color.CornflowerBlue);
                base.Draw(gameTime);
            }
        }
        """;

    // ── 1. Custom constructor is captured ─────────────────────────────────────

    [Fact]
    public void Revert_CapturesCustomConstructorBody()
    {
        SnippetRevertResult result = SnippetReverter.Revert(AutoPongCode);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Constructor);
        Assert.Contains("PreferredBackBufferWidth", result.Constructor);
        Assert.Contains("_graphics = new GraphicsDeviceManager", result.Constructor);
    }

    // ── 2. Round-trip is faithful and the scaffold steps aside ────────────────

    [Fact]
    public void RoundTrip_PreservesCustomConstructor_AndSuppressesScaffold()
    {
        SnippetRevertResult result = SnippetReverter.Revert(AutoPongCode);
        string expanded = SnippetExpander.Expand(ToModel(result));

        // (a) The user's constructor body survived verbatim.
        Assert.Contains("_graphics.PreferredBackBufferWidth = 800;", expanded);
        // (b) Exactly one GraphicsDeviceManager is constructed — the scaffold didn't add its own.
        Assert.Equal(1, CountOccurrences(expanded, "new GraphicsDeviceManager"));
        // (c) The scaffold's own field declaration is gone (the user owns the GDM field).
        Assert.DoesNotContain("GraphicsDeviceManager graphics;", expanded);
        // (d) The expanded source is syntactically valid C#.
        AssertNoSyntaxErrors(expanded);
    }

    // ── 3. Compactness preserved for scaffold-shaped code ─────────────────────

    [Fact]
    public void Revert_NoConstructor_YieldsNullConstructor()
    {
        SnippetRevertResult result = SnippetReverter.Revert(NoConstructorCode);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.Constructor);
    }

    [Fact]
    public void Revert_ScaffoldShapedConstructor_YieldsNullConstructor()
    {
        SnippetRevertResult result = SnippetReverter.Revert(ScaffoldConstructorCode);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.Constructor);
    }

    // ── 4. Existing behavior: null constructor still emits the scaffold ───────

    [Fact]
    public void Expand_NullConstructor_EmitsScaffold()
    {
        var model = new SnippetModel { Draw = "GraphicsDevice.Clear(Color.Red);" };

        string expanded = SnippetExpander.Expand(model);

        Assert.Contains("GraphicsDeviceManager graphics;", expanded);
        Assert.Contains("graphics = new GraphicsDeviceManager(this);", expanded);
        AssertNoSyntaxErrors(expanded);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Mirrors how Index.razor.cs builds the SnippetModel from a revert result (Constructor is
    // carried straight through, no UI toggle).
    static SnippetModel ToModel(SnippetRevertResult r) => new()
    {
        IsGum = r.IsGum,
        IsAposShapes = r.IsAposShapes,
        IsMonoGameExtended = r.IsMonoGameExtended,
        Usings = r.ExtraUsings.Count > 0 ? r.ExtraUsings : null,
        Members = r.Members,
        Constructor = r.Constructor,
        Initialize = r.Initialize,
        LoadContent = r.LoadContent,
        Update = r.Update,
        Draw = r.Draw,
    };

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    // Syntactic check only — no XNA assemblies are referenced, so we assert the parse tree has no
    // Error-severity diagnostics rather than attempting a semantic compile.
    static void AssertNoSyntaxErrors(string code)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }
}
