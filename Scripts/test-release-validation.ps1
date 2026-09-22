param([string]$PublishDir = (Join-Path $PSScriptRoot '../bin/Release/net10.0/publish'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-validation.ps1')
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('release-check-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory $tmp
$script:passed = 0
function Expect-Rejection([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $script:passed++
        return
    }
    throw "Corrupt fixture was accepted: $Message"
}
try {
    $meta = Get-Content (Join-Path $PublishDir 'meta.json') -Raw | ConvertFrom-Json
    $tag = 'v' + $meta.version
    $repo = 'Kuschel-code/JellyfinUpscalerPlugin'
    $abi = $meta.targetAbi + '.0'
    $entry = @{version=$meta.version; sourceUrl="https://github.com/$repo/releases/download/$tag/plugin.zip";
        targetAbi=$abi; checksum=('a' * 32)}
    $guid = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22'
    $feed = @(@{guid=$guid; versions=@($entry)})
    $null = Assert-ReleaseFeedEntry $feed $tag $repo $abi
    $script:passed++
    Expect-Rejection { Assert-ReleaseFeedEntry @(@{guid=$guid; versions=@($entry, $entry)}) $tag $repo $abi } 'exactly one'
    foreach ($case in @(
        @('sourceUrl', "https://github.com/wrong/repo/releases/download/$tag/plugin.zip", 'sourceUrl'),
        @('sourceUrl', "https://github.com/$repo/releases/download/$tag/../plugin.zip", 'sourceUrl'),
        @('targetAbi', '10.10.0.0', 'targetAbi'),
        @('checksum', ('a' * 64), 'MD5')
    )) {
        $bad = $entry.Clone(); $bad[$case[0]] = $case[1]
        Expect-Rejection { Assert-ReleaseFeedEntry @(@{guid=$guid; versions=@($bad)}) $tag $repo $abi } $case[2]
    }
    Expect-Rejection { Assert-ReleaseFeedEntry @(@{guid='wrong'; versions=@($entry)}) $tag $repo $abi } 'GUID'
    $short = $entry.Clone(); $short.version = '1.9.0.0'; $short.sourceUrl = "https://github.com/$repo/releases/download/v1.9.0/plugin.zip"
    $null = Assert-ReleaseFeedEntry @(@{guid=$guid; versions=@($short)}) 'v1.9.0' $repo $abi
    $script:passed++
    $stage = Join-Path $tmp 'stage'; $null = New-Item -ItemType Directory $stage
    $names = @('CliWrap.dll', 'FFMpegCore.dll', 'Instances.dll', 'JellyfinUpscalerPlugin.dll', 'meta.json', 'SixLabors.ImageSharp.dll')
    foreach ($name in $names) { Copy-Item (Join-Path $PublishDir $name) $stage }
    $zip = Join-Path $tmp 'plugin.zip'
    function New-TestZip {
        if (Test-Path $zip) { Remove-Item $zip }
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
    }
    function Check-TestZip { Assert-PluginArchive $zip $tag $meta.targetAbi (Join-Path $tmp 'unzipped') }
    New-TestZip; Check-TestZip; $script:passed++
    Set-Content (Join-Path $stage 'Extra.Tests.dll') 'unexpected'; New-TestZip
    Expect-Rejection { Check-TestZip } 'exactly six'
    Remove-Item (Join-Path $stage 'Extra.Tests.dll')
    Remove-Item (Join-Path $stage 'Instances.dll'); New-TestZip
    Expect-Rejection { Check-TestZip } 'exactly six'
    Copy-Item (Join-Path $PublishDir 'Instances.dll') $stage
    New-TestZip
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Update)
    try { $null = $archive.CreateEntry('Instances.dll') } finally { $archive.Dispose() }
    Expect-Rejection { Check-TestZip } 'exactly six'
    [IO.File]::WriteAllBytes((Join-Path $stage 'Instances.dll'), [byte[]]@()); New-TestZip
    Expect-Rejection { Check-TestZip } 'nonempty Instances.dll'
    Copy-Item (Join-Path $PublishDir 'CliWrap.dll') (Join-Path $stage 'Instances.dll') -Force; New-TestZip
    Expect-Rejection { Check-TestZip } 'assembly identity'
    Copy-Item (Join-Path $PublishDir 'Instances.dll') $stage -Force
    $badMeta = $meta | ConvertTo-Json | ConvertFrom-Json
    $badMeta.guid = 'wrong'
    $badMeta | ConvertTo-Json | Set-Content (Join-Path $stage 'meta.json'); New-TestZip
    Expect-Rejection { Check-TestZip } 'GUID'
    $badMeta.guid = $guid
    $badMeta.version = '1.2.3.4' 
    $badMeta | ConvertTo-Json | Set-Content (Join-Path $stage 'meta.json'); New-TestZip
    Expect-Rejection { Check-TestZip } 'metadata version'
    Expect-Rejection { Assert-PluginArchive $zip 'v1.2.3.4' $meta.targetAbi (Join-Path $tmp 'wrong-version') } 'assembly/file version'
    Write-Host "$script:passed release validation cases passed (real publish assemblies)."
} finally { Remove-Item -Recurse -Force $tmp }
