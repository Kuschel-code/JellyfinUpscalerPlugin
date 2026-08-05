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
# v1.8.3.26 - reads AVAILABLE_MODELS with ast instead of importing main.py.
# The old version called exec_module(), which runs the whole service module: it needed
# fastapi, cv2 and onnxruntime installed, and since v1.8.3.23 it failed outright with
# "attempted relative import with no known parent package" because main.py does
# `from . import object_mask` and this loads the file as a standalone module. The
# catalog has therefore been un-regenerable since then. Parsing beats executing here -
# a literal dict needs no imports at all.
import json, ast, sys
from datetime import date

with open(r'$mainPyEsc', encoding='utf-8') as fh:
    tree = ast.parse(fh.read())

available = None
for node in tree.body:
    targets = node.targets if isinstance(node, ast.Assign) else ([node.target] if isinstance(node, ast.AnnAssign) else [])
    for t in targets:
        if isinstance(t, ast.Name) and t.id == 'AVAILABLE_MODELS':
            available = node.value
if available is None:
    print('AVAILABLE_MODELS not found', file=sys.stderr)
    sys.exit(1)

# One entry sets `available` from the runtime flag NCNN_AVAILABLE rather than a literal,
# so a plain literal_eval of the whole dict raises. Values that are not literals are
# reported and treated as True: this file is a FALLBACK catalog shipped inside the DLL and
# read when the service is unreachable, so a runtime capability flag cannot be known here
# anyway. The old exec-based version silently froze whatever the build machine happened to
# have installed, which was worse - it was a guess that did not look like one.
def literal(node, where):
    try:
        return ast.literal_eval(node)
    except ValueError:
        if isinstance(node, ast.Dict):
            return {literal(k, where): literal(v, where) for k, v in zip(node.keys, node.values)}
        print(f'note: {where} is not a literal ({ast.dump(node)[:60]}) - assuming True', file=sys.stderr)
        return True

catalog = {literal(k, 'key'): literal(v, literal(k, 'key')) for k, v in zip(available.keys, available.values)}

models = []
for mid, info in catalog.items():
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

# PowerShell captures native-command output as an ARRAY of lines, and Out-File -NoNewline
# concatenates array elements with no separator - so piping it straight through collapsed
# the whole pretty-printed catalog onto one line, turning every run into a ~1000-line diff
# with no semantic change. Join explicitly, and with LF: this file is read on Linux inside
# the container and the repo keeps it LF.
$text = ($json -join "`n")
[System.IO.File]::WriteAllText($jsonOut, $text, (New-Object System.Text.UTF8Encoding($false)))

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
