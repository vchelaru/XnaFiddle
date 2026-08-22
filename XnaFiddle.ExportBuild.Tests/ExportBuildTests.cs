using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using XnaFiddle;

namespace XnaFiddle.ExportBuild.Tests;

// Exports a fiddle and runs a real `dotnet build` against the output. ProjectExporterTests (in
// XnaFiddle.Tests) only asserts on generated file *content* — it would not have caught a
// regression where the generated project fails to actually restore/compile (e.g. the SDK/KNI
// version bump in #132). This project closes that gap for a first pair of targets.
//
// Scope: KniDesktopGL and KniBlazorGL only. MonoGame/Android/FNA/WindowsDX are deliberately not
// covered yet — a follow-up, not an oversight. Both covered targets need no extra SDK workload:
// KniDesktopGL references nkast.Kni.Platform.SDL2.GL (a plain cross-platform NuGet package), and
// KniBlazorGL (without shaders) builds against plain net8.0 with the Blazor WebAssembly SDK, not
// net8.0-browser — see ProjectExporter.GenerateCsproj's `needsBrowserTarget` — so no
// wasm-tools workload is required either.
public class ExportBuildTests
{
    // Minimal game code that triggers no third-party library detection — mirrors
    // XnaFiddle.Tests/ProjectExporterTests.cs's MinimalCode.
    const string MinimalCode = @"
using Microsoft.Xna.Framework;
public class Game1 : Game
{
    protected override void Draw(GameTime gt) { }
}";

    // First-time NuGet restore of a fresh project can be slow; fail clearly on timeout instead
    // of hanging CI.
    const int BuildTimeoutMs = 5 * 60 * 1000;

    [Theory]
    [InlineData(ExportTarget.KniDesktopGL)]
    [InlineData(ExportTarget.KniBlazorGL)]
    public void ExportedProject_RealDotnetBuild_Succeeds(ExportTarget target)
    {
        byte[] zip = ProjectExporter.Export(MinimalCode, target, "MyGame");
        string extractDir = ExtractZip(zip);
        try
        {
            // A single-target export is a flat structure: {ProjectName}.slnx at the root plus
            // {ProjectName}/{ProjectName}.csproj (see ProjectExporterTests.SinglePlatform_ProducesFlatStructure).
            // Build via the .slnx, since that's what a real user opens/builds.
            string slnxPath = Path.Combine(extractDir, "MyGame.slnx");
            Assert.True(File.Exists(slnxPath), $"Expected {slnxPath} to exist after extracting the exported zip.");

            (int exitCode, string output) = RunDotnetBuild(slnxPath, extractDir);

            Assert.True(exitCode == 0,
                $"`dotnet build` of the exported {target} project failed with exit code {exitCode}.\n\n--- dotnet build output ---\n{output}");
        }
        finally
        {
            TryDeleteDirectory(extractDir);
        }
    }

    // Writes the zip's raw bytes to disk and extracts via ZipFile.ExtractToDirectory. Unlike
    // ProjectExporterTests's ExtractTextFiles (which decodes every entry as UTF8 text), this
    // preserves binary content byte-for-byte.
    static string ExtractZip(byte[] zip)
    {
        string extractDir = Path.Combine(Path.GetTempPath(), "XnaFiddleExportBuildTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        string zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(zipPath, zip);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
        }
        finally
        {
            File.Delete(zipPath);
        }
        return extractDir;
    }

    static (int ExitCode, string Output) RunDotnetBuild(string slnxPath, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{slnxPath}\" -c Release")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        // Read output asynchronously — reading RedirectStandardOutput/Error synchronously while
        // the child process blocks on a full pipe buffer is a classic deadlock.
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(BuildTimeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            output.AppendLine($"[TIMEOUT] `dotnet build` did not exit within {BuildTimeoutMs}ms.");
            return (-1, output.ToString());
        }

        return (process.ExitCode, output.ToString());
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup is best-effort (e.g. a file locked by an AV scan) and must never mask the
            // real assertion result above.
        }
    }
}
