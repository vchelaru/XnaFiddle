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
        /// Assembly names used to read the version number shown in the diagnostics
        /// banner. Empty means this plugin contributes no banner entry. The display
        /// name comes from <see cref="Name"/>.
        /// </summary>
        string[] VersionAssemblies { get; }

        /// <summary>
        /// Reset static state left behind by this library between game runs.
        /// Must be idempotent — safe to call when the library hasn't been initialized,
        /// and safe to call multiple times.
        /// </summary>
        void CleanUp();
    }
}
