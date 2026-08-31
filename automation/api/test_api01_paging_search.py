"""API-01: GET /api/offenders supports search/page/pageSize and returns
consistent paging metadata."""
from conftest import ceil_div


def _pick_live_offender(session, base_url):
    """Picks a real, currently-existing offender to search for, instead of
    hardcoding a seeded name — some seeded offenders (e.g. Cohen, id=1) were
    permanently corrupted earlier by BUG-007's app-crashing invalid-signal
    input, so a hardcoded name can silently start failing for reasons
    unrelated to what this test actually checks.

    Skips offenders with an empty/degenerate last name (junk left on the
    shared demo app, e.g. from manually reproducing BUG-002) — an empty
    substring would match every offender regardless of casing, turning the
    case-sensitivity check below into a vacuous pass instead of a real one.
    """
    items = session.get(f"{base_url}/api/offenders", params={"pageSize": 100}).json()["items"]
    for o in items:
        if o.get("lastName") and len(o["lastName"]) >= 3 and o["lastName"].isalpha():
            return o
    raise AssertionError("no offender with a usable (non-empty, alphabetic) last name found")


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
    offender = _pick_live_offender(session, base_url)
    substring = offender["lastName"][: max(3, len(offender["lastName"]) // 2)]

    resp = session.get(f"{base_url}/api/offenders", params={"search": substring, "pageSize": 25})
    assert resp.status_code == 200
    body = resp.json()

    assert body["total"] >= 1, f"searching for '{substring}' (from {offender['lastName']}) found nothing"
    assert any(o["id"] == offender["id"] for o in body["items"])


def test_search_is_case_insensitive(session, base_url):
    """FR-02 / API-01 — search must be case-insensitive.
    Known defect: BUG-013. Verified directly at the API: a lowercase
    substring returns 0 results while the same substring capitalized matches.
    """
    offender = _pick_live_offender(session, base_url)
    substring = offender["lastName"][: max(3, len(offender["lastName"]) // 2)]

    lower = session.get(f"{base_url}/api/offenders", params={"search": substring.lower(), "pageSize": 25}).json()
    upper = session.get(f"{base_url}/api/offenders", params={"search": substring.upper(), "pageSize": 25}).json()
    mixed = session.get(f"{base_url}/api/offenders", params={"search": substring, "pageSize": 25}).json()

    assert mixed["total"] >= 1, f"sanity check failed: searching '{substring}' itself found nothing"
    assert lower["total"] == mixed["total"], (
        f"case-insensitive search should return equal results regardless of casing; "
        f"got lower={lower['total']} vs mixed={mixed['total']}"
    )
    assert upper["total"] == mixed["total"]
