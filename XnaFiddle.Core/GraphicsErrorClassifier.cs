using System;

namespace XnaFiddle
{
    /// <summary>
    /// Recognizes the WebGL graphics-device creation failure from issue #12 so the UI can show
    /// an actionable message instead of a raw "Object reference not set to an instance of an
    /// object." stack trace.
    ///
    /// When the browser can't hand back a WebGL context (WebGL disabled or GPU-blocklisted,
    /// hardware acceleration off, or too many live contexts), KNI's canvas.getContext(...)
    /// returns null on the JS side. KNI then wraps that null handle in a JSObject and throws a
    /// bare NullReferenceException deep in the device-creation path — uninformative on its own.
    /// </summary>
    public static class GraphicsErrorClassifier
    {
        // Frames present in the issue-12 stack trace. Matching the device-creation path (rather
        // than just any "WebGL" frame) keeps us from hijacking unrelated NullReferenceExceptions.
        static readonly string[] DeviceCreationFrames =
        [
            "CreateGraphicsContextStrategy",
            "GraphicsDeviceStrategy",
            "WebGLRenderingContext",
        ];

        /// <summary>
        /// True when <paramref name="ex"/> (or any inner exception) is the opaque
        /// NullReferenceException thrown while creating the WebGL graphics device. The
        /// NullReferenceException gate is deliberate: if KNI ever fails device creation with a
        /// *descriptive* exception, we want to show that message rather than override it here.
        /// </summary>
        public static bool IsGraphicsDeviceCreationFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is NullReferenceException && StackTraceIndicatesDeviceCreation(current.StackTrace))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when a stack-trace string runs through KNI's graphics-device creation path.
        /// Pure and exposed so the frame markers can be unit-tested against the real issue-12
        /// trace without reproducing a live WebGL failure (stack traces aren't settable).
        /// </summary>
        public static bool StackTraceIndicatesDeviceCreation(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return false;
            foreach (string frame in DeviceCreationFrames)
            {
                if (stackTrace.Contains(frame)) return true;
            }
            return false;
        }

        /// <summary>
        /// Plain-language, actionable text shown in the diagnostics panel for the failure above.
        /// </summary>
        public static string DeviceCreationFailureMessage =>
            "Couldn't create a graphics (WebGL) device.\n\n" +
            "WebGL appears to be unavailable in this browser. Things to try:\n" +
            "  • Reload the page — after many runs the browser can run out of graphics contexts.\n" +
            "  • Enable hardware acceleration in your browser settings.\n" +
            "  • Make sure WebGL is enabled and not blocklisted — test it at https://get.webgl.org.\n" +
            "  • Firefox: check that webgl.disabled is false in about:config.";
    }
}
