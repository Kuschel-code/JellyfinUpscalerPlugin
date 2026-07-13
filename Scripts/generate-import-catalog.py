#!/usr/bin/env python3
"""generate-import-catalog.py — regenerates site/models-import.json from OpenModelDB.

Source:  OpenModelDB/open-model-database (data/models/*.json), cloned locally or
         passed as a path.
Output:  <out>/models-import.json — the single data source. The website page
         (site/models-import.html) renders this JSON client-side, and a future
         importer endpoint resolves model ids against it. The HTML is therefore
         NOT generated here (v1.8.3.5: one data source, no duplicated markup).

Principle: the catalog is GENERATED, never hand-maintained (anti-drift).
           Refreshed weekly by .github/workflows/import-catalog-refresh.yml.

Usage:   python3 Scripts/generate-import-catalog.py <path-to-open-model-database> <output-dir>
"""
import datetime
import glob
import json
import os
import sys


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "OMDB"
    out = sys.argv[2] if len(sys.argv) > 2 else "out"
    os.makedirs(out, exist_ok=True)

    direct, convertible = [], []
    for f in sorted(glob.glob(os.path.join(src, "data/models/*.json"))):
        d = json.load(open(f, encoding="utf-8"))
        model_id = os.path.basename(f)[:-5]
        onnx_res = [r for r in d.get("resources", []) if r.get("platform") == "onnx"]
        any_res = d.get("resources", [])
        entry = {
            "id": model_id,
            "name": d.get("name", model_id),
            "author": d.get("author", ""),
            "scale": d.get("scale", "?"),
            "architecture": d.get("architecture", "?"),
            "license": d.get("license") or "",
            "tags": d.get("tags", []),
            "description": (d.get("description") or "").strip(),
            "date": d.get("date", ""),
            "omdb_url": f"https://openmodeldb.info/models/{model_id}",
        }
        if onnx_res:
            r = onnx_res[0]
            entry.update({
                "download_url": (r.get("urls") or [""])[0],
                "sha256": r.get("sha256", ""),
                "size_bytes": r.get("size", 0),
                "import": "direct",
            })
            direct.append(entry)
        elif any_res:
            r = any_res[0]
            entry.update({
                "source_platform": r.get("platform", "?"),
                "download_url": (r.get("urls") or [""])[0],
                "size_bytes": r.get("size", 0),
                "import": "convert",
            })
            convertible.append(entry)

    stamp = datetime.date.today().isoformat()
    out_path = os.path.join(out, "models-import.json")
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump({
            "generated": stamp,
            "source": "OpenModelDB/open-model-database",
            "direct_onnx": direct,
            "requires_conversion": convertible,
        }, fh, indent=1, ensure_ascii=False)

    print(f"OK: {len(direct)} direct-ONNX, {len(convertible)} convertible")
    print(f"   -> {out_path}")


if __name__ == "__main__":
    main()
