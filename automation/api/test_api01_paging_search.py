"""API-01: GET /api/offenders supports search/page/pageSize and returns
consistent paging metadata."""
from conftest import ceil_div


def test_paging_metadata_is_consistent(session, base_url):
    """Known defect: BUG-001 — totalPages is computed with floor division
    instead of ceiling division, so it under-reports by one whenever total
    is not an exact multiple of pageSize. pageSize=5 is only chosen after
    checking the live total isn't currently a multiple of 5, so this test
    reliably exercises the bug regardless of how much seed data exists.
    """
    total = session.get(f"{base_url}/api/offenders", params={"pageSize": 1}).json()["total"]
    page_size = 5 if total % 5 != 0 else 4  # avoid an accidental exact-multiple total

    resp = session.get(f"{base_url}/api/offenders", params={"page": 1, "pageSize": page_size})
    assert resp.status_code == 200
    body = resp.json()

    for key in ("items", "total", "page", "pageSize", "totalPages"):
        assert key in body, f"missing '{key}' in paging response"

    assert body["page"] == 1
    assert body["pageSize"] == page_size
    assert len(body["items"]) <= body["pageSize"]
    assert body["totalPages"] == ceil_div(body["total"], body["pageSize"]), (
        f"totalPages={body['totalPages']} but ceil({body['total']}/{page_size}) = "
        f"{ceil_div(body['total'], page_size)} (looks like floor division is used server-side)"
    )


def test_search_is_partial_match(session, base_url):
    resp = session.get(f"{base_url}/api/offenders", params={"search": "Coh", "pageSize": 25})
    assert resp.status_code == 200
    body = resp.json()

    assert body["total"] >= 1
    assert any(o["lastName"] == "Cohen" for o in body["items"])


def test_search_is_case_insensitive(session, base_url):
    """FR-02 / API-01 — search must be case-insensitive.
    Known defect: BUG-013. Verified directly at the API: a lowercase
    substring returns 0 results while the same substring capitalized matches.
    """
    lower = session.get(f"{base_url}/api/offenders", params={"search": "coh", "pageSize": 25}).json()
    upper = session.get(f"{base_url}/api/offenders", params={"search": "COH", "pageSize": 25}).json()
    mixed = session.get(f"{base_url}/api/offenders", params={"search": "Coh", "pageSize": 25}).json()

    assert lower["total"] == mixed["total"], (
        f"case-insensitive search should return equal results regardless of casing; "
        f"got lower={lower['total']} vs mixed={mixed['total']}"
    )
    assert upper["total"] == mixed["total"]
