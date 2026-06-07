using System.Collections.Generic;

namespace XnaFiddle
{
    /// <summary>
    /// Optional interface for <see cref="ILibraryPlugin"/> implementations that need
    /// NuGet packages added to exported projects. Plugins that implement this interface
    /// declare how to detect their usage in source code and what packages to emit for
    /// each <see cref="ExportTarget"/> (KNI, MonoGame, or future FNA).
    /// </summary>
    public interface IExportableLibrary
    {
        /// <summary>
        /// Returns true if the user's source code uses this library.
        /// Typically implemented via <c>source.Contains("SomeNamespace")</c>.
        /// </summary>
        bool IsUsedInSource(string source);

        /// <summary>
        /// Returns the NuGet packages to add when exporting for the given target.
        /// Called only when <see cref="IsUsedInSource"/> returns true.
        /// The source code is provided for libraries that need sub-package detection
        /// (e.g., MLEM conditionally adds MLEM.Ui and MLEM.Extended packages).
        /// </summary>
        List<ExportPackage> GetExportPackages(ExportTarget target, string source);
    }

    public struct ExportPackage
    {
        public string Id;
        public string Version;
    }

    public static class ExportTargetExtensions
    {
        public static bool IsKni(this ExportTarget target) => target switch
        {
            ExportTarget.KniDesktopGL => true,
            ExportTarget.KniWindowsDX => true,
            ExportTarget.KniAndroid => true,
            ExportTarget.KniBlazorGL => true,
            _ => false,
        };
    }
}
