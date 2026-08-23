"""API-04: GET /api/offenders/{id}/trail must return points oldest -> newest."""
from datetime import datetime


def _parse(ts):
    return datetime.fromisoformat(ts.replace("Z", "+00:00"))


def _find_offender_with_trail(session, base_url, min_points=2):
    """Picks any live offender with enough trail points, instead of
    hardcoding id=1 (Cohen) — that seed offender was permanently corrupted
    earlier today by BUG-007's app-crashing invalid-signal input, so
    hardcoding it makes this test fail for the wrong reason.
    """
    offenders = session.get(f"{base_url}/api/offenders", params={"pageSize": 100}).json()["items"]
    for o in offenders:
        trail = session.get(f"{base_url}/api/offenders/{o['id']}/trail").json()
        if len(trail) >= min_points:
            return o["id"], trail
    raise AssertionError(f"no offender with >= {min_points} trail points found among {len(offenders)} offenders")


def test_trail_is_returned_in_chronological_order(session, base_url):
    """Known defect: BUG-016 — trail API does not return points in order."""
    offender_id, points = _find_offender_with_trail(session, base_url)

    timestamps = [_parse(p["timestamp"]) for p in points]
    assert timestamps == sorted(timestamps), (
        f"trail points for offender {offender_id} must be ordered oldest -> newest; "
        f"got out-of-order sequence: {[p['timestamp'] for p in points]}"
    )
