import math
import time

import pytest
import requests

BASE_URL = "https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie"


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
