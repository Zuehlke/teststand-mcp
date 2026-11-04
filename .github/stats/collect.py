"""Collects release download counts, own-download records and repository traffic.

Run daily, just after midnight UTC, by .github/workflows/pages.yml. It writes three CSV files
in the `stats` branch worktree:

    downloads.csv      cumulative download_count of every release asset at the end of the
                       previous UTC day, one snapshot per date, appended only when a count changed
    own-downloads.csv  downloads made by the repository's own release workflow, taken from
                       the `own-downloads` artifacts of its runs (see OWN_DOWNLOADS_ARTIFACT)
    traffic.csv        daily views and clones; only with the optional traffic token

Layers: stats_csv (schema, atomic files), net_installs (pure domain), github_api (HTTP client);
this module is the orchestration and the command line. build.py derives the net installs.
Standard library only. An API error on the releases, the artifact list or the traffic exits
non-zero before a file is written; one unusable own-downloads artifact is only a warning.

Run the tests from the repository root with
    python -m unittest discover -s .github/stats
or from .github/stats with
    python -m unittest
"""

from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys
import zipfile
from dataclasses import dataclass, field
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Callable, Mapping

import net_installs as domain
from github_api import MAX_ARTIFACT_BYTES, GitHubApi, GitHubClient
from stats_csv import (
    DOWNLOADS_COLUMNS, DOWNLOADS_FILE, OWN_DOWNLOADS_COLUMNS, OWN_DOWNLOADS_CSV, TRAFFIC_COLUMNS,
    TRAFFIC_FILE, TRAFFIC_METRICS, CollectError, parse_day, read_csv, write_csv_if_changed,
)

PAGE_SIZE = 100
MAX_PAGES = 10

# The artifact contract with release.yml: an artifact of this name holds this file, whose
# schema is documented in docs/specs/plugin-install-and-release-workflow.md (AC-18).
OWN_DOWNLOADS_ARTIFACT = "own-downloads"
OWN_DOWNLOADS_FILE = "own-downloads.json"
RELEASE_WORKFLOW_PATH = ".github/workflows/release.yml"
OWN_RUN_EVENTS = ("push", "workflow_dispatch")

REPO_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*")


@dataclass
class CollectResult:
    changed_files: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


# --------------------------------------------------------------------------
#  Fetching
# --------------------------------------------------------------------------

def fetch_pages(api: GitHubApi, path: str, extract: Callable[[object], list], params: Mapping[str, str] | None = None) -> list:
    """All items of a paginated list endpoint, bounded by MAX_PAGES."""
    items: list = []
    for page in range(1, MAX_PAGES + 1):
        query = {**(params or {}), "per_page": str(PAGE_SIZE), "page": str(page)}
        batch = extract(api.get_json(path, query))
        items.extend(batch)
        if len(batch) < PAGE_SIZE:
            return items
    raise CollectError(f"{path} has more than {MAX_PAGES * PAGE_SIZE} entries; raise MAX_PAGES")


def _release_list(response: object) -> list:
    if not isinstance(response, list):
        raise CollectError("the releases response is not a list")
    return response


def _artifact_list(response: object) -> list:
    artifacts = response.get("artifacts") if isinstance(response, dict) else None
    if not isinstance(artifacts, list):
        raise CollectError("the artifacts response has no 'artifacts' list")
    return artifacts


def provenance_problem(artifact_run: Mapping[str, object], run: object) -> str | None:
    """Why an own-downloads artifact must not be trusted, or None when it comes from a run of
    release.yml of this very repository (not a fork) started by a push or by hand.

    The default branch is deliberately not checked: release runs are tag pushes.
    """
    repository_id, head_repository_id = artifact_run.get("repository_id"), artifact_run.get("head_repository_id")
    if repository_id is None or repository_id != head_repository_id:
        return "its run comes from another repository"
    if not isinstance(run, dict):
        return "its run could not be read"
    path = run.get("path")
    if not isinstance(path, str) or path.split("@", 1)[0] != RELEASE_WORKFLOW_PATH:
        return "its run is not a run of release.yml"
    if run.get("event") not in OWN_RUN_EVENTS:
        return "its run was not started by a push or by hand"
    return None


def _read_record(archive: bytes) -> object:
    try:
        with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
            info = bundle.getinfo(OWN_DOWNLOADS_FILE)
            if info.file_size > MAX_ARTIFACT_BYTES:
                raise domain.OwnRecordError(f"{OWN_DOWNLOADS_FILE} is too large")
            return json.loads(bundle.read(info))
    except domain.OwnRecordError:
        raise
    except (zipfile.BadZipFile, KeyError, ValueError, RecursionError):
        raise domain.OwnRecordError(f"the artifact does not hold a readable {OWN_DOWNLOADS_FILE}") from None


