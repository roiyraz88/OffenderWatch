"""Part 5 (Step 6 / TM-08) — centralized API request/response capture.

A small, pure module shared by conftest.py (which wires the shared
`requests.Session`'s response hook) and ow_event_reporter.py (which reads
what was captured for the scenario that just finished, to write it as
evidence). No individual test file needs to change — this is exactly the
"centralized/instrumented requests.Session approach" 6.16 asks for.

Nothing here makes an HTTP call of its own or touches assertions; it only
observes requests the tests already make through the shared `session`
fixture.
"""

import re

# Header names redacted before anything is ever written to disk (6.16 — "do
# not persist secrets, authentication tokens, cookies, or sensitive headers").
_SENSITIVE_HEADER_RE = re.compile(r"authoriz|cookie|token|api[-_]?key|secret", re.IGNORECASE)

_MAX_BODY_CHARS = 20_000

_state = {"nodeid": None, "entries": []}


def begin_scenario(nodeid):
    """Called once per test (conftest's pytest_runtest_setup) so capture never leaks across scenarios."""
    _state["nodeid"] = nodeid
    _state["entries"] = []


def record(response, **_kwargs):
    """The requests.Session response hook — appended to every request/response the shared session makes. requests calls response hooks as hook(response, **request_kwargs), so **_kwargs absorbs whatever requests passes along (e.g. `timeout`)."""
    if _state["nodeid"] is None:
        return
    request = response.request
    _state["entries"].append(
        {
            "request": {
                "method": request.method,
                "url": request.url,
                "headers": _safe_headers(request.headers),
                "body": _safe_text(request.body),
            },
            "response": {
                "statusCode": response.status_code,
                "headers": _safe_headers(response.headers),
                "body": _safe_response_body(response),
            },
        }
    )


def take_final_for(nodeid):
    """The last captured request/response pair for this scenario (6.16's "final request and response"), or None if the session made no HTTP calls."""
    if _state["nodeid"] != nodeid or not _state["entries"]:
        return None
    return _state["entries"][-1]


def _safe_headers(headers):
    return {k: ("<redacted>" if _SENSITIVE_HEADER_RE.search(k) else v) for k, v in dict(headers).items()}


def _safe_text(body):
    if body is None:
        return None
    if isinstance(body, bytes):
        try:
            body = body.decode("utf-8")
        except UnicodeDecodeError:
            return "<binary>"
    return body[:_MAX_BODY_CHARS]


def _safe_response_body(response):
    try:
        return response.text[:_MAX_BODY_CHARS]
    except Exception:
        return None
