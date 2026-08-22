"""API-05: GET /api/stats must match the actual stored data at all times."""


def test_stats_offender_total_matches_offenders_endpoint(session, base_url):
    stats = session.get(f"{base_url}/api/stats").json()
    offenders = session.get(f"{base_url}/api/offenders", params={"pageSize": 1}).json()

    assert stats["totalOffenders"] == offenders["total"], (
        "/api/stats totalOffenders should match GET /api/offenders total "
        f"({stats['totalOffenders']} vs {offenders['total']})"
    )


def test_stats_active_offender_total_is_correct(session, base_url):
    offenders = session.get(f"{base_url}/api/offenders", params={"pageSize": 100}).json()
    actual_active = sum(1 for o in offenders["items"] if o["status"] == "Active")

    stats = session.get(f"{base_url}/api/stats").json()
    assert stats["activeOffenders"] == actual_active, (
        f"/api/stats activeOffenders={stats['activeOffenders']} but "
        f"{actual_active} offenders actually have status=Active"
    )


def test_stats_trail_point_total_decreases_after_offender_deletion(session, base_url, unique_national_id):
    """FR-11 / FR-05 — deleting an offender must remove its trail data, and the
    global trail-point total must decrease accordingly.
    Known defect: BUG-015 — total does not decrease after deletion.
    """
    created = session.post(
        f"{base_url}/api/offenders",
        json={
            "firstName": "Stats",
            "lastName": "Cascade",
            "nationalId": unique_national_id,
            "dateOfBirth": "1990-01-01",
            "riskLevel": "Low",
            "status": "Active",
        },
    ).json()
    offender_id = created["id"]

    added_points = 3
    for i in range(added_points):
        session.post(
            f"{base_url}/api/offenders/{offender_id}/locations",
            json={
                "timestamp": f"2026-01-0{i + 1}T00:00:00Z",
                "lat": 32.0 + i * 0.001,
                "lon": 34.0 + i * 0.001,
                "speedKmh": 10,
                "batteryPct": 50,
                "signal": 3,
            },
        )

    before = session.get(f"{base_url}/api/stats").json()["totalLocationPoints"]

    del_resp = session.delete(f"{base_url}/api/offenders/{offender_id}")
    assert del_resp.status_code == 204

    after = session.get(f"{base_url}/api/stats").json()["totalLocationPoints"]

    assert after == before - added_points, (
        f"expected totalLocationPoints to drop by {added_points} after deleting the offender "
        f"and its trail (before={before}, after={after})"
    )
