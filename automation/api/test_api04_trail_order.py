"""API-04: GET /api/offenders/{id}/trail must return points oldest -> newest."""
from datetime import datetime


def _parse(ts):
    return datetime.fromisoformat(ts.replace("Z", "+00:00"))


def test_trail_is_returned_in_chronological_order(session, base_url):
    """Known defect: BUG-016 — trail API does not return points in order."""
    resp = session.get(f"{base_url}/api/offenders/1/trail")
    assert resp.status_code == 200
    points = resp.json()
    assert len(points) > 1, "offender 1 should have multiple trail points for this check"

    timestamps = [_parse(p["timestamp"]) for p in points]
    assert timestamps == sorted(timestamps), (
        "trail points must be ordered oldest -> newest; "
        f"got out-of-order sequence: {[p['timestamp'] for p in points]}"
    )
