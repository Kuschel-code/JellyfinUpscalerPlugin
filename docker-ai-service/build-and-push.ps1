<#
.SYNOPSIS
  Builds and pushes the 6 AI Upscaler Service variants to Docker Hub under
  the `docker7*` tag family, with multi-arch support for slim-python images
  and semver triple-tagging.

.DESCRIPTION
  Mirrors the GitHub Actions `docker-publish.yml` workflow but runs locally.
  Produces three tags per variant:
    1. Rolling tag:  <family>[-suffix]         (e.g. docker7-amd)
    2. Pinned tag:   <family>-v<ver>[-suffix]  (e.g. docker7-v1.6.1.13-amd)
    3. Semver tag:   v<ver>[-suffix]           (e.g. v1.6.1.13-amd)

  Uses `docker buildx` so NVIDIA/AMD/Intel stay linux/amd64-only (their base
  images aren't multi-arch) while CPU/Apple/Vulkan build for amd64+arm64.

  Requires:
    - Docker Desktop (or `docker` CLI) running with buildx enabled
    - `docker login` already completed for an account with push rights on
      docker.io/kuscheltier/jellyfin-ai-upscaler

.PARAMETER Variants
  Which variants to build. Defaults to all 6. Values: nvidia, amd, intel,
  apple, vulkan, cpu.

.PARAMETER Version
  Semver string to embed as APP_VERSION and use in pinned/semver tags.
  Defaults to the plugin's current csproj AssemblyVersion.

.PARAMETER NoPush
  Build only; skip registry push. Useful for dry-runs.

.PARAMETER Family
  Tag family prefix. Defaults to "docker7" — change only when bumping the base.

.EXAMPLE
  pwsh ./build-and-push.ps1

.EXAMPLE
  pwsh ./build-and-push.ps1 -Variants cpu,nvidia -NoPush

.EXAMPLE
  pwsh ./build-and-push.ps1 -Version 1.6.1.13
#>
param(
    [ValidateSet('nvidia','amd','intel','apple','vulkan','cpu')]
    [string[]] $Variants = @('nvidia','amd','intel','apple','vulkan','cpu'),
    [string]   $Version,
    [string]   $Family   = 'docker7',
    [switch]   $NoPush
)

$ErrorActionPreference = 'Stop'
$repo = 'kuscheltier/jellyfin-ai-upscaler'

# --- Resolve version from csproj if not supplied ----------------------------
if (-not $Version) {
    $csprojPath = Join-Path $PSScriptRoot '..' 'JellyfinUpscalerPlugin.csproj'
    if (-not (Test-Path $csprojPath)) {
        throw "Could not find JellyfinUpscalerPlugin.csproj to infer version. Pass -Version explicitly."
    }
    $csprojContent = Get-Content $csprojPath -Raw
    if ($csprojContent -match '<AssemblyVersion>([^<]+)</AssemblyVersion>') {
        $Version = $Matches[1]
    } else {
        throw 'Could not extract <AssemblyVersion> from csproj. Pass -Version explicitly.'
    }
}

$commit = 'unknown'
try {
    $commit = (git -C (Split-Path $PSScriptRoot -Parent) rev-parse --short HEAD).Trim()
} catch {
    Write-Warning "git rev-parse failed; APP_COMMIT will be 'unknown'"
}

Write-Host ""
Write-Host ("Version: $Version  Commit: $commit  Family: $Family") -ForegroundColor Cyan
Write-Host ""

# --- Variant table ----------------------------------------------------------
# platforms: NVIDIA/AMD/Intel base images are amd64-only; python:slim is multi-arch.
$map = @{
    'nvidia' = @{ file = 'Dockerfile';         suffix = '';         platforms = 'linux/amd64' }
    'amd'    = @{ file = 'Dockerfile.amd';     suffix = '-amd';     platforms = 'linux/amd64' }
    'intel'  = @{ file = 'Dockerfile.intel';   suffix = '-intel';   platforms = 'linux/amd64' }
    'apple'  = @{ file = 'Dockerfile.apple';   suffix = '-apple';   platforms = 'linux/amd64,linux/arm64' }
    'vulkan' = @{ file = 'Dockerfile.vulkan';  suffix = '-vulkan';  platforms = 'linux/amd64,linux/arm64' }
    'cpu'    = @{ file = 'Dockerfile.cpu';     suffix = '-cpu';     platforms = 'linux/amd64,linux/arm64' }
}

# --- Ensure a multi-arch capable builder exists -----------------------------
$builderName = 'jellyfin-upscaler-builder'
$existing = docker buildx ls 2>&1 | Out-String
if ($existing -notmatch [regex]::Escape($builderName)) {
    Write-Host "Creating buildx builder '$builderName'..." -ForegroundColor DarkGray
    docker buildx create --name $builderName --driver docker-container --use | Out-Null
} else {
    docker buildx use $builderName | Out-Null
}
docker buildx inspect --bootstrap | Out-Null

# --- Build & push each variant ---------------------------------------------
$failed = @()
foreach ($v in $Variants) {
    $entry   = $map[$v]
    $rolling = "${repo}:${Family}$($entry.suffix)"
    $pinned  = "${repo}:${Family}-v${Version}$($entry.suffix)"
    $semver  = "${repo}:v${Version}$($entry.suffix)"

    Write-Host ""
    Write-Host ("=== Building $v ($($entry.platforms)) ===") -ForegroundColor Cyan
    Write-Host ("  tags: $rolling, $pinned, $semver") -ForegroundColor DarkGray

    $buildArgs = @(
        'buildx','build',
        '-f', (Join-Path $PSScriptRoot $entry.file),
        '--platform', $entry.platforms,
        '-t', $rolling,
        '-t', $pinned,
        '-t', $semver,
        '--build-arg', "APP_VERSION=$Version",
        '--build-arg', "APP_COMMIT=$commit",
        '--provenance=false'
    )

    if ($NoPush) {
        # buildx can only --load single-platform images; for multi-arch, we skip.
        if ($entry.platforms -notmatch ',') {
            $buildArgs += '--load'
        } else {
            Write-Host "  (multi-arch; --load not supported, building to cache only)" -ForegroundColor DarkGray
        }
    } else {
        $buildArgs += '--push'
    }

    $buildArgs += $PSScriptRoot

    & docker @buildArgs
    if ($LASTEXITCODE -ne 0) { $failed += $v; continue }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host (">>> FAILED variants: " + ($failed -join ', ')) -ForegroundColor Red
    exit 1
}
if ($NoPush) {
    Write-Host ">>> All variants built successfully (push skipped)" -ForegroundColor Green
} else {
    Write-Host ">>> All variants built + pushed successfully" -ForegroundColor Green
    Write-Host ("     Rolling family: ${repo}:${Family}*") -ForegroundColor Green
    Write-Host ("     Pinned version: ${repo}:${Family}-v${Version}*") -ForegroundColor Green
}
