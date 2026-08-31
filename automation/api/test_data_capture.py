"""Part 5 (Step 7 / TM-06) — centralized, explicit test-data-creation
tracking.

A response hook shared by conftest.py, symmetric to evidence_capture.py:
it observes real create responses the shared session makes and records
ONLY entities the target API's own response actually confirms it created
(a 2xx from a real creation endpoint) — never by scanning the app or by
National ID convention. No test file references this module.

Explicit ownership, not discovery: nothing here consults the "AUTO"
convention at all — that stays purely an additional server-side safety
guard applied later, at cleanup time (7.9), never the ownership signal
itself.
"""

import re

_OFFENDER_CREATE_RE = re.compile(r"/api/offenders/?$")
_LOCATION_CREATE_RE = re.compile(r"/api/offenders/(\d+)/locations/?$")

_state = {"nodeid": None, "created": []}


def begin_scenario(nodeid):
    _state["nodeid"] = nodeid
    _state["created"] = []


def record(response, **_kwargs):
    if _state["nodeid"] is None:
        return
    request = response.request
    if request.method != "POST" or not (200 <= response.status_code < 300):
        return

    path = _url_path(request.url)

    if _OFFENDER_CREATE_RE.search(path):
        try:
            body = response.json()
        except ValueError:
            return
        offender_id = body.get("id")
        if offender_id is None:
            return  # nothing the target app actually confirmed creating
        _state["created"].append(
            {
                "entityType": "Offender",
                "entityExternalId": str(offender_id),
                "entityIdentifier": body.get("nationalId"),
            }
        )
        return

    location_match = _LOCATION_CREATE_RE.search(path)
    if location_match:
        # The target API's POST .../locations response carries no location
        # point id at all (verified live: {"ok": true}) — there is nothing
        # to invent (7.3: "do not invent IDs the target API did not
        # return"). Registered for ownership/inspection visibility only;
        # TM-06 offers no automated cleanup for this entity type — see
        # test-management/README.md for why (no delete endpoint exists,
        # and deleting the parent Offender does not cascade-delete it).
        _state["created"].append(
            {
                "entityType": "LocationPoint",
                "entityExternalId": None,
                "entityIdentifier": f"offenderId={location_match.group(1)}",
            }
        )


def take_created_for(nodeid):
    if _state["nodeid"] != nodeid:
        return []
    return list(_state["created"])


def _url_path(url):
    without_query = url.split("?", 1)[0]
    idx = without_query.find("/api/")
    return without_query[idx:] if idx != -1 else without_query
