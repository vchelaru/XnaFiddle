namespace XnaFiddle
{
    /// <summary>
    /// Represents a third-party library integration in XnaFiddle.
    /// Each library that needs host-level support (cleanup, assembly registration, etc.)
    /// implements this interface and registers with the <see cref="LibraryRegistry"/>.
    /// </summary>
    public interface ILibraryPlugin
    {
        /// <summary>Display name (e.g., "Gum", "MLEM").</summary>
        string Name { get; }

        /// <summary>
        /// Assembly names to force-load and add to Roslyn compilation references.
        /// These are the KNI-side assembly names (e.g., "KniGum", "GumCommon").
        /// </summary>
        string[] RequiredAssemblies { get; }

        /// <summary>
        /// Label and assembly names for the version banner in the diagnostics panel.
        /// </summary>
        (string Label, string[] AssemblyNames) VersionInfo { get; }

        /// <summary>
        /// Reset static state left behind by this library between game runs.
        /// Must be idempotent — safe to call when the library hasn't been initialized,
        /// and safe to call multiple times.
        /// </summary>
        void CleanUp();
    }
}
