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
$CurrentNvidiaTag = 'v1.7.8'   # latest is re-pointed to this

if (-not $env:DOCKERHUB_USER -or -not $env:DOCKERHUB_TOKEN) {
    Write-Error 'Set $env:DOCKERHUB_USER and $env:DOCKERHUB_TOKEN first (use a Docker Hub access token, not your password).'
    exit 1
}

function Test-Keep([string]$n) {
    if ($n -eq 'latest') { return $true }
    if ($n -like 'buildcache-*') { return $true }
    if ($n -in @('docker7','docker7-amd','docker7-intel','docker7-cpu','docker7-apple','docker7-vulkan')) { return $true }
    if ($n -match '^(docker7-)?v1\.7\.8(-|$)') { return $true }
    if ($n -match '^(docker7-)?v1\.7\.7(-|$)') { return $true }
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
