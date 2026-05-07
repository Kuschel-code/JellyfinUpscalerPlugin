# sync-fallback-models.ps1
# Regenerate Resources/models-fallback.json from
# docker-ai-service/app/main.py:AVAILABLE_MODELS.
#
# Run after editing AVAILABLE_MODELS to keep the C# fallback registry in sync.
# Source of truth: docker-ai-service/app/main.py
# Embedded into Plugin.dll via JellyfinUpscalerPlugin.csproj <EmbeddedResource>
# Consumed at runtime by Controllers/UpscalerController.cs (when Docker service unreachable)
#
# Usage:        pwsh Scripts/sync-fallback-models.ps1
# CI gate:      this script must be a no-op on a clean tree (verify via `git diff --exit-code`)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$mainPy = Join-Path $repo "docker-ai-service/app/main.py"
$jsonOut = Join-Path $repo "Resources/models-fallback.json"

if (-not (Test-Path $mainPy)) {
    Write-Error "main.py not found at $mainPy"
    exit 1
}

$mainPyEsc = $mainPy.Replace('\', '\\')
$pythonScript = @"
import json, importlib.util, sys
from datetime import date

spec = importlib.util.spec_from_file_location('mainmodule', r'$mainPyEsc')
m = importlib.util.module_from_spec(spec)
sys.modules['mainmodule'] = m
spec.loader.exec_module(m)

models = []
for mid, info in m.AVAILABLE_MODELS.items():
    models.append({
        'id': mid,
        'name': info.get('name', ''),
        'description': (info.get('description') or '')[:200],
        'scale': [info.get('scale', 1)],
        'category': info.get('category', 'uncategorized'),
        'type': info.get('type', 'onnx'),
        'available': bool(info.get('available', True)),
        'downloaded': False,
        'loaded': False,
    })

out = {
    'generated_at': date.today().isoformat(),
    'source': 'docker-ai-service/app/main.py:AVAILABLE_MODELS',
    'generator': 'Scripts/sync-fallback-models.ps1',
    'notice': 'AUTO-GENERATED - DO NOT EDIT BY HAND. Regenerate via the script when main.py changes.',
    'total': len(models),
    'models': models,
}
print(json.dumps(out, indent=2))
"@

Write-Host "Parsing AVAILABLE_MODELS from $mainPy ..."
$json = python -c $pythonScript
if ($LASTEXITCODE -ne 0) {
    Write-Error "Python parser failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Ensure output directory exists
$resourcesDir = Split-Path $jsonOut -Parent
if (-not (Test-Path $resourcesDir)) {
    New-Item -ItemType Directory -Path $resourcesDir | Out-Null
}

$json | Out-File -FilePath $jsonOut -Encoding UTF8 -NoNewline

# Parse for summary stats
$registry = $json | ConvertFrom-Json
$availCount = ($registry.models | Where-Object { $_.available }).Count

Write-Host ""
Write-Host "Wrote: $jsonOut"
Write-Host "       $($registry.total) models, $availCount available, $($registry.total - $availCount) self-host"
Write-Host ""
Write-Host "Note: site/models.html regeneration is not automated yet."
Write-Host "      When you change AVAILABLE_MODELS, manually re-render site/models.html"
Write-Host "      from Resources/models-fallback.json (or add a CI step to do it)."
