#!/usr/bin/env python3
"""Bump every user-visible version string to a new release.

The 16 sites below mirror $versionChecks in Scripts/verify-release.ps1 exactly.
That list exists because this project shipped two releases in a row where a
displayed version lagged behind the tag, and one (#74) where a feed did. Running
this instead of hand-editing keeps the two lists in sync by construction.

Usage:  python Scripts/bump-version.py 1.8.3.13 1.8.3.14

Every replacement is asserted BEFORE anything is written: a crash halfway through
used to leave the checkout in a mixed state that then shipped.
"""
import io
import re
import sys


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    old, new = sys.argv[1], sys.argv[2]

    # meta.json mirrors the git tag: 3-part when the 4th part is 0, else 4-part.
    def tag_form(v: str) -> str:
        parts = v.split(".")
        return ".".join(parts[:3]) if len(parts) == 4 and parts[3] == "0" else v

    old_tag, new_tag = tag_form(old), tag_form(new)

    # (path, regex with ONE capture group holding the version, replacement version)
    sites = [
        ("Configuration/configurationpage.html", r'(?<=upscaler-header-meta">v)([\d.]+)', new_tag),
        ("Configuration/configurationpage.html", r"(?<=config\.PluginVersion = ')([\d.]+)(?=')", new_tag),
        ("Configuration/player-integration.js", r"(?<=PLUGIN_VERSION = ')([\d.]+)(?=')", new_tag),
        ("Configuration/quick-menu.js", r"(?<=PLUGIN_VERSION = ')([\d.]+)(?=')", new_tag),
        ("Configuration/sidebar-upscaler.js", r"(?<=PLUGIN_VERSION = ')([\d.]+)(?=')", new_tag),
        ("meta.json", r'(?<="version": ")([\d.]+)(?=")', new_tag),
        ("README.md", r"(?<=^# Jellyfin AI Upscaler Plugin v)([\d.]+)", new_tag),
        ("README.md", r"(?<=in lockstep with the plugin, both at v)([\d.]+)(?=\))", new_tag),
        ("README.md", r"(?<=AI Upscaler Plugin v)([\d.]+)(?=\s+│)", new_tag),
        ("PluginConfiguration.cs", r'(?<=PluginVersion \{ get; set; \} = ")([\d.]+)(?=")', new_tag),
        ("JellyfinUpscalerPlugin.csproj", r"(?<=<Version>)([\d.]+)(?=</Version>)", new),
        ("JellyfinUpscalerPlugin.csproj", r"(?<=<AssemblyVersion>)([\d.]+)(?=</AssemblyVersion>)", new),
        ("JellyfinUpscalerPlugin.csproj", r"(?<=<FileVersion>)([\d.]+)(?=</FileVersion>)", new),
    ]

    # Load once per file so several sites in one file do not clobber each other.
    loaded: dict[str, str] = {}
    plan = []
    for path, pattern, target in sites:
        if path not in loaded:
            with io.open(path, encoding="utf-8", newline="") as f:
                loaded[path] = f.read()
        hits = re.findall(pattern, loaded[path], re.M)
        if len(hits) != 1:
            print(f"FAIL {path}: /{pattern}/ matched {len(hits)} times (need exactly 1)")
            return 1
        if hits[0] not in (old, old_tag, target):
            print(f"FAIL {path}: found '{hits[0]}', expected '{old}' or '{old_tag}'")
            return 1
        plan.append((path, pattern, target, hits[0]))

    for path, pattern, target, was in plan:
        loaded[path] = re.sub(pattern, target, loaded[path], count=1, flags=re.M)
        print(f"  {path:45s} {was} -> {target}")

    for path, text in loaded.items():
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)

    print(f"\n{len(plan)} version sites bumped to {new} (tag form {new_tag}).")
    print("Feeds (manifest.json, repository-jellyfin.json, repository-simple.json)")
    print("still need the new entry with its real checksum - see the release process.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
