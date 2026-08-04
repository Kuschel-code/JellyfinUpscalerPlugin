#!/usr/bin/env pwsh
# cleanup-dockerhub-tags.ps1
# Delete obsolete Docker Hub tags for kuscheltier/jellyfin-ai-upscaler and
# re-point `latest` to the current nvidia image.
#
# SECURITY: credentials are read from environment variables — NEVER hardcode a
# token here or paste one into chat. Create a scoped Docker Hub access token at
# hub.docker.com > Account Settings > Security, then:
#   $env:DOCKERHUB_USER  = "kuscheltier"
#   $env:DOCKERHUB_TOKEN = "<docker hub access token>"
#
# Usage:
#   pwsh Scripts/cleanup-dockerhub-tags.ps1            # DRY RUN — only lists what would change
#   pwsh Scripts/cleanup-dockerhub-tags.ps1 -Execute   # actually re-point latest + delete
#
# Keep rule (everything else is deleted):
#   - rolling tags : docker7, docker7-{amd,intel,cpu,apple,vulkan}
#   - current pin  : v1.7.8* / docker7-v1.7.8*
#   - rollback pin : v1.7.7* / docker7-v1.7.7*
#   - build cache  : buildcache-*
#   - latest       : kept, re-pointed to $CurrentNvidiaTag (not deleted)

param([switch]$Execute)

$ErrorActionPreference = 'Stop'
$Repo = 'kuscheltier/jellyfin-ai-upscaler'

# v1.8.3.22 - the version is DERIVED here, never typed.
#
# It was hardcoded to 'v1.7.8'. By the time anyone ran -Execute the repo was on
# v1.8.3.21, so the keep-list matched nothing current: the run would have deleted
# every v1.8.x tag, dropped the docker7-converter rolling tag (added in v1.8.3.8 and
# never added here), and re-pointed :latest - which Watchtower users follow - back to
# a v1.7.8 image. A destructive script must not carry a version that can age.
$MetaPath = Join-Path $PSScriptRoot '..' 'meta.json'
if (-not (Test-Path $MetaPath)) { Write-Error "meta.json not found at $MetaPath"; exit 1 }
$CurrentVersion = (Get-Content $MetaPath -Raw | ConvertFrom-Json).version
if (-not $CurrentVersion) { Write-Error 'Could not read version from meta.json'; exit 1 }
$CurrentNvidiaTag = "v$CurrentVersion"   # latest is re-pointed to this

# Release families to keep besides the current one. A rollback needs at least one
# predecessor to still exist on the registry.
$KeepPreviousReleases = 3

if (-not $env:DOCKERHUB_USER -or -not $env:DOCKERHUB_TOKEN) {
    Write-Error 'Set $env:DOCKERHUB_USER and $env:DOCKERHUB_TOKEN first (use a Docker Hub access token, not your password).'
    exit 1
}

# Every rolling tag docker-publish.yml pushes, matched by SHAPE rather than by an
# enumerated list. The old list predated docker7-converter and would have deleted it.
function Test-IsRollingTag([string]$n) { return $n -match '^docker7(-[a-z0-9]+)?$' }

# Version families to keep. Filled from the registry below, never hardcoded.
$script:KeepVersions = @()

function Test-Keep([string]$n) {
    if ($n -eq 'latest') { return $true }
    if ($n -like 'buildcache-*') { return $true }
    if (Test-IsRollingTag $n) { return $true }
    foreach ($v in $script:KeepVersions) {
        if ($n -match ('^(docker7-)?v' + [regex]::Escape($v) + '(-|$)')) { return $true }
    }
    return $false
}

# 1. JWT login (token-based; never your account password)
$login = Invoke-RestMethod -Method Post -Uri 'https://hub.docker.com/v2/users/login' `
    -ContentType 'application/json' `
    -Body (@{ username = $env:DOCKERHUB_USER; password = $env:DOCKERHUB_TOKEN } | ConvertTo-Json)
$headers = @{ Authorization = "JWT $($login.token)" }

# 2. Enumerate all tags (paginated)
$tags = @()
$url = "https://hub.docker.com/v2/repositories/$Repo/tags?page_size=100"
while ($url) {
    $page = Invoke-RestMethod -Uri $url -Headers $headers
    $tags += $page.results
    $url = $page.next
}

# Build the keep-list from what is actually ON the registry, so a release that was
# never published cannot silently shrink the retention window.
$versionsOnRegistry = @(
    $tags.name |
    ForEach-Object { if ($_ -match '^(?:docker7-)?v(\d+(?:\.\d+)+)') { $Matches[1] } } |
    Sort-Object -Unique -Descending { [version]$_ }
)
$script:KeepVersions = @($CurrentVersion) +
    @($versionsOnRegistry | Where-Object { $_ -ne $CurrentVersion } | Select-Object -First $KeepPreviousReleases)
$script:KeepVersions = @($script:KeepVersions | Select-Object -Unique)

Write-Host "Current version (meta.json) : $CurrentVersion"
Write-Host "Keeping version families    : $($script:KeepVersions -join ', ')"

# Guardrail: the tag :latest gets re-pointed to must exist, or -Execute would leave
# every Watchtower user following a dangling reference.
if ($tags.name -notcontains $CurrentNvidiaTag) {
    Write-Error "Refusing to run: $CurrentNvidiaTag is not on the registry, so :latest cannot be re-pointed to it."
    exit 1
}

$toDelete = @($tags | Where-Object { -not (Test-Keep $_.name) } | Select-Object -ExpandProperty name | Sort-Object)
$kept     = @($tags | Where-Object {      (Test-Keep $_.name) } | Select-Object -ExpandProperty name | Sort-Object)

Write-Host "Repo: $Repo"
Write-Host "KEEP   ($($kept.Count)): $($kept -join ', ')"
Write-Host ''
Write-Host "DELETE ($($toDelete.Count)):"
$toDelete | ForEach-Object { Write-Host "  $_" }

if (-not $Execute) {
    Write-Host ''
    Write-Host 'DRY RUN — nothing changed. Re-run with -Execute to apply.' -ForegroundColor Yellow
    exit 0
}

# 3. Re-point latest -> current nvidia (manifest copy, no full image pull)
Write-Host ''
Write-Host "Re-pointing latest -> $CurrentNvidiaTag ..." -ForegroundColor Cyan
$env:DOCKERHUB_TOKEN | docker login -u $env:DOCKERHUB_USER --password-stdin
docker buildx imagetools create -t "${Repo}:latest" "${Repo}:$CurrentNvidiaTag"

# 4. Delete obsolete tags
$failed = @()
foreach ($t in $toDelete) {
    try {
        Invoke-RestMethod -Method Delete -Uri "https://hub.docker.com/v2/repositories/$Repo/tags/$t/" -Headers $headers | Out-Null
        Write-Host "  deleted $t"
    } catch {
        Write-Warning "  FAILED $t : $($_.Exception.Message)"
        $failed += $t
    }
}

Write-Host ''
Write-Host "Done. Deleted $($toDelete.Count - $failed.Count)/$($toDelete.Count) tags. latest -> $CurrentNvidiaTag."
if ($failed.Count) { Write-Warning "Failed: $($failed -join ', ')" }
