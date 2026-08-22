"""Deletes offenders created by this automation project (national IDs
starting with 'AUTO'). Run manually after a test session if you want to tidy
the shared demo environment; the suite does not need this to pass.

    python cleanup_test_data.py
"""
import requests

BASE_URL = "https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie"


def main():
    s = requests.Session()
    resp = s.get(f"{BASE_URL}/api/offenders", params={"pageSize": 200})
    resp.raise_for_status()
    items = resp.json()["items"]

    targets = [o for o in items if o["nationalId"].startswith("AUTO")]
    print(f"Found {len(targets)} automation-created offender(s) to delete.")
    for o in targets:
        d = s.delete(f"{BASE_URL}/api/offenders/{o['id']}")
        print(f"  id={o['id']} {o['firstName']} {o['lastName']} -> {d.status_code}")


if __name__ == "__main__":
    main()
