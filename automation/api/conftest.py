import math
import os
import time

import pytest
import requests

# Part 5 (Step 4 / TM-02) injects the target via OFFENDERWATCH_BASE_URL so
# the orchestrator can run this suite against any configured Environment's
# BaseUrlSnapshot. No hard-coded fallback: if it's missing, fail loudly at
# collection time rather than silently hitting the wrong (or no) target.
BASE_URL = os.environ.get("OFFENDERWATCH_BASE_URL")
if not BASE_URL:
    raise RuntimeError(
        "OFFENDERWATCH_BASE_URL environment variable is required (no "
        "hard-coded fallback). Set it to the target OffenderWatch base URL "
        "before running this suite, e.g.:\n"
        "  OFFENDERWATCH_BASE_URL=https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie "
        "pytest -v"
    )

# Part 5 (Step 4) structured event reporter — see ow_event_reporter.py.
# Auto-loaded for every invocation (standalone or platform-launched); it
# only ever prints extra "OW_EVENT|{...}" lines to stdout, so it doesn't
# change what a human sees when running this suite directly.
pytest_plugins = ["ow_event_reporter"]


@pytest.fixture(scope="session")
def base_url():
    return BASE_URL


@pytest.fixture(scope="session")
def session():
    s = requests.Session()
    s.headers.update({"Content-Type": "application/json"})
    yield s
    s.close()


@pytest.fixture
def unique_national_id():
    return f"AUTO{int(time.time() * 1000)}"


def ceil_div(total, page_size):
    return math.ceil(total / page_size) if page_size else 0
