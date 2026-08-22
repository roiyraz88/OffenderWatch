"""API-02: 404 for unknown resources, 201 on create, 204 on delete."""

UNKNOWN_ID = 999999


def test_get_unknown_offender_returns_404(session, base_url):
    """Known defect: BUG-012 — API returns 200 instead of 404 for an unknown id."""
    resp = session.get(f"{base_url}/api/offenders/{UNKNOWN_ID}")
    assert resp.status_code == 404, (
        f"expected 404 for unknown offender id, got {resp.status_code}: {resp.text[:200]}"
    )


def test_get_unknown_offender_trail_returns_404(session, base_url):
    """Known defect: BUG-012 — trail for an unknown id returns 200 with []."""
    resp = session.get(f"{base_url}/api/offenders/{UNKNOWN_ID}/trail")
    assert resp.status_code == 404, (
        f"expected 404 for unknown offender's trail, got {resp.status_code}: {resp.text[:200]}"
    )


def test_create_offender_returns_201(session, base_url, unique_national_id):
    payload = {
        "firstName": "Api",
        "lastName": "Created",
        "nationalId": unique_national_id,
        "dateOfBirth": "1990-01-01",
        "riskLevel": "Low",
        "status": "Active",
    }
    resp = session.post(f"{base_url}/api/offenders", json=payload)
    assert resp.status_code == 201, f"expected 201, got {resp.status_code}: {resp.text[:200]}"
    created = resp.json()
    assert created["nationalId"] == unique_national_id

    # cleanup
    session.delete(f"{base_url}/api/offenders/{created['id']}")


def test_delete_offender_returns_204(session, base_url, unique_national_id):
    payload = {
        "firstName": "Api",
        "lastName": "Disposable",
        "nationalId": unique_national_id,
        "dateOfBirth": "1990-01-01",
        "riskLevel": "Low",
        "status": "Active",
    }
    created = session.post(f"{base_url}/api/offenders", json=payload).json()

    resp = session.delete(f"{base_url}/api/offenders/{created['id']}")
    assert resp.status_code == 204, f"expected 204, got {resp.status_code}: {resp.text[:200]}"
