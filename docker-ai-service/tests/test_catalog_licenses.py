"""Every model the service offers must say under which licence it may be used.

The catalog carried a ``license`` on 15 of its 77 entries. Three of the entries without
one turned out to be non-commercial upstream - ``span-x2`` and ``foolhardy-remacri-x4``
are CC-BY-NC-SA-4.0 on OpenModelDB, ``codeformer`` is S-Lab License 1.0 - and were
offered like any other model, with nothing telling the user so.

Read with ast, like Scripts/sync-fallback-models.ps1: importing app.main needs cv2 and
onnxruntime, which CI does not install, and would fail this file at collection.
"""
import ast
from pathlib import Path

MAIN = Path(__file__).resolve().parents[1] / "app" / "main.py"

NC_NAME_FLAG = "[non-commercial license]"

# Models whose upstream states no licence anywhere it could be verified. This set may
# only shrink: an entry that gains a licence has to leave it (second test below).
KNOWN_UNLICENSED = {
    # xiongjie/lightweight-real-ESRGAN-anime: no licence tag, no LICENSE file.
    "anime-compact-x4",
    # realsr-df2k-x4 ships inside the ncnn wheel; the origin of the weights is not established.
    "ncnn-realsr-x4",
    # Not on OpenModelDB, and Phhofm/models carries no repository-level licence.
    "bhi-realplksr-x4",
}


def _catalog():
    tree = ast.parse(MAIN.read_text(encoding="utf-8"))
    for node in ast.walk(tree):
        targets = []
        if isinstance(node, ast.Assign):
            targets = node.targets
        elif isinstance(node, ast.AnnAssign):
            targets = [node.target]
        if any(isinstance(t, ast.Name) and t.id == "AVAILABLE_MODELS" for t in targets):
            return {
                ast.literal_eval(key): {
                    ast.literal_eval(k): _literal(v) for k, v in zip(value.keys, value.values)
                }
                for key, value in zip(node.value.keys, node.value.values)
            }
    raise AssertionError("AVAILABLE_MODELS not found in app/main.py")


def _literal(node):
    try:
        return ast.literal_eval(node)
    except ValueError:
        # e.g. "available": NCNN_AVAILABLE - a runtime flag, irrelevant to licensing.
        return None


def _is_non_commercial(license_id):
    # The same test the service applies to imported models ("NC" in license.upper()).
    return "NC" in (license_id or "").upper()


def test_every_model_declares_a_license():
    missing = sorted(
        model_id
        for model_id, info in _catalog().items()
        if not info.get("license") and model_id not in KNOWN_UNLICENSED
    )
    assert not missing, (
        "catalog entries without a license: "
        f"{missing}. Look the licence up at the source of the weights; if upstream "
        "really states none, add the id to KNOWN_UNLICENSED with the reason."
    )


def test_the_unlicensed_list_only_shrinks():
    catalog = _catalog()
    gone = sorted(KNOWN_UNLICENSED - catalog.keys())
    assert not gone, f"KNOWN_UNLICENSED names models that left the catalog: {gone}"
    licensed = sorted(m for m in KNOWN_UNLICENSED if catalog[m].get("license"))
    assert not licensed, f"these now declare a license and must leave KNOWN_UNLICENSED: {licensed}"


def test_non_commercial_models_say_so_in_their_name():
    # The name is the one field every surface shows - dropdown, dashboard, fallback list.
    unflagged = sorted(
        model_id
        for model_id, info in _catalog().items()
        if _is_non_commercial(info.get("license")) and NC_NAME_FLAG not in info.get("name", "")
    )
    assert not unflagged, f"non-commercial models without '{NC_NAME_FLAG}' in the name: {unflagged}"


def test_the_non_commercial_models_are_the_known_ones():
    # Pins the set, so a new NC model is a decision somebody made rather than a side effect.
    nc = {m for m, info in _catalog().items() if _is_non_commercial(info.get("license"))}
    assert nc == {"ultrasharp-v2-x4", "span-x2", "foolhardy-remacri-x4", "codeformer"}