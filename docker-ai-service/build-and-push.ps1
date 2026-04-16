<#
.SYNOPSIS
  Builds and pushes all 6 AI Upscaler Service variants to Docker Hub.

.DESCRIPTION
  Rebuilds every Dockerfile against the current app/main.py (which carries
  VERSION = "1.6.1.13" and the v1.6.1.12 catalog cleanup) and pushes under
  the `docker6.1*` tag family that the README + docker-compose.yml point to.

  Run from the docker-ai-service/ directory. Requires:
    - Docker Desktop (or `docker` CLI) running
    - `docker login` already completed with an account that can push to
      docker.io/kuscheltier/jellyfin-ai-upscaler

.PARAMETER Variants
  Which variants to build. Defaults to all 6. Values: nvidia, amd, intel,
  apple, vulkan, cpu.

.PARAMETER NoPush
  Build only; skip `docker push`.

.EXAMPLE
  pwsh ./build-and-push.ps1

.EXAMPLE
  pwsh ./build-and-push.ps1 -Variants cpu,nvidia -NoPush
#>
param(
    [ValidateSet('nvidia','amd','intel','apple','vulkan','cpu')]
    [string[]] $Variants = @('nvidia','amd','intel','apple','vulkan','cpu'),
    [switch] $NoPush
)

$ErrorActionPreference = 'Stop'
$repo = 'kuscheltier/jellyfin-ai-upscaler'

$map = @{
    'nvidia' = @{ file = 'Dockerfile';         tag = 'docker6.1' }
    'amd'    = @{ file = 'Dockerfile.amd';     tag = 'docker6.1-amd' }
    'intel'  = @{ file = 'Dockerfile.intel';   tag = 'docker6.1-intel' }
    'apple'  = @{ file = 'Dockerfile.apple';   tag = 'docker6.1-apple' }
    'vulkan' = @{ file = 'Dockerfile.vulkan';  tag = 'docker6.1-vulkan' }
    'cpu'    = @{ file = 'Dockerfile.cpu';     tag = 'docker6.1-cpu' }
}

$failed = @()
foreach ($v in $Variants) {
    $entry = $map[$v]
    $full  = "${repo}:$($entry.tag)"
    Write-Host ""
    Write-Host ("=== Building $v -> $full ===") -ForegroundColor Cyan

    docker build -f $entry.file -t $full .
    if ($LASTEXITCODE -ne 0) { $failed += "$v (build)"; continue }

    if (-not $NoPush) {
        Write-Host ("  pushing " + $full) -ForegroundColor DarkGray
        docker push $full
        if ($LASTEXITCODE -ne 0) { $failed += "$v (push)" }
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host (">>> FAILED variants: " + ($failed -join ', ')) -ForegroundColor Red
    exit 1
}
Write-Host ">>> All variants built + pushed successfully" -ForegroundColor Green
