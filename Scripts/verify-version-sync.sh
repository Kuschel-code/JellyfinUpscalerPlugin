#!/usr/bin/env bash
# Asserts every version-bearing file matches version.json. Run in CI.
# Exit 0 if all in sync, 1 if any drift, 2 on internal error.

set -uo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

if [ ! -f version.json ]; then
  echo "version.json missing" >&2
  exit 2
fi

EXPECTED="$(jq -r .version version.json)"
if [ -z "$EXPECTED" ] || [ "$EXPECTED" = "null" ]; then
  echo "Could not read .version from version.json" >&2
  exit 2
fi

drift=0
report() {
  local file="$1"; local label="$2"; local actual="$3"
  if [ "$actual" != "$EXPECTED" ]; then
    printf 'DRIFT  %-55s  %s = %s (expected %s)\n' "$file" "$label" "${actual:-<missing>}" "$EXPECTED"
    drift=$((drift+1))
  fi
}

# csproj
report "JellyfinUpscalerPlugin.csproj" "<Version>"         "$(grep -oP '<Version>\K[^<]+'         JellyfinUpscalerPlugin.csproj | head -1)"
report "JellyfinUpscalerPlugin.csproj" "<AssemblyVersion>" "$(grep -oP '<AssemblyVersion>\K[^<]+' JellyfinUpscalerPlugin.csproj | head -1)"
report "JellyfinUpscalerPlugin.csproj" "<FileVersion>"     "$(grep -oP '<FileVersion>\K[^<]+'     JellyfinUpscalerPlugin.csproj | head -1)"

# HTML
report "Configuration/configurationpage.html" "banner"       "$(grep -oP 'AI Upscaler v\K[0-9.]+(?= —)'   Configuration/configurationpage.html | head -1)"
report "Configuration/configurationpage.html" "header-meta"  "$(grep -oP '>v\K[0-9.]+(?= &middot;)'        Configuration/configurationpage.html | head -1)"
report "Configuration/configurationpage.html" "PluginVersion" "$(grep -oP "config\.PluginVersion = '\K[0-9.]+" Configuration/configurationpage.html | head -1)"

# JS resources
for f in Configuration/sidebar-upscaler.js Configuration/player-integration.js Configuration/quick-menu.js; do
  report "$f" "header-comment"   "$(grep -oP '// AI Upscaler Plugin -[^v]*v\K[0-9.]+'    "$f" | head -1)"
  report "$f" "PLUGIN_VERSION"   "$(grep -oP "PLUGIN_VERSION *= *['\"]\K[0-9.]+"         "$f" | head -1)"
done

# Python
report "docker-ai-service/app/main.py" "VERSION" "$(grep -oP '^VERSION = "\K[0-9.]+' docker-ai-service/app/main.py | head -1)"

# Dockerfiles
for df in docker-ai-service/Dockerfile docker-ai-service/Dockerfile.amd docker-ai-service/Dockerfile.intel \
          docker-ai-service/Dockerfile.apple docker-ai-service/Dockerfile.vulkan docker-ai-service/Dockerfile.cpu; do
  report "$df" "ARG APP_VERSION" "$(grep -oP '^ARG APP_VERSION=\K[0-9.]+' "$df" | head -1)"
done

# Manifest top-entries (verify only — never auto-mutate)
report "meta.json"                  ".version"             "$(jq -r '.version'                 meta.json)"
report "manifest.json"              ".[0].versions[0]"     "$(jq -r '.[0].versions[0].version' manifest.json)"
report "repository-jellyfin.json"   ".[0].versions[0]"     "$(jq -r '.[0].versions[0].version' repository-jellyfin.json)"
report "repository-simple.json"     ".[0].versions[0]"     "$(jq -r '.[0].versions[0].version' repository-simple.json)"

if [ "$drift" -gt 0 ]; then
  echo
  echo "FAIL: $drift drifted file(s). Fix with: Scripts/sync-version.sh"
  echo "      (manifest JSONs need a release entry — add via release pipeline.)"
  exit 1
fi

echo "OK: all version strings synced to $EXPECTED."
