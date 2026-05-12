#!/usr/bin/env pwsh
# v1.7.3 - syncs the <span class="brand-version">vX.Y.Z</span> in all site/*.html to the
# current plugin version from meta.json. Idempotent - safe to run in CI on every commit.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Join-Path $PSScriptRoot ".."),
    [string]$Version
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $metaPath = Join-Path $RepoRoot "meta.json"
    if (-not (Test-Path $metaPath)) { Write-Error "meta.json not found at $metaPath"; exit 1 }
    $meta = Get-Content $metaPath -Raw | ConvertFrom-Json
    $Version = $meta.version
    if ([string]::IsNullOrWhiteSpace($Version)) { Write-Error "Could not read .version from meta.json"; exit 1 }
}

# Versions in the topbar are shown without trailing ".0" segments.
$DisplayVersion = $Version -replace '\.0+$', ''

$sitePages = Get-ChildItem -Path (Join-Path $RepoRoot "site") -Filter "*.html" -File -ErrorAction SilentlyContinue
if (-not $sitePages -or $sitePages.Count -eq 0) {
    Write-Warning "No site/*.html files found"
    exit 0
}

$pattern = '<span class="brand-version">v[0-9]+(\.[0-9]+)*</span>'
$replacement = '<span class="brand-version">v' + $DisplayVersion + '</span>'

$changed = 0
$total = 0
foreach ($page in $sitePages) {
    $total++
    $content = Get-Content -Path $page.FullName -Raw -Encoding UTF8
    $updated = [regex]::Replace($content, $pattern, $replacement)
    if ($updated -ne $content) {
        Set-Content -Path $page.FullName -Value $updated -NoNewline -Encoding UTF8
        Write-Host "  Updated: $($page.Name)" -ForegroundColor Green
        $changed++
    }
}

Write-Host ""
Write-Host "Sync complete: $changed of $total files updated to v$DisplayVersion" -ForegroundColor Cyan
