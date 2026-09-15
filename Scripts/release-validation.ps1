# Shared checks for the manual release verifier and its corrupt-payload tests.
function Assert-ReleaseFeedEntry {
    param($Feed, [string]$Tag, [string]$Repo, [string]$TargetAbi)
    $guid = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22'
    if ($Feed.Count -ne 1 -or $Feed[0].guid -ne $guid) { throw 'Unexpected plugin GUID in feed' }
    $version = $Tag.TrimStart('v')
    $version4 = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
    $entries = @($Feed[0].versions | Where-Object { $_.version -eq $version4 })
    if ($entries.Count -ne 1) { throw "Expected exactly one feed entry for $Tag" }
    $entry = $entries[0]
    $prefix = "https://github.com/$Repo/releases/download/$Tag/"
    if (-not $entry.sourceUrl.StartsWith($prefix, [StringComparison]::Ordinal) -or
        $entry.sourceUrl.Substring($prefix.Length) -notmatch '^[A-Za-z0-9_.-]+\.zip$') {
        throw 'Feed sourceUrl must point to a ZIP asset in the requested repository and tag'
    }
    if ($entry.targetAbi -ne $TargetAbi) { throw "Feed targetAbi must equal $TargetAbi" }
    if ($entry.checksum -notmatch '^[0-9a-fA-F]{32}$') { throw 'Feed checksum must be a 32-character MD5' }
    return $entry
}

function Assert-PluginArchive {
    param([string]$Path, [string]$Tag, [string]$TargetAbi, [string]$Destination)
    $expected = @('CliWrap.dll', 'FFMpegCore.dll', 'Instances.dll',
        'JellyfinUpscalerPlugin.dll', 'meta.json', 'SixLabors.ImageSharp.dll')
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        # Check original entries before extraction: duplicates and paths must not
        # disappear through overwriting or normalization.
        if ($archive.Entries.Count -ne $expected.Count) { throw 'ZIP must contain exactly six runtime files' }
        foreach ($name in $expected) {
            $matches = @($archive.Entries | Where-Object { $_.FullName -ceq $name })
            if ($matches.Count -ne 1 -or $matches[0].Length -eq 0) {
                throw "ZIP requires exactly one nonempty $name"
            }
        }
    } finally { $archive.Dispose() }
    Expand-Archive -LiteralPath $Path -DestinationPath $Destination -Force
    $meta = Get-Content -LiteralPath (Join-Path $Destination 'meta.json') -Raw | ConvertFrom-Json
    $version = $Tag.TrimStart('v')
    $version4 = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
    if ($meta.guid -ne 'f87f700e-679d-43e6-9c7c-b3a410dc3f22') { throw 'Unexpected plugin GUID in ZIP' }
    if ($meta.version -ne $version -or $meta.targetAbi -ne $TargetAbi) {
        throw 'ZIP metadata version or targetAbi differs from the release'
    }
    foreach ($name in $expected | Where-Object { $_.EndsWith('.dll') }) {
        $dll = Join-Path $Destination $name
        $assembly = [Reflection.AssemblyName]::GetAssemblyName($dll)
        if ($assembly.Name -cne [IO.Path]::GetFileNameWithoutExtension($name)) {
            throw "Wrong assembly identity in $name"
        }
        if ($name -eq 'JellyfinUpscalerPlugin.dll') {
            $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
            if ($assembly.Version.ToString() -ne $version4 -or $fileVersion -ne $version4) {
                throw 'Plugin assembly/file version differs from the release'
            }
        }
    }
}
