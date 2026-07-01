namespace XnaFiddle.E2E.Tests;

/// <summary>
/// Locates the published Blazor WASM app (its <c>wwwroot</c>) that the smoke test serves.
/// CI sets <c>E2E_PUBLISH_DIR</c> to the <c>dotnet publish -o</c> output; locally we fall back
/// to the repo's conventional publish location so the test is runnable without env setup.
/// </summary>
public static class PublishOutput
{
    private const string PublishDirEnvVar = "E2E_PUBLISH_DIR";
    private const string DefaultRelativePublishDir = "artifacts/e2e-publish";

    /// <summary>Absolute path to the served web root (the folder containing index.html).</summary>
    public static string ResolveWebRoot()
    {
        string publishDir = Environment.GetEnvironmentVariable(PublishDirEnvVar)
            ?? Path.Combine(FindRepoRoot(), DefaultRelativePublishDir);

        // Accept either the publish output root (which contains wwwroot) or the wwwroot itself,
        // so callers can point the env var at whichever they have on hand.
        string webRoot = Directory.Exists(Path.Combine(publishDir, "wwwroot"))
            ? Path.Combine(publishDir, "wwwroot")
            : publishDir;

        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            throw new DirectoryNotFoundException(
                $"No published app found at '{webRoot}'. Publish first "
                + "(dotnet publish XnaFiddle.BlazorGL -c Release -o <dir>) and/or set "
                + $"{PublishDirEnvVar}.");
        }

        return webRoot;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "XnaFiddle.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repo root (XnaFiddle.sln).");
    }
}
