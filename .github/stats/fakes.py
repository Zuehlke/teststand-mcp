"""What the statistics tests share: the dates, rows and release fixtures, and a fake GitHub API.

Not a test module itself (its name does not start with test), so unittest discovery skips it."""

from __future__ import annotations

import io
import json
import zipfile
from datetime import date
from pathlib import Path

import collect
import net_installs
import stats_csv

REPO_ROOT = Path(__file__).resolve().parents[2]
OWN_DOWNLOADS_FIXTURE = REPO_ROOT / "build" / "tests" / "fixtures" / "own-downloads.json"
REPO = "owner/name"
ZIP = "teststand-mcp.zip"
HEADER = {name: ",".join(columns) + "\n" for name, columns in (
    ("downloads", stats_csv.DOWNLOADS_COLUMNS), ("own", stats_csv.OWN_DOWNLOADS_COLUMNS), ("traffic", stats_csv.TRAFFIC_COLUMNS))}


def d(text: str) -> date:
    return date.fromisoformat(text)


def own(run_id: str, day: str, asset: str, draft: int, published: int) -> net_installs.OwnRecord:
    return net_installs.OwnRecord(run_id, d(day), asset, draft, published)


def snapshots(*days: tuple[str, int]) -> dict:
    """Snapshots of one asset on one release, as (date, cumulative count)."""
    return {d(day): {("v1.9.0", ZIP): count} for day, count in days}


def release(tag: str, assets: dict[str, int], draft: bool = False) -> dict:
    return {"tag_name": tag, "draft": draft, "assets": [{"name": n, "download_count": c} for n, c in assets.items()]}


def artifact_zip(document: object, name: str = collect.OWN_DOWNLOADS_FILE) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as bundle:
        bundle.writestr(name, document if isinstance(document, str) else json.dumps(document))
    return buffer.getvalue()


def fixture_document() -> dict:
    return json.loads(OWN_DOWNLOADS_FIXTURE.read_text(encoding="utf-8"))


GOOD_RUN = {"path": ".github/workflows/release.yml", "event": "push"}


def artifact(run_id: int, expired: bool = False, **run_fields) -> dict:
    run = {"id": run_id, "repository_id": 7, "head_repository_id": 7, **run_fields}
    return {"id": run_id, "name": "own-downloads", "expired": expired, "workflow_run": run,
            "archive_download_url": f"https://api.example/artifacts/{run_id}/zip"}


def artifact_url(run_id: int) -> str:
    return f"https://api.example/artifacts/{run_id}/zip"


class FakeApi:
    """Canned API JSON by path and artifacts by URL. A failing path raises like the real client.

    A list endpoint (releases, artifacts) honours `page` and `per_page` like GitHub: a collector that stops
    after the first page, or reads one page too many, gets the wrong items from this fake."""

    def __init__(self, releases=(), artifacts=(), downloads=None, traffic=None, failing=(), runs=None):
        self.responses = {
            f"/repos/{REPO}/releases": list(releases),
            f"/repos/{REPO}/actions/artifacts": {"total_count": len(artifacts), "artifacts": list(artifacts)},
        }
        for item in artifacts:
            run_id = (item.get("workflow_run") or {}).get("id")
            self.responses[f"/repos/{REPO}/actions/runs/{run_id}"] = (runs or {}).get(run_id, GOOD_RUN)
        for metric, document in (traffic or {}).items():
            self.responses[f"/repos/{REPO}/traffic/{metric}"] = document
        self.downloads = downloads or {}
        self.failing = set(failing)
        self.requested: list[str] = []

    def get_json(self, path, params=None):
        self.requested.append(path)
        if path in self.failing:
            raise stats_csv.CollectError(f"GET {path}: HTTP 403 rate limit exceeded")
        return self._page(self.responses[path], params or {})

    @staticmethod
    def _page(response, params):
        start = (int(params.get("page", "1")) - 1) * int(params.get("per_page", str(collect.PAGE_SIZE)))
        stop = start + int(params.get("per_page", str(collect.PAGE_SIZE)))
        if isinstance(response, list):
            return response[start:stop]
        if isinstance(response, dict) and isinstance(response.get("artifacts"), list):
            return {**response, "artifacts": response["artifacts"][start:stop]}
        return response

    def download(self, url):
        self.requested.append(url)
        result = self.downloads[url]
        if isinstance(result, Exception):
            raise result
        return result