def fetch_own_records(api: GitHubApi, repo: str, known_run_ids: set[str], run_date: date, warnings: list[str]) -> list[domain.OwnRecord]:
    """Records of the own-downloads artifacts of runs not recorded yet. Zero artifacts is fine.

    Only the artifact listing can fail the run. Whatever goes wrong with one artifact (the run
    lookup, the download, an oversized or malformed record, a failed provenance check) is a
    warning, and the artifact is skipped.
    """
    artifacts = fetch_pages(api, f"/repos/{repo}/actions/artifacts", _artifact_list, {"name": OWN_DOWNLOADS_ARTIFACT})
    seen = set(known_run_ids)
    runs: dict[str, object] = {}
    records: list[domain.OwnRecord] = []
    for artifact in artifacts:
        if not isinstance(artifact, dict) or artifact.get("expired") is True:
            continue
        artifact_run = artifact.get("workflow_run")
        run_id = str(artifact_run.get("id")) if isinstance(artifact_run, dict) else ""
        if not domain.SAFE_NAME_PATTERN.fullmatch(run_id):
            warnings.append(f"skipped an {OWN_DOWNLOADS_ARTIFACT} artifact without a usable workflow run")
            continue
        if run_id in seen:
            continue
        try:
            if run_id not in runs:
                runs[run_id] = api.get_json(f"/repos/{repo}/actions/runs/{run_id}")
            problem = provenance_problem(artifact_run, runs[run_id])
            if problem:
                warnings.append(f"skipped an {OWN_DOWNLOADS_ARTIFACT} artifact: {problem}")
                continue
            url = artifact.get("archive_download_url")
            if not isinstance(url, str):
                raise CollectError("the artifact has no archive_download_url")
            parsed = domain.own_records_from_json(_read_record(api.download(url)), expected_run_id=run_id)
        except (CollectError, domain.OwnRecordError) as error:
            warnings.append(f"skipped an unusable {OWN_DOWNLOADS_ARTIFACT} artifact: {error}")
            continue
        seen.add(run_id)
        records.extend(parsed or [domain.OwnRecord(run_id, run_date, domain.NO_ASSET, 0, 0)])
    return records


def fetch_traffic(api: GitHubApi, repo: str) -> list[domain.TrafficDay]:
    days: list[domain.TrafficDay] = []
    for metric in TRAFFIC_METRICS:
        days.extend(domain.traffic_from_json(metric, api.get_json(f"/repos/{repo}/traffic/{metric}")))
    return days


# --------------------------------------------------------------------------
#  Orchestration
# --------------------------------------------------------------------------

def collect(api: GitHubApi, traffic_api: GitHubApi | None, repo: str, stats_dir: Path, run_date: date) -> CollectResult:
    """Fetches everything first, then writes; any failing fetch leaves the stats files untouched.

    The snapshot is dated the previous UTC day (see net_installs.snapshot_date_for).
    """
    result = CollectResult()
    downloads = read_csv(stats_dir / DOWNLOADS_FILE, DOWNLOADS_COLUMNS)
    own = read_csv(stats_dir / OWN_DOWNLOADS_CSV, OWN_DOWNLOADS_COLUMNS)
    traffic = read_csv(stats_dir / TRAFFIC_FILE, TRAFFIC_COLUMNS)

    releases = fetch_pages(api, f"/repos/{repo}/releases", _release_list)
    counts, warnings = domain.asset_counts_from_releases(releases)
    result.warnings.extend(warnings)
    new_own = fetch_own_records(api, repo, {row["run_id"] for row in own}, run_date, result.warnings)
    new_traffic = fetch_traffic(traffic_api, repo) if traffic_api else []

    outputs = [
        (DOWNLOADS_FILE, DOWNLOADS_COLUMNS, domain.merge_snapshot(downloads, counts, domain.snapshot_date_for(run_date))),
        (OWN_DOWNLOADS_CSV, OWN_DOWNLOADS_COLUMNS, domain.merge_own_records(own, new_own)),
        (TRAFFIC_FILE, TRAFFIC_COLUMNS, domain.merge_traffic(traffic, new_traffic)),
    ]
    for name, columns, rows in outputs:
        if write_csv_if_changed(stats_dir / name, columns, rows):
            result.changed_files.append(name)
    return result


def main(argv: list[str] | None = None, env: Mapping[str, str] | None = None,
         clock: Callable[[], datetime] = lambda: datetime.now(timezone.utc),
         client_factory: Callable[[str], GitHubApi] = GitHubClient) -> int:
    env = os.environ if env is None else env
    parser = argparse.ArgumentParser(description="Collect release download counts into the stats CSV files.")
    parser.add_argument("--stats-dir", type=Path, required=True, help="the checked-out stats branch")
    parser.add_argument("--repo", default=env.get("GITHUB_REPOSITORY", ""), help="owner/name; default $GITHUB_REPOSITORY")
    parser.add_argument("--today", help="override the run date (YYYY-MM-DD); the snapshot is dated the day before")
    args = parser.parse_args(argv)

    token = env.get("GITHUB_TOKEN", "").strip()
    traffic_token = env.get("STATS_TRAFFIC_TOKEN", "").strip()
    try:
        if not REPO_PATTERN.fullmatch(args.repo):
            raise CollectError("--repo or GITHUB_REPOSITORY must look like owner/name")
        if not token:
            raise CollectError("GITHUB_TOKEN is not set")
        run_date = parse_day(args.today, CollectError) if args.today else domain.utc_date(clock())
        api = client_factory(token)
        traffic_api = client_factory(traffic_token) if traffic_token else None
        result = collect(api, traffic_api, args.repo, args.stats_dir, run_date)
    except CollectError as error:
        print(f"collect failed: {error}", file=sys.stderr)
        return 1

    for warning in result.warnings:
        print(f"warning: {warning}", file=sys.stderr)
    if not traffic_token:
        print("traffic skipped: STATS_TRAFFIC_TOKEN is not set")
    print(f"changed: {', '.join(result.changed_files) or 'nothing'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
