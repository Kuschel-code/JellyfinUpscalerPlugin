<#
.SYNOPSIS
  Validates a published Jellyfin-Upscaler-Plugin GitHub release end-to-end.

.DESCRIPTION
  Runs AFTER `gh release create`. It downloads every ZIP asset from the named
  release, verifies each is a well-formed plugin bundle (no Moq/Mono.Cecil/
  InstrumentationEngine/CodeCoverage/Scripts leak-through from a `dotnet test`
  output), and confirms the SHA-256 matches the live manifest checksum.

  Exits 1 on ANY mismatch. Designed so the release is only marked "done" when
  this script passes — this is the guard that would have caught the v1.6.1.11
  wrong-ZIP-upload where a 13.9 MB test-binary got hosted instead of the 1.7 MB
  plugin bundle.

.PARAMETER Tag
  Release tag to verify. E.g. 'v1.6.1.12'.

.PARAMETER Repo
  GitHub repo in owner/name form. Defaults to Kuschel-code/JellyfinUpscalerPlugin.

.PARAMETER ManifestUrl
  Raw URL of the plugin manifest (live-served). Must expose the same checksum
  that Jellyfin's plugin installer validates against.

.EXAMPLE
  pwsh ./Scripts/verify-release.ps1 -Tag v1.6.1.12
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [string]$Repo = "Kuschel-code/JellyfinUpscalerPlugin",

    [string]$ManifestUrl = "https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json"
)

$ErrorActionPreference = "Stop"

# Files a correct plugin ZIP MUST contain. Order-insensitive.
$ExpectedFiles = @(
    "CliWrap.dll",
    "FFMpegCore.dll",
    "Instances.dll",
    "JellyfinUpscalerPlugin.dll",
    "meta.json",
    "SixLabors.ImageSharp.dll"
)

# Anything matching these patterns means a test-binary output leaked into the
# plugin ZIP. Triggered the v1.6.1.11 SHA-mismatch bug.
$ForbiddenPatterns = @(
    "Moq\.",
    "Mono\.Cecil",
    "InstrumentationEngine",
    "CodeCoverage",
    "^Scripts/",
    "^runtimes/",
    "\.pdb$",
    "\.deps\.json$"
)

$fail = $false
$tmp = Join-Path $env:TEMP ("verify-release-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    Write-Host "=== Fetching live manifest ===" -ForegroundColor Cyan
    $manifest = Invoke-RestMethod -Uri $ManifestUrl
    $manifestEntry = $manifest[0].versions | Where-Object {
        $_.sourceUrl -like "*$Tag*"
    } | Select-Object -First 1
    if (-not $manifestEntry) {
        Write-Host "FAIL: no manifest entry references tag $Tag" -ForegroundColor Red
        exit 1
    }
    $manifestChecksum = $manifestEntry.checksum.ToLower()
    Write-Host ("  manifest checksum: " + $manifestChecksum)
    Write-Host ("  targetAbi:         " + $manifestEntry.targetAbi)

    Write-Host ""
    Write-Host "=== Downloading release assets for $Tag ===" -ForegroundColor Cyan
    gh release download $Tag --dir $tmp --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: gh release download returned $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }

    $zips = Get-ChildItem -Path $tmp -Filter "*.zip"
    if ($zips.Count -eq 0) {
        Write-Host "FAIL: no ZIP assets in release" -ForegroundColor Red
        exit 1
    }

    foreach ($zip in $zips) {
        Write-Host ""
        Write-Host ("=== Verifying " + $zip.Name + " ===") -ForegroundColor Cyan

        $sha = (Get-FileHash -Algorithm SHA256 -Path $zip.FullName).Hash.ToLower()
        Write-Host ("  size: " + $zip.Length + " bytes")
        Write-Host ("  sha256: " + $sha)

        if ($sha -ne $manifestChecksum) {
            Write-Host ("  [FAIL] SHA does not match manifest checksum") -ForegroundColor Red
            $fail = $true
        } else {
            Write-Host "  [OK] SHA matches manifest"
        }

        # Inspect contents
        $unzipDir = Join-Path $tmp ($zip.BaseName + "-unzipped")
        New-Item -ItemType Directory -Force -Path $unzipDir | Out-Null
        Expand-Archive -Path $zip.FullName -DestinationPath $unzipDir -Force

        $entries = Get-ChildItem -Path $unzipDir -Recurse -File | ForEach-Object {
            $_.FullName.Substring($unzipDir.Length + 1).Replace("\", "/")
        }

        # Forbidden artifact check
        foreach ($pat in $ForbiddenPatterns) {
            $hits = $entries | Where-Object { $_ -match $pat }
            if ($hits) {
                Write-Host ("  [FAIL] forbidden pattern '" + $pat + "' matched:") -ForegroundColor Red
                $hits | ForEach-Object { Write-Host ("      " + $_) -ForegroundColor Red }
                $fail = $true
            }
        }

        # Required files check
        foreach ($req in $ExpectedFiles) {
            if ($entries -notcontains $req) {
                Write-Host ("  [FAIL] missing required file: " + $req) -ForegroundColor Red
                $fail = $true
            }
        }

        # meta.json version inside the ZIP must match tag
        $metaPath = Join-Path $unzipDir "meta.json"
        if (Test-Path $metaPath) {
            $meta = Get-Content $metaPath -Raw | ConvertFrom-Json
            $tagVersion = $Tag.TrimStart("v")
            if ($meta.version -ne $tagVersion) {
                Write-Host ("  [FAIL] meta.json version '" + $meta.version + "' does not match tag '" + $tagVersion + "'") -ForegroundColor Red
                $fail = $true
            } else {
                Write-Host ("  [OK] meta.json version = " + $meta.version)
            }
        }
    }

    Write-Host ""
    if ($fail) {
        Write-Host ">>> RELEASE VERIFICATION FAILED <<<" -ForegroundColor Red
        exit 1
    } else {
        Write-Host ">>> RELEASE VERIFICATION PASSED <<<" -ForegroundColor Green
        exit 0
    }
} finally {
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
}
