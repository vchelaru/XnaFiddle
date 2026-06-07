namespace XnaFiddle
{
    /// <summary>
    /// A single editor diagnostic (squiggle): line/column span, message, and severity.
    /// Produced by <see cref="SecurityChecker"/> (XnaFiddle.Core) and by the Roslyn compile in
    /// CompilationService (XnaFiddle.BlazorGL), and consumed by the Monaco marker interop.
    /// Top-level (was nested in CompilationService) so Core can produce it without depending on
    /// the Blazor app. See issue #26.
    /// </summary>
    public class DiagnosticInfo
    {
        public int StartLine { get; set; }
        public int StartCol { get; set; }
        public int EndLine { get; set; }
        public int EndCol { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; } // "error", "warning", "info"
    }
}
