"""The domain of the download statistics: counts, own downloads and net installs. Pure, no I/O.

What a snapshot means: the cumulative download counts at the END of UTC day D, recorded
shortly after midnight under the date D (see snapshot_date_for). Own downloads of the
repository's release workflow carry the UTC date they happened on. A snapshot's installs are
therefore its counts minus the previous snapshot's, minus the own downloads dated in
(previous snapshot date, this snapshot date].
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import date, datetime, timedelta, timezone
from typing import Iterable, Mapping

from stats_csv import CollectError, csv_int, parse_day, shown

# Whether downloads of a DRAFT release raise download_count is measured on the first release
# (the open checkbox in the plan's PR A list). This default is provisional: that measurement
# confirms or flips it. Draft assets are counted in the raw totals either way, so a draft
# download lands in the same day's delta as its own-download record. installs_per_day()
# clamps at zero if drafts turn out not to count.
SUBTRACT_DRAFT_DOWNLOADS = True

SAFE_NAME_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,199}")
TIMESTAMP_DATE_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T")

# A run whose record lists no download at all is stored as one row with this asset name, so
# the run counts as known and its artifact is not downloaded again.
NO_ASSET = ""


class OwnRecordError(ValueError):
    """One own-downloads record does not follow the documented schema."""


@dataclass(frozen=True)
class AssetCount:
    tag: str
    asset: str
    count: int


@dataclass(frozen=True)
class OwnRecord:
    run_id: str
    day: date
    asset: str
    draft: int
    published: int


@dataclass(frozen=True)
class TrafficDay:
    day: date
    metric: str
    count: int
    uniques: int


def utc_date(moment: datetime) -> date:
    """The UTC calendar date of an aware moment; a naive one is ambiguous, so it is refused."""
    if moment.tzinfo is None:
        raise ValueError("a naive datetime has no time zone; pass an aware one")
    return moment.astimezone(timezone.utc).date()


def snapshot_date_for(run_date: date) -> date:
    """The run happens just after midnight and records the counts of the day that just ended.

    A manual run during the day also records the previous day; its counts then include part
    of the current day, which the next scheduled run corrects only by adding the rest to its own day.
    """
    return run_date - timedelta(days=1)


def _is_count(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


# --------------------------------------------------------------------------
#  JSON -> typed rows
# --------------------------------------------------------------------------

def asset_counts_from_releases(releases: object) -> tuple[list[AssetCount], list[str]]:
    """Cumulative counts of the assets of all releases, drafts included, plus warnings.

    Draft assets are counted because only the repository's own workflow downloads them, and
    those downloads are recorded as own downloads on the same day. Not for a dry run: it records
    nothing, because its draft is deleted. shortcut: a dry-run draft still listed by a snapshot
    (a failed cleanup, or a snapshot taken mid-run) counts as installs; attributing own downloads
    per tag would close it.
    """
    if not isinstance(releases, list):
        raise CollectError("the releases response is not a list")
    counts: list[AssetCount] = []
    warnings: list[str] = []
    for release in releases:
        if not isinstance(release, dict):
            continue
        tag = release.get("tag_name")
        assets = release.get("assets")
        if not isinstance(tag, str) or not isinstance(assets, list):
            raise CollectError("a release lacks tag_name or assets")
        if not SAFE_NAME_PATTERN.fullmatch(tag):
            warnings.append("skipped a release with an unexpected tag name")
            continue
        for asset in assets:
            name = asset.get("name") if isinstance(asset, dict) else None
            count = asset.get("download_count") if isinstance(asset, dict) else None
            if not isinstance(name, str) or not _is_count(count):
                raise CollectError(f"an asset of release {tag} lacks name or download_count")
            if not SAFE_NAME_PATTERN.fullmatch(name):
                warnings.append(f"skipped an asset of release {tag} with an unexpected name")
                continue
            counts.append(AssetCount(tag, name, count))
    return sorted(counts, key=lambda c: (c.tag, c.asset)), warnings


def own_records_from_json(document: object, expected_run_id: str | None = None) -> list[OwnRecord]:
    """Parses one own-downloads.json: {run_id, counts: {date: {asset: {draft, published}}}}.

    With expected_run_id, the record must name exactly that run.
    """
    if not isinstance(document, dict):
        raise OwnRecordError("the record is not an object")
    run_id = document.get("run_id")
    counts = document.get("counts")
    if not isinstance(run_id, str) or not SAFE_NAME_PATTERN.fullmatch(run_id):
        raise OwnRecordError("run_id is missing or not a plain string")
    if expected_run_id is not None and run_id != expected_run_id:
        raise OwnRecordError("run_id does not name the run that uploaded the artifact")
    if not isinstance(counts, dict):
        raise OwnRecordError("counts is not an object")
    records: list[OwnRecord] = []
    for day_text, assets in counts.items():
        day = parse_day(day_text, OwnRecordError)
        if not isinstance(assets, dict):
            raise OwnRecordError(f"counts for {shown(day_text)} is not an object")
        for asset, phases in assets.items():
            if not SAFE_NAME_PATTERN.fullmatch(asset):
                raise OwnRecordError("an asset name is not a plain file name")
            if not isinstance(phases, dict) or not _is_count(phases.get("draft")) or not _is_count(phases.get("published")):
                raise OwnRecordError(f"{shown(asset)} on {shown(day_text)} needs integer draft and published counts")
            records.append(OwnRecord(run_id, day, asset, phases["draft"], phases["published"]))
    return sorted(records, key=lambda r: (r.day, r.asset))


def traffic_from_json(metric: str, document: object) -> list[TrafficDay]:
    """Daily rows of a /traffic/views or /traffic/clones response."""
    entries = document.get(metric) if isinstance(document, dict) else None
    if not isinstance(entries, list):
        raise CollectError(f"the {metric} traffic response has no '{metric}' list")
    rows = []
    for entry in entries:
        timestamp = entry.get("timestamp") if isinstance(entry, dict) else None
        if not isinstance(timestamp, str) or not TIMESTAMP_DATE_PATTERN.match(timestamp):
            raise CollectError(f"a {metric} entry has no usable timestamp")
        count, uniques = entry.get("count"), entry.get("uniques")
        if not _is_count(count) or not _is_count(uniques):
            raise CollectError(f"a {metric} entry has no integer count and uniques")
        rows.append(TrafficDay(parse_day(timestamp[:10], CollectError), metric, count, uniques))
    return rows


# --------------------------------------------------------------------------
#  CSV rows -> typed rows
# --------------------------------------------------------------------------

def snapshots_from_rows(rows: Iterable[Mapping[str, str]]) -> dict[date, dict[tuple[str, str], int]]:
    """downloads.csv rows grouped as {date: {(tag, asset): cumulative count}}."""
    snapshots: dict[date, dict[tuple[str, str], int]] = {}
    for row in rows:
        day = parse_day(row["date"], CollectError)
        snapshots.setdefault(day, {})[(row["tag"], row["asset"])] = csv_int(row["download_count"])
    return snapshots


def own_records_from_rows(rows: Iterable[Mapping[str, str]]) -> list[OwnRecord]:
    return [
        OwnRecord(row["run_id"], parse_day(row["date"], CollectError), row["asset"], csv_int(row["draft"]), csv_int(row["published"]))
        for row in rows
    ]


# --------------------------------------------------------------------------
#  Merging into the stored rows
# --------------------------------------------------------------------------

def merge_snapshot(existing: list[dict[str, str]], counts: list[AssetCount], snapshot_day: date) -> list[dict[str, str]]:
    """Appends a snapshot only when a count differs from the latest one.

    A second run for the same snapshot date replaces that date's rows instead of duplicating them.
    """
    if not counts:
        return existing
    snapshots = snapshots_from_rows(existing)
    new_snapshot = {(c.tag, c.asset): c.count for c in counts}
    if snapshots and new_snapshot == snapshots[max(snapshots)]:
        return existing
    kept = [row for row in existing if row["date"] != snapshot_day.isoformat()]
    added = [
        {"date": snapshot_day.isoformat(), "tag": c.tag, "asset": c.asset, "download_count": str(c.count)}
        for c in counts
    ]
    return kept + added


def merge_own_records(existing: list[dict[str, str]], records: list[OwnRecord]) -> list[dict[str, str]]:
    """Appends the records of runs not stored yet. (Two artifacts naming one run are
    reduced to one by the fetcher, which knows where one artifact ends.)"""
    seen = {row["run_id"] for row in existing}
    added = [
        {"run_id": r.run_id, "date": r.day.isoformat(), "asset": r.asset, "draft": str(r.draft), "published": str(r.published)}
        for r in records
        if r.run_id not in seen
    ]
    return existing + added


def merge_traffic(existing: list[dict[str, str]], days: list[TrafficDay]) -> list[dict[str, str]]:
    """The API repeats the last 14 days on every call; the newest value of a day wins."""
    merged = {(row["date"], row["metric"]): row for row in existing}
    for item in days:
        merged[(item.day.isoformat(), item.metric)] = {
            "date": item.day.isoformat(), "metric": item.metric, "count": str(item.count), "uniques": str(item.uniques),
        }
    return [merged[key] for key in sorted(merged)]


# --------------------------------------------------------------------------
#  Net installs
# --------------------------------------------------------------------------

def snapshot_predecessors(snapshots: Mapping[date, object]) -> dict[date, date]:
    """For every snapshot date but the first, the date of the snapshot before it."""
    dates = sorted(snapshots)
    return dict(zip(dates[1:], dates))


def installs_per_day(
    snapshots: Mapping[date, Mapping[tuple[str, str], int]],
    own_records: Iterable[OwnRecord],
    subtract_drafts: bool = SUBTRACT_DRAFT_DOWNLOADS,
) -> dict[date, dict[str, int]]:
    """Net installs per snapshot date and asset name.

    The raw delta of a snapshot is its cumulative counts minus the previous snapshot's,
    summed over all releases carrying the asset. The workflow's own downloads dated after
    the previous snapshot up to this one are subtracted, clamped at zero. The first snapshot
    is the baseline and yields nothing. When snapshots are more than one day apart, the whole
    delta is booked on the later date; snapshot_predecessors() tells how far back it reaches.
    """
    records = list(own_records)
    return {
        current: _net_installs(snapshots[previous], snapshots[current], records, previous, current, subtract_drafts)
        for current, previous in snapshot_predecessors(snapshots).items()
    }


def _net_installs(previous_counts, current_counts, records, previous, current, subtract_drafts) -> dict[str, int]:
    raw: dict[str, int] = {}
    for (tag, asset), count in current_counts.items():
        raw[asset] = raw.get(asset, 0) + max(0, count - previous_counts.get((tag, asset), 0))
    own = own_downloads_between(records, previous, current, subtract_drafts)
    return {asset: max(0, delta - own.get(asset, 0)) for asset, delta in raw.items()}


def own_downloads_between(
    records: Iterable[OwnRecord], after: date, until: date, subtract_drafts: bool
) -> dict[str, int]:
    """Own downloads per asset dated in (after, until]."""
    totals: dict[str, int] = {}
    for record in records:
        if after < record.day <= until:
            counted = record.published + (record.draft if subtract_drafts else 0)
            totals[record.asset] = totals.get(record.asset, 0) + counted
    return totals
