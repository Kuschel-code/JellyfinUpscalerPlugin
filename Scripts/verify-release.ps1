<#
.SYNOPSIS
  Validates a published Jellyfin-Upscaler-Plugin GitHub release end-to-end.

.DESCRIPTION
  Runs AFTER `gh release create`. It downloads every ZIP asset from the named
  release, verifies each is a well-formed plugin bundle (no Moq/Mono.Cecil/
  InstrumentationEngine/CodeCoverage/Scripts leak-through from a `dotnet test`
  output), and confirms the MD5 matches the live manifest checksum.
  (Jellyfin's plugin installer computes MD5 of the downloaded ZIP and rejects
  the package when it doesn't match the 32-char hex `checksum` in the manifest.)

  It also asserts that every USER-VISIBLE version string in the local checkout
  (configurationpage.html header + PluginVersion, the three JS PLUGIN_VERSION
  consts, meta.json, and the csproj Version/AssemblyVersion/FileVersion) equals
  the release tag. Those strings compile into the DLL, so the ZIP/meta check
  cannot see them — this is the guard that would have caught the v1.7.10 drift
  where the dashboard still rendered "v1.7.9" after release.

  Exits 1 on ANY mismatch. Designed so the release is only marked "done" when
  this script passes — this is the guard that would have caught the v1.6.1.11
  wrong-ZIP-upload where a 13.9 MB test-binary got hosted instead of the 1.7 MB
  plugin bundle.

.PARAMETER Tag
  Release tag to verify. E.g. 'v1.6.1.12'.

.PARAMETER Repo
  GitHub repo in owner/name form. Defaults to Kuschel-code/JellyfinUpscalerPlugin.

.PARAMETER ManifestUrl
  Legacy override, no longer used for the checksum source: since v1.8.3.5 the
  script asserts ALL THREE live feeds (manifest.json, repository-jellyfin.json,
  repository-simple.json) agree on the release entry and checksum.

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
# $env:TEMP on Windows returns the 8.3 short path (e.g. C:\Users\KUSCHE~1\...),
# but Get-ChildItem.FullName returns the resolved long-path form. That length
# mismatch silently broke Substring-based relative-path extraction here. Neither
# Resolve-Path nor Convert-Path expand 8.3 — only Get-Item.FullName does.
$tmp = (Get-Item -LiteralPath $tmp).FullName

try {
    # v1.8.3.5 — ALL THREE plugin feeds must carry the release, consistently.
    # Issue #74 root cause: manifest.json (the most-advertised feed) silently
    # lagged behind because only repository-jellyfin/-simple were maintained.
    # Any feed missing the tag entry, pointing its sourceUrl at a different
    # tag, or carrying a diverging checksum fails the release.
    Write-Host "=== Fetching all three live feeds ===" -ForegroundColor Cyan
    $tagVersionFeed = $Tag.TrimStart("v")
    $rawBase = "https://raw.githubusercontent.com/$Repo/main"
    $feedUrls = @(
        "$rawBase/manifest.json",
        "$rawBase/repository-jellyfin.json",
        "$rawBase/repository-simple.json"
    )
    $feedChecksums = @{}
    foreach ($feedUrl in $feedUrls) {
        $feedName = Split-Path -Leaf $feedUrl
        try {
            $feed = Invoke-RestMethod -Uri $feedUrl
        } catch {
            Write-Host ("  [FAIL] " + $feedName + ": fetch failed (" + $_.Exception.Message + ")") -ForegroundColor Red
            $fail = $true
            continue
        }
        $entry = $feed[0].versions | Where-Object { $_.version -eq $tagVersionFeed } | Select-Object -First 1
        if (-not $entry) {
            Write-Host ("  [FAIL] " + $feedName + ": no entry with version " + $tagVersionFeed) -ForegroundColor Red
            $fail = $true
            continue
        }
        if ($entry.sourceUrl -notlike "*/download/$Tag/*") {
            Write-Host ("  [FAIL] " + $feedName + ": sourceUrl does not point at the $Tag asset (" + $entry.sourceUrl + ")") -ForegroundColor Red
            $fail = $true
        }
        if (-not ($entry.checksum -match '^[0-9a-fA-F]{32}$')) {
            Write-Host ("  [FAIL] " + $feedName + ": checksum is not a 32-char hex MD5 ('" + $entry.checksum + "')") -ForegroundColor Red
            $fail = $true
            continue
        }
        $feedChecksums[$feedName] = $entry.checksum.ToLower()
        Write-Host ("  [OK] " + $feedName + " -> checksum " + $entry.checksum.ToLower() + ", targetAbi " + $entry.targetAbi)
    }
    if (($feedChecksums.Values | Select-Object -Unique).Count -gt 1) {
        Write-Host "  [FAIL] feeds disagree on the checksum:" -ForegroundColor Red
        $feedChecksums.GetEnumerator() | ForEach-Object { Write-Host ("      " + $_.Key + " = " + $_.Value) -ForegroundColor Red }
        $fail = $true
    }
    if ($feedChecksums.Count -eq 0) {
        Write-Host "FAIL: no feed carries tag $Tag - nothing to verify the ZIP against" -ForegroundColor Red
        exit 1
    }
    $manifestChecksum = ($feedChecksums.Values | Select-Object -First 1)

    Write-Host ""
    Write-Host "=== Source version-string consistency (local checkout) ===" -ForegroundColor Cyan
    # Guards the whole class of "displayed version lagged behind the release" drift.
    # v1.7.10 shipped with Configuration/configurationpage.html still rendering "v1.7.9"
    # because a manual bump missed it - and these strings compile INTO the DLL, so the
    # post-publish ZIP/meta check below cannot see them. Every USER-VISIBLE version
    # string in the checkout must equal the release tag, or the release is not "done".
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $tagVersion = $Tag.TrimStart("v")                                # e.g. 1.7.11
    $tagVersion4 = if ($tagVersion -match '^\d+\.\d+\.\d+$') { "$tagVersion.0" } else { $tagVersion }  # csproj uses x.y.z.0

    $versionChecks = @(
        @{ File = "Configuration/configurationpage.html"; Pattern = 'upscaler-header-meta">v([\d.]+)';            Expect = $tagVersion  },
        @{ File = "Configuration/configurationpage.html"; Pattern = "config\.PluginVersion = '([\d.]+)'";          Expect = $tagVersion  },
        @{ File = "Configuration/player-integration.js";  Pattern = "PLUGIN_VERSION = '([\d.]+)'";                 Expect = $tagVersion  },
        @{ File = "Configuration/quick-menu.js";          Pattern = "PLUGIN_VERSION = '([\d.]+)'";                 Expect = $tagVersion  },
        @{ File = "Configuration/sidebar-upscaler.js";    Pattern = "PLUGIN_VERSION = '([\d.]+)'";                 Expect = $tagVersion  },
        @{ File = "meta.json";                            Pattern = '"version"\s*:\s*"([\d.]+)"';                  Expect = $tagVersion  },
        # ALL THREE local feed files must lead with the new release (first "version" in
        # each file = newest entry). Local counterpart of the live triple-feed assert.
        @{ File = "manifest.json";                        Pattern = '"version"\s*:\s*"([\d.]+)"';                  Expect = $tagVersion  },
        @{ File = "repository-jellyfin.json";             Pattern = '"version"\s*:\s*"([\d.]+)"';                  Expect = $tagVersion  },
        @{ File = "repository-simple.json";               Pattern = '"version"\s*:\s*"([\d.]+)"';                  Expect = $tagVersion  },
        # v1.8.3.5 follow-up: README + config default drifted for TWO releases
        # (README body said v1.8.3.3, PluginConfiguration default said 1.7.7)
        # because none of them were guarded. Now they are.
        @{ File = "README.md";                            Pattern = '^# Jellyfin AI Upscaler Plugin v([\d.]+)';    Expect = $tagVersion  },
        @{ File = "README.md";                            Pattern = 'independently versioned at v([\d.]+)\)';      Expect = $tagVersion  },
        @{ File = "README.md";                            Pattern = 'AI Upscaler Plugin v([\d.]+)\s+│';            Expect = $tagVersion  },
        @{ File = "PluginConfiguration.cs";               Pattern = 'PluginVersion \{ get; set; \} = "([\d.]+)"';  Expect = $tagVersion  },
        @{ File = "JellyfinUpscalerPlugin.csproj";        Pattern = '<Version>([\d.]+)</Version>';                 Expect = $tagVersion4 },
        @{ File = "JellyfinUpscalerPlugin.csproj";        Pattern = '<AssemblyVersion>([\d.]+)</AssemblyVersion>'; Expect = $tagVersion4 },
        @{ File = "JellyfinUpscalerPlugin.csproj";        Pattern = '<FileVersion>([\d.]+)</FileVersion>';         Expect = $tagVersion4 }
    )

    foreach ($chk in $versionChecks) {
        $path = Join-Path $repoRoot $chk.File
        if (-not (Test-Path $path)) {
            Write-Host ("  [FAIL] missing file: " + $chk.File) -ForegroundColor Red
            $fail = $true
            continue
        }
        $content = Get-Content -LiteralPath $path -Raw
        $m = [regex]::Match($content, $chk.Pattern)
        if (-not $m.Success) {
            Write-Host ("  [FAIL] " + $chk.File + ": version pattern not found (/" + $chk.Pattern + "/)") -ForegroundColor Red
            $fail = $true
        } elseif ($m.Groups[1].Value -ne $chk.Expect) {
            Write-Host ("  [FAIL] " + $chk.File + ": found '" + $m.Groups[1].Value + "', expected '" + $chk.Expect + "'") -ForegroundColor Red
            $fail = $true
        } else {
            Write-Host ("  [OK] " + $chk.File + " = " + $m.Groups[1].Value)
        }
    }

    Write-Host ""
    Write-Host "=== UI field consistency (JS selectors vs HTML ids) ===" -ForegroundColor Cyan
    # Kills the v1.8.3.3 class: config-page JS referencing a removed element id
    # compiles clean (EmbeddedResource) and only crashes at runtime.
    $py = (Get-Command python -ErrorAction SilentlyContinue) ?? (Get-Command python3 -ErrorAction SilentlyContinue)
    if ($py) {
        & $py.Source (Join-Path $PSScriptRoot "check_ui_field_consistency.py")
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [FAIL] UI field consistency check failed" -ForegroundColor Red
            $fail = $true
        }
    } else {
        Write-Host "  [FAIL] python not found - cannot run check_ui_field_consistency.py" -ForegroundColor Red
        $fail = $true
    }

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

        $md5 = (Get-FileHash -Algorithm MD5 -Path $zip.FullName).Hash.ToLower()
        $sha = (Get-FileHash -Algorithm SHA256 -Path $zip.FullName).Hash.ToLower()
        Write-Host ("  size: " + $zip.Length + " bytes")
        Write-Host ("  md5 (manifest): " + $md5)
        Write-Host ("  sha256 (info):  " + $sha)

        if ($md5 -ne $manifestChecksum) {
            Write-Host ("  [FAIL] MD5 does not match manifest checksum (Jellyfin expects MD5)") -ForegroundColor Red
            $fail = $true
        } else {
            Write-Host "  [OK] MD5 matches manifest"
        }

        # Inspect contents
        $unzipDir = Join-Path $tmp ($zip.BaseName + "-unzipped")
        New-Item -ItemType Directory -Force -Path $unzipDir | Out-Null
        Expand-Archive -Path $zip.FullName -DestinationPath $unzipDir -Force
        $unzipDir = (Get-Item -LiteralPath $unzipDir).FullName

        $entries = @(Get-ChildItem -Path $unzipDir -Recurse -File | ForEach-Object {
            $_.FullName.Substring($unzipDir.Length + 1).Replace("\", "/")
        })

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
