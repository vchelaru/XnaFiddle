# Post-deploy smoke check: fetches the live site's _framework/dotnet.js (the boot resource
# manifest -- see Verify-BlazorAssetIntegrity.ps1 for background) and confirms a sample of the
# hashed assets it references are actually reachable with matching content on the real URL.
#
# actions/deploy-pages reporting "success" only means GitHub's backend accepted the deployment,
# not that every edge node in front of GitHub Pages has picked it up yet -- this step exists to
# catch that propagation window (surfaced as SRI integrity failures / 503s for end users, see
# #132) rather than leaving it for the first visitor to discover.
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [int]$SampleCount = 5,
    [int]$MaxAttempts = 8,
    [int]$DelaySeconds = 15
)

$BaseUrl = $BaseUrl.TrimEnd("/")
$sha256 = [System.Security.Cryptography.SHA256]::Create()

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++)
{
    Write-Host "Attempt $attempt/$MaxAttempts against $BaseUrl ..."
    try
    {
        $dotnetJs = Invoke-WebRequest -Uri "$BaseUrl/_framework/dotnet.js" -UseBasicParsing -ErrorAction Stop
        $pattern = '"virtualPath"\s*:\s*"(?<vpath>[^"]+)"\s*,\s*"name"\s*:\s*"(?<name>[^"]+)"\s*,\s*"hash"\s*:\s*"sha256-(?<hash>[^"]+)"'
        $allEntries = [System.Text.RegularExpressions.Regex]::Matches($dotnetJs.Content, $pattern)
        if ($allEntries.Count -eq 0)
        {
            throw "No boot resource entries found in the live dotnet.js."
        }

        # Satellite resource assemblies (e.g. Microsoft.CodeAnalysis.resources.<hash>.wasm) live
        # under a per-culture subfolder (_framework/de/..., _framework/ja/...) that the manifest
        # entry doesn't spell out -- skip them here rather than guessing culture folder names.
        $entries = $allEntries | Where-Object { $_.Groups["name"].Value -notmatch "\.resources\." }

        # Sample evenly across the manifest rather than always the first N, so this isn't
        # blind to a mismatch that happens to sit outside the first few entries.
        $step = [Math]::Max(1, [Math]::Floor($entries.Count / $SampleCount))
        $sample = for ($i = 0; $i -lt $entries.Count -and $sample.Count -lt $SampleCount; $i += $step) { $entries[$i] }

        $mismatches = @()
        foreach ($entry in $sample)
        {
            $name = $entry.Groups["name"].Value
            $expectedHash = $entry.Groups["hash"].Value
            $response = Invoke-WebRequest -Uri "$BaseUrl/_framework/$name" -UseBasicParsing -ErrorAction Stop
            $actualHash = [Convert]::ToBase64String($sha256.ComputeHash($response.Content))
            if ($actualHash -ne $expectedHash)
            {
                $mismatches += "$name expected sha256-$expectedHash but got sha256-$actualHash"
            }
        }

        if ($mismatches.Count -eq 0)
        {
            Write-Host "Live deploy OK: $($sample.Count) sampled boot asset(s) match their manifest hash."
            exit 0
        }

        Write-Host "::warning::Live deploy mismatch(es) on attempt $attempt (likely CDN propagation lag, retrying):"
        $mismatches | ForEach-Object { Write-Host "  $_" }
    }
    catch
    {
        Write-Host "::warning::Attempt $attempt failed: $($_.Exception.Message)"
    }

    if ($attempt -lt $MaxAttempts)
    {
        Start-Sleep -Seconds $DelaySeconds
    }
}

Write-Host "::error::Live deploy still failing boot asset checks after $MaxAttempts attempts ($([Math]::Round($MaxAttempts * $DelaySeconds / 60, 1)) min) -- this is past normal CDN propagation lag."
exit 1
