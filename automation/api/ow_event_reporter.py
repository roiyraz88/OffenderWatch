"""Part 5 (Step 4 / TM-02) structured runner-event reporter.

Emits one JSON object per line, prefixed with "OW_EVENT|", to stdout for
every scenario_discovered / scenario_started / scenario_finished /
suite_finished event — the machine-readable protocol the ASP.NET Core
orchestrator parses (never human-readable pytest console output).

Does NOT touch test behavior, assertions, or fixtures — this is a pure
pytest plugin (hook implementations only), loaded via conftest.py's
`pytest_plugins`. No HTTP calls live in the tests themselves.
"""

import json
import re
import sys
from datetime import datetime, timezone

# Matches "BUG-001", or "BUG-007 / BUG-018" style combined tags, wherever
# they appear in a test's own docstring or its module's docstring — this
# repo already writes "Known defect: BUG-xxx" as free text, not a separate
# structured field, so that's the real, existing signal to read.
_BUG_ID_RE = re.compile(r"BUG-\d+(?:\s*/\s*BUG-\d+)*")
_REQUIREMENT_ID_RE = re.compile(r"\b(?:FR|API)-\d+\b")

_finished_nodeids = set()


def _now_iso():
    return datetime.now(timezone.utc).isoformat()


def _emit(event):
    payload = {"version": 1, "runner": "pytest", "timestampUtc": _now_iso(), **event}
    # Leading newline: pytest's own live "test name ... PASSED" line is
    # sometimes still open (no trailing "\n" yet) when a hook fires, which
    # would otherwise glue our line onto the middle of pytest's own output.
    sys.stdout.write("\nOW_EVENT|" + json.dumps(payload) + "\n")
    sys.stdout.flush()


def _external_id(nodeid):
    # api::<pytest nodeid> — stable across runs, no run-specific data (4.8).
    return f"api::{nodeid}"


def _extract_metadata(item):
    func = getattr(item, "obj", None)
    func_doc = (getattr(func, "__doc__", None) or "") if func else ""
    module_doc = getattr(getattr(item, "module", None), "__doc__", None) or ""

    bug_match = _BUG_ID_RE.search(func_doc) or _BUG_ID_RE.search(module_doc)
    req_match = _REQUIREMENT_ID_RE.search(func_doc) or _REQUIREMENT_ID_RE.search(module_doc)

    return (
        req_match.group(0) if req_match else None,
        bug_match.group(0) if bug_match else None,
    )


def pytest_collection_modifyitems(session, config, items):
    for item in items:
        requirement_id, bug_id = _extract_metadata(item)
        _emit(
            {
                "eventType": "scenario_discovered",
                "externalId": _external_id(item.nodeid),
                "name": item.nodeid,
                "suite": "API",
                "requirementId": requirement_id,
                "bugId": bug_id,
            }
        )


def pytest_runtest_logstart(nodeid, location):
    _emit({"eventType": "scenario_started", "externalId": _external_id(nodeid)})


def _finish(nodeid, status, duration_seconds, longrepr):
    if nodeid in _finished_nodeids:
        return
    _finished_nodeids.add(nodeid)

    failure_message = None
    stack_trace = None
    if status == "failed" and longrepr is not None:
        stack_trace = str(longrepr)
        # Last non-empty line is usually the assertion message itself.
        non_empty = [l for l in stack_trace.splitlines() if l.strip()]
        failure_message = non_empty[-1] if non_empty else "Test failed."

    _emit(
        {
            "eventType": "scenario_finished",
            "externalId": _external_id(nodeid),
            "status": status,
            "durationMs": int(duration_seconds * 1000),
            "failureMessage": failure_message,
            "stackTrace": stack_trace,
        }
    )


def pytest_runtest_logreport(report):
    # "call" is the normal path: the test body actually ran to completion
    # (pass or fail). A "setup" failure/skip (e.g. a fixture raised) means
    # the test never reaches "call" at all, so it must be finished from here
    # instead, or it would never get an event.
    if report.when == "call":
        if report.passed:
            status = "passed"
        elif report.failed:
            status = "failed"
        else:
            status = "skipped"
        _finish(report.nodeid, status, report.duration, report.longrepr)
    elif report.when == "setup" and not report.passed:
        status = "skipped" if report.skipped else "failed"
        _finish(report.nodeid, status, report.duration, report.longrepr)


def pytest_sessionfinish(session, exitstatus):
    _emit({"eventType": "suite_finished", "totalScenarios": len(_finished_nodeids)})
