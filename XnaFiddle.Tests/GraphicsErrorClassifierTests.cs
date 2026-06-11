using XnaFiddle;

namespace XnaFiddle.Tests;

public class GraphicsErrorClassifierTests
{
    // The verbatim inner-exception stack trace reported in issue #12 (Firefox, WebGL context
    // creation returning null). Pinning the classifier against this guards the frame markers
    // against drift if KNI's call path is renamed.
    const string Issue12StackTrace = """
           at nkast.Wasm.JSInterop.JSObject..ctor(Int32 uid)
           at nkast.Wasm.Canvas.RenderingContext..ctor(Canvas canvas, Int32 uid)
           at nkast.Wasm.Canvas.WebGL.WebGLRenderingContext..ctor(Canvas canvas, Int32 uid)
           at nkast.Wasm.Canvas.Canvas.GetContext[IWebGLRenderingContext](ContextAttributes attributes)
           at Microsoft.Xna.Platform.Graphics.ConcreteGraphicsDevice.CreateGraphicsContextStrategy(GraphicsContext context)
           at Microsoft.Xna.Framework.Graphics.GraphicsContext..ctor(GraphicsDeviceStrategy deviceStrategy)
           at Microsoft.Xna.Platform.Graphics.GraphicsDeviceStrategy.CreateGraphicsContext()
           at Microsoft.Xna.Platform.Graphics.GraphicsDeviceStrategy.Initialize()
        """;

    // An NullReferenceException whose StackTrace we can dictate — the runtime only populates
    // StackTrace on a thrown exception, so faking it is the only way to unit-test the walker.
    sealed class FakeException : Exception
    {
        readonly string _stack;
        public FakeException(string stack, Exception? inner = null) : base("fake", inner) => _stack = stack;
        public override string StackTrace => _stack;
    }

    sealed class FakeNullReferenceException : NullReferenceException
    {
        readonly string _stack;
        public FakeNullReferenceException(string stack) => _stack = stack;
        public override string StackTrace => _stack;
    }

    // ── StackTraceIndicatesDeviceCreation (pure string matcher) ───────────────

    [Fact]
    public void StackTrace_MatchesRealIssue12Trace()
    {
        Assert.True(GraphicsErrorClassifier.StackTraceIndicatesDeviceCreation(Issue12StackTrace));
    }

    [Fact]
    public void StackTrace_DoesNotMatchUnrelatedTrace()
    {
        const string unrelated = """
               at MyGame.Update(GameTime gameTime)
               at Microsoft.Xna.Framework.Game.Tick()
            """;
        Assert.False(GraphicsErrorClassifier.StackTraceIndicatesDeviceCreation(unrelated));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StackTrace_HandlesNullOrEmpty(string? stack)
    {
        Assert.False(GraphicsErrorClassifier.StackTraceIndicatesDeviceCreation(stack));
    }

    // ── IsGraphicsDeviceCreationFailure (exception walker) ────────────────────

    [Fact]
    public void Failure_DetectedOnTopLevelNullReference()
    {
        Exception ex = new FakeNullReferenceException(Issue12StackTrace);
        Assert.True(GraphicsErrorClassifier.IsGraphicsDeviceCreationFailure(ex));
    }

    [Fact]
    public void Failure_DetectedThroughInnerException()
    {
        // Mirrors the real shape: a wrapping exception around the WebGL NullReferenceException.
        Exception ex = new FakeException("at Some.Outer.Frame()", new FakeNullReferenceException(Issue12StackTrace));
        Assert.True(GraphicsErrorClassifier.IsGraphicsDeviceCreationFailure(ex));
    }

    [Fact]
    public void Failure_NotReportedForNonNullReferenceWithDeviceTrace()
    {
        // Same device-creation frames, but a descriptive (non-NRE) exception — we must NOT
        // override its message with the generic one.
        Exception ex = new FakeException(Issue12StackTrace);
        Assert.False(GraphicsErrorClassifier.IsGraphicsDeviceCreationFailure(ex));
    }

    [Fact]
    public void Failure_NotReportedForUnrelatedNullReference()
    {
        Exception ex = new FakeNullReferenceException("at MyGame.LoadContent()");
        Assert.False(GraphicsErrorClassifier.IsGraphicsDeviceCreationFailure(ex));
    }
}
