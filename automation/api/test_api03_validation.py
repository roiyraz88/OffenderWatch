"""API-03: POST/PUT must enforce the same validation rules as the UI
(FR-03 for offenders, FR-10 for locations) and return 400 with an error
description for invalid payloads. Every test here cleans up any record that
is unexpectedly persisted, so a failing (defect-confirming) run does not
leave junk data behind.
"""
import pytest


def _cleanup(session, base_url, offender_id):
    if offender_id is not None:
        session.delete(f"{base_url}/api/offenders/{offender_id}")


@pytest.fixture
def disposable_offender(session, base_url, unique_national_id):
    """A throwaway offender for tests that must POST a location, so a
    signal/battery/coordinate value outside the valid range never pollutes a
    shared seeded offender (this is exactly what corrupted seed offender
    id=1 "Cohen" earlier — see BUG-007's evidence in the bug report).
    """
    created = session.post(
        f"{base_url}/api/offenders",
        json={
            "firstName": "Disposable",
            "lastName": f"Loc{unique_national_id}",
            "nationalId": unique_national_id,
            "dateOfBirth": "1990-01-01",
            "riskLevel": "Low",
            "status": "Active",
        },
    ).json()
    yield created["id"]
    session.delete(f"{base_url}/api/offenders/{created['id']}")


@pytest.mark.known_bug
def test_create_offender_rejects_empty_last_name(session, base_url, unique_national_id):
    """Known defect: BUG-002 — required-field validation missing (confirmed at API layer)."""
    payload = {
        "firstName": "Only",
        "lastName": "",
        "nationalId": unique_national_id,
        "dateOfBirth": "1990-01-01",
        "riskLevel": "Low",
        "status": "Active",
    }
    resp = session.post(f"{base_url}/api/offenders", json=payload)
    created_id = resp.json().get("id") if resp.ok else None
    try:
        assert resp.status_code == 400, (
            f"expected 400 for empty Last Name, got {resp.status_code}: {resp.text[:200]}"
        )
    finally:
        _cleanup(session, base_url, created_id)


@pytest.mark.known_bug
def test_create_offender_rejects_duplicate_national_id(session, base_url):
    """Known defect: BUG-014 — National ID uniqueness is not enforced (confirmed at API layer)."""
    payload = {
        "firstName": "Dup",
        "lastName": "Api",
        "nationalId": "305412876",  # belongs to seeded offender David Cohen
        "dateOfBirth": "1990-01-01",
        "riskLevel": "Low",
        "status": "Active",
    }
    resp = session.post(f"{base_url}/api/offenders", json=payload)
    created_id = resp.json().get("id") if resp.ok else None
    try:
        assert resp.status_code == 400, (
            f"expected 400 for duplicate National ID, got {resp.status_code}: {resp.text[:200]}"
        )
    finally:
        _cleanup(session, base_url, created_id)


@pytest.mark.known_bug
def test_create_offender_rejects_future_date_of_birth(session, base_url, unique_national_id):
    """Known defect: BUG-003 — future DOB is accepted (confirmed at API layer)."""
    payload = {
        "firstName": "Future",
        "lastName": "Api",
        "nationalId": unique_national_id,
        "dateOfBirth": "2099-01-01",
        "riskLevel": "Low",
        "status": "Active",
    }
    resp = session.post(f"{base_url}/api/offenders", json=payload)
    created_id = resp.json().get("id") if resp.ok else None
    try:
        assert resp.status_code == 400, (
            f"expected 400 for future DOB, got {resp.status_code}: {resp.text[:200]}"
        )
    finally:
        _cleanup(session, base_url, created_id)


@pytest.mark.known_bug
def test_create_offender_rejects_unsupported_risk_level(session, base_url, unique_national_id):
    """Known defect: unsupported enum values are accepted (part of BUG-008)."""
    payload = {
        "firstName": "Bad",
        "lastName": "Enum",
        "nationalId": unique_national_id,
        "dateOfBirth": "1990-01-01",
        "riskLevel": "Extreme",
        "status": "Active",
    }
    resp = session.post(f"{base_url}/api/offenders", json=payload)
    created_id = resp.json().get("id") if resp.ok else None
    try:
        assert resp.status_code == 400, (
            f"expected 400 for unsupported riskLevel, got {resp.status_code}: {resp.text[:200]}"
        )
    finally:
        _cleanup(session, base_url, created_id)


@pytest.mark.known_bug
@pytest.mark.parametrize(
    "field,value",
    [
        ("signal", 6),
        ("signal", 0),
        ("batteryPct", 101),
        ("batteryPct", -1),
        ("speedKmh", -5),
        ("lat", 200),
        ("lon", -200),
    ],
)
def test_add_location_rejects_out_of_range_values(session, base_url, field, value, disposable_offender):
    """Known defect: BUG-009 — location validation is not enforced (confirmed at API layer)."""
    payload = {
        "timestamp": "2026-01-01T00:00:00Z",
        "lat": 32.0,
        "lon": 34.0,
        "speedKmh": 10,
        "batteryPct": 50,
        "signal": 3,
    }
    payload[field] = value

    resp = session.post(f"{base_url}/api/offenders/{disposable_offender}/locations", json=payload)
    assert resp.status_code == 400, (
        f"expected 400 for {field}={value}, got {resp.status_code}: {resp.text[:200]}"
    )
