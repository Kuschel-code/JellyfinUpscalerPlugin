"""Every global name main.py references must actually exist.

This exists because of a bug in the commit that added object masking: the detector
loader called `_get_providers()`, a helper this service has never had. Python resolves
global names at CALL time, not import time, so the module imported cleanly, the route
appeared in the OpenAPI schema, the tests around it passed - and the endpoint would
have raised NameError the first time a user touched it. A feature shipped dead.

`symtable` gives the compiler's own view of which names each scope uses as globals,
so this is a static check over the whole file, not a list of names someone remembered
to add. It catches a typo'd helper, a function deleted while a caller survived, and an
import dropped in a refactor - anywhere in main.py, not only in new code.
"""
import builtins
import symtable
from pathlib import Path

MAIN = Path(__file__).resolve().parent.parent / "app" / "main.py"

# Names bound by machinery symtable cannot see: TYPE_CHECKING-only imports, the
# optional-dependency stubs, and anything assigned inside a try/except ImportError that
# symtable still reports as a module-level binding. Kept empty on purpose - every entry
# here is a hole in the check, so a real miss must be fixed rather than listed.
KNOWN_DYNAMIC: set[str] = set()


def _module_bindings(table: symtable.SymbolTable) -> set[str]:
    return {s.get_name() for s in table.get_symbols() if s.is_assigned() or s.is_imported()}


def _walk(table: symtable.SymbolTable, path: str = ""):
    """Yield (qualified name, scope) for every function/class scope in the module."""
    for child in table.get_children():
        name = f"{path}.{child.get_name()}" if path else child.get_name()
        yield name, child
        yield from _walk(child, name)


def test_no_function_references_a_global_that_does_not_exist():
    src = MAIN.read_text(encoding="utf-8")
    top = symtable.symtable(src, str(MAIN), "exec")

    defined = _module_bindings(top) | set(dir(builtins)) | KNOWN_DYNAMIC
    missing = []

    for qualname, scope in _walk(top):
        for sym in scope.get_symbols():
            name = sym.get_name()
            # is_global() is the compiler's verdict: not local here, not a closure cell
            # from an enclosing function - so it must resolve at module level.
            if sym.is_global() and not sym.is_assigned() and name not in defined:
                missing.append(f"{qualname} -> {name}")

    assert not missing, (
        "these names are referenced but never defined at module level, so the code "
        "raises NameError the moment it runs:\n  " + "\n  ".join(sorted(set(missing)))
    )


def test_the_check_would_have_caught_the_bug_it_was_written_for():
    # A guard that cannot fail proves nothing. This is the exact shape of the shipped
    # defect: a call to a helper that does not exist, inside a nested function.
    src = "def endpoint():\n    def _load():\n        return _get_providers()\n    return _load\n"
    top = symtable.symtable(src, "sample.py", "exec")
    defined = _module_bindings(top) | set(dir(builtins))

    missing = [
        s.get_name()
        for _q, scope in _walk(top)
        for s in scope.get_symbols()
        if s.is_global() and not s.is_assigned() and s.get_name() not in defined
    ]

    assert "_get_providers" in missing
