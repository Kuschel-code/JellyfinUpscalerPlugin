#!/usr/bin/env bash
# Propagates the version from version.json into every source file that
# carries a self-identifying version string. Idempotent.
#
# Manifest JSONs (manifest.json, repository-*.json, meta.json) are NOT
# auto-mutated here — adding a new release entry needs a real checksum
# and sourceUrl which only exist after `gh release create`. Instead,
# verify-version-sync.sh asserts the *top* entry matches version.json
# so drift is caught.
#
# Usage: Scripts/sync-version.sh [new-version]
#   - With no argument: read version from version.json
#   - With argument:    write that value into version.json first, then sync

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

if [ "${1:-}" != "" ]; then
  printf '{\n  "version": "%s"\n}\n' "$1" > version.json
fi

VERSION="$(jq -r .version version.json)"
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Refusing to sync: version '$VERSION' does not match X.Y.Z.W" >&2
  exit 2
fi
echo "Syncing version $VERSION across source files..."

# C# csproj — 3 elements
sed -i -E "s|<Version>[^<]+</Version>|<Version>$VERSION</Version>|"                         JellyfinUpscalerPlugin.csproj
sed -i -E "s|<AssemblyVersion>[^<]+</AssemblyVersion>|<AssemblyVersion>$VERSION</AssemblyVersion>|" JellyfinUpscalerPlugin.csproj
sed -i -E "s|<FileVersion>[^<]+</FileVersion>|<FileVersion>$VERSION</FileVersion>|"         JellyfinUpscalerPlugin.csproj

# HTML configurationpage — banner comment, header meta, hardcoded saved value
sed -i -E "s|AI Upscaler v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+ —|AI Upscaler v$VERSION —|"        Configuration/configurationpage.html
sed -i -E "s|>v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+ &middot;|>v$VERSION \&middot;|"               Configuration/configurationpage.html
sed -i -E "s|config\.PluginVersion = '[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+'|config.PluginVersion = '$VERSION'|" Configuration/configurationpage.html

# JS embedded resources
for f in Configuration/sidebar-upscaler.js Configuration/player-integration.js Configuration/quick-menu.js; do
  sed -i -E "s|(// AI Upscaler Plugin -[^v]*v)[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|\1$VERSION|"     "$f"
  sed -i -E "s|(PLUGIN_VERSION *= *['\"])[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|\1$VERSION|"          "$f"
done

# Python AI service
sed -i -E "s|^VERSION = \"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+\"|VERSION = \"$VERSION\"|"          docker-ai-service/app/main.py

# All Dockerfiles — ARG APP_VERSION default + the example comment one line above
for df in docker-ai-service/Dockerfile docker-ai-service/Dockerfile.amd docker-ai-service/Dockerfile.intel \
          docker-ai-service/Dockerfile.apple docker-ai-service/Dockerfile.vulkan docker-ai-service/Dockerfile.cpu; do
  sed -i -E "s|--build-arg APP_VERSION=[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|--build-arg APP_VERSION=$VERSION|" "$df"
  sed -i -E "s|^ARG APP_VERSION=[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|ARG APP_VERSION=$VERSION|"               "$df"
done

echo "Done. Run Scripts/verify-version-sync.sh to confirm."
