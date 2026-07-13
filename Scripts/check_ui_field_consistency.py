#!/usr/bin/env python3
"""check_ui_field_consistency.py — asserts every element ID referenced from JS exists.

Kills the v1.8.3.3 regression class: configurationpage.html is an EmbeddedResource,
so `dotnet build` never sees its inline JS. A removed element whose save-handler
line survived (`page.querySelector('#RemoteUser').value`) crashed config saving at
runtime with a clean build. This check makes that class of drift a CI failure.

Checked:
  1. Configuration/configurationpage.html — every '#id' selector used in its
     inline <script> must exist as id="..." in the same file (self-consistency).
  2. Configuration/*.js (standalone: player/quick-menu/sidebar) — every '#id'
     selector must be defined either in the same file (these scripts build their
     own DOM via innerHTML/createElement) or in configurationpage.html.

Exit 0 = consistent, exit 1 = at least one phantom reference (listed).
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONFIG_DIR = REPO / "Configuration"
CONFIG_PAGE = CONFIG_DIR / "configurationpage.html"

# querySelector('#x') / querySelectorAll("#x .y") / getElementById('x').
# Captures the whole string literal plus a trailing '+' marker so composed
# selectors like querySelector('#tab-' + tabId) can be skipped (not statically
# checkable — the runtime id is prefix+variable).
SELECTOR_RE = re.compile(
    r"""(?P<fn>querySelector(?:All)?|getElementById)\(\s*(?P<q>['"])(?P<sel>[^'"]*)(?P=q)\s*(?P<concat>\+)?"""
)
ID_TOKEN_RE = re.compile(r"#([A-Za-z_][\w-]*)")
# id definitions: HTML attributes (also inside JS template strings) and el.id = '...'
ID_DEF_RE = re.compile(
    r"""\bid\s*=\s*["']([A-Za-z_][\w-]*)["']"""
    r"""|\.id\s*=\s*['"]([A-Za-z_][\w-]*)['"]"""
)

# IDs that are composed at runtime (prefix + variable) or belong to jellyfin-web
# itself. Every entry needs a reason — an unexplained entry is a smell.
ALLOWLIST: dict[str, str] = {
    # jellyfin-web core containers the player/sidebar scripts attach to
    "videoOsdPage": "jellyfin-web's own OSD page, not plugin markup",
    "castButton": "jellyfin-web player control used as an anchor",
}


def refs_in(text: str) -> set[str]:
    out: set[str] = set()
    for m in SELECTOR_RE.finditer(text):
        if m.group("concat"):
            continue  # composed at runtime, not statically checkable
        if m.group("fn") == "getElementById":
            if re.fullmatch(r"[A-Za-z_][\w-]*", m.group("sel")):
                out.add(m.group("sel"))
        else:
            out.update(ID_TOKEN_RE.findall(m.group("sel")))
    return out


def defs_in(text: str) -> set[str]:
    return {m.group(1) or m.group(2) for m in ID_DEF_RE.finditer(text)}


def main() -> int:
    if not CONFIG_PAGE.exists():
        print(f"FAIL: {CONFIG_PAGE} not found")
        return 1

    page_text = CONFIG_PAGE.read_text(encoding="utf-8")
    page_ids = defs_in(page_text)
    failures: list[str] = []

    # 1) config page self-consistency
    for ref in sorted(refs_in(page_text)):
        if ref not in page_ids and ref not in ALLOWLIST:
            failures.append(f"configurationpage.html references #{ref} but defines no such id")

    # 2) standalone scripts: own DOM first, config page second
    for js in sorted(CONFIG_DIR.glob("*.js")):
        js_text = js.read_text(encoding="utf-8")
        own_ids = defs_in(js_text)
        for ref in sorted(refs_in(js_text)):
            if ref in own_ids or ref in page_ids or ref in ALLOWLIST:
                continue
            failures.append(f"{js.name} references #{ref} - not defined in {js.name} nor configurationpage.html")

    if failures:
        print("UI field consistency: FAIL")
        for f in failures:
            print(f"  [PHANTOM] {f}")
        return 1

    n_page = len(refs_in(page_text))
    print(f"UI field consistency: OK ({n_page} config-page refs, "
          f"{len(list(CONFIG_DIR.glob('*.js')))} standalone scripts checked)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
