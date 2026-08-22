# Verifies that every boot resource .NET 10 Blazor WASM embeds into _framework/dotnet.js
# (the replacement for the old blazor.boot.json manifest) actually exists on disk under
# _framework/ with bytes matching the recorded SHA-256. This catches a publish-pipeline
# regression (missing file, wrong compressed variant substituted for the plain one,
# non-deterministic asset fingerprinting) before it reaches GitHub Pages. It does NOT
# catch CDN edge-propagation lag after a real deploy -- that has to be checked against
# the live URL post-deploy (see the "Smoke-check live deploy" step in deploy.yml).
param(
    [Parameter(Mandatory = $true)]
    [string]$WwwrootPath
)

$dotnetJsPath = Join-Path $WwwrootPath "_framework/dotnet.js"
if (-not (Test-Path $dotnetJsPath))
{
    Write-Error "dotnet.js not found at $dotnetJsPath"
    exit 1
}

$content = Get-Content -Raw -LiteralPath $dotnetJsPath
$pattern = '"virtualPath"\s*:\s*"(?<vpath>[^"]+)"\s*,\s*"name"\s*:\s*"(?<name>[^"]+)"\s*,\s*"hash"\s*:\s*"sha256-(?<hash>[^"]+)"'
$entries = [System.Text.RegularExpressions.Regex]::Matches($content, $pattern)

if ($entries.Count -eq 0)
{
    # If Blazor's boot resource format changes again, this check needs updating too --
    # fail loudly instead of silently checking nothing.
    Write-Error "No boot resource entries found in dotnet.js -- the manifest format may have changed."
    exit 1
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$failures = [System.Collections.Generic.List[string]]::new()

$frameworkDir = Join-Path $WwwrootPath "_framework"

foreach ($entry in $entries)
{
    $name = $entry.Groups["name"].Value
    $expectedHash = $entry.Groups["hash"].Value
    $filePath = Join-Path $frameworkDir $name

    if (-not (Test-Path -LiteralPath $filePath))
    {
        # Satellite resource assemblies (e.g. Microsoft.CodeAnalysis.resources.<hash>.wasm) are
        # nested under a per-culture subfolder (_framework/de/..., _framework/ja/...) rather than
        # directly under _framework/, and the manifest entry doesn't spell out which one. Fall
        # back to a recursive search instead of hardcoding the known culture folder names.
        $found = Get-ChildItem -LiteralPath $frameworkDir -Recurse -Filter $name -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $found)
        {
            $failures.Add("MISSING: $name (referenced as $($entry.Groups['vpath'].Value)) not found under $frameworkDir")
            continue
        }
        $filePath = $found.FullName
    }

    $bytes = [System.IO.File]::ReadAllBytes($filePath)
    $actualHash = [Convert]::ToBase64String($sha256.ComputeHash($bytes))
    if ($actualHash -ne $expectedHash)
    {
        $failures.Add("MISMATCH: _framework/$name expected sha256-$expectedHash but got sha256-$actualHash")
    }
}

Write-Host "Checked $($entries.Count) boot resource(s) under $WwwrootPath."

if ($failures.Count -gt 0)
{
    Write-Host "::error::$($failures.Count) boot asset integrity failure(s):"
    foreach ($failure in $failures)
    {
        Write-Host "  $failure"
    }
    exit 1
}

Write-Host "All boot resource integrity hashes match published bytes."
