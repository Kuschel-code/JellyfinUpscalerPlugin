#!/usr/bin/env python3
"""Fail closed unless a TRX records at least one executed, successful test."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def validate(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    if root.tag != "{" + ns["t"] + "}TestRun":
        raise ValueError("Not a TRX TestRun")
    summary = root.find("t:ResultSummary", ns)
    counters = root.find("t:ResultSummary/t:Counters", ns)
    if summary is None or counters is None:
        raise ValueError("Missing ResultSummary/Counters")
    if summary.get("outcome") not in {"Completed", "Passed"}:
        raise ValueError("Unsuccessful test run: " + str(summary.get("outcome")))
    counts = {key: int(counters.attrib[key]) for key in ("total", "executed", "passed", "failed")}
    if any(n < 0 for n in counts.values()) or not 0 < counts["executed"] <= counts["total"]:
        raise ValueError("No executed tests or inconsistent counters")
    if counts["failed"] or counts["passed"] != counts["executed"]:
        raise ValueError("Not all executed tests passed")
    for key in ("error", "timeout", "aborted", "disconnected", "notRunnable"):
        if int(counters.get(key, "0")) != 0:
            raise ValueError("Nonzero failure counter: " + key)
    results = root.findall("t:Results/t:UnitTestResult", ns)
    if len(results) != counts["total"]:
        raise ValueError("Missing test results or inconsistent total")
    if any(r.get("outcome") not in {"Passed", "NotExecuted"} for r in results):
        raise ValueError("Unsuccessful individual test result")
    if sum(r.get("outcome") == "Passed" for r in results) != counts["passed"]:
        raise ValueError("Results disagree with passed counter")
    return counts


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: assert-test-results.py path/to/csharp.trx")
        print("TRX verified:", validate(Path(sys.argv[1])))
    except (OSError, ET.ParseError, ValueError, KeyError) as error:
        print("TRX rejected:", error, file=sys.stderr)
        sys.exit(1)
