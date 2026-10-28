"""The stats branch's CSV files: their schema and atomic reading and writing.

This is the lowest layer of .github/stats: it knows nothing about GitHub or about installs.
Standard library only.
"""

from __future__ import annotations

import csv
import io
import os
import tempfile
from datetime import date
from pathlib import Path

DOWNLOADS_FILE = "downloads.csv"
DOWNLOADS_COLUMNS = ("date", "tag", "asset", "download_count")
OWN_DOWNLOADS_CSV = "own-downloads.csv"
OWN_DOWNLOADS_COLUMNS = ("run_id", "date", "asset", "draft", "published")
TRAFFIC_FILE = "traffic.csv"
TRAFFIC_COLUMNS = ("date", "metric", "count", "uniques")
TRAFFIC_METRICS = ("views", "clones")

SHOWN_LENGTH = 40


class CollectError(Exception):
    """The run must fail: an API error, a corrupt stats file or an unusable response."""


def shown(value: object) -> str:
    """A value from outside, safe for a log line: escaped (no newline survives) and shortened."""
    text = ascii(value)
    return text if len(text) <= SHOWN_LENGTH else text[: SHOWN_LENGTH - 3] + "..."


def parse_day(text: object, error: type[Exception]) -> date:
    try:
        if not isinstance(text, str) or len(text) != 10:
            raise ValueError(text)
        return date.fromisoformat(text)
    except ValueError:
        raise error(f"{shown(text)} is not a YYYY-MM-DD date") from None


def csv_int(text: str) -> int:
    try:
        value = int(text)
    except ValueError:
        raise CollectError(f"{shown(text)} in a stats CSV is not an integer") from None
    if value < 0:
        raise CollectError(f"{shown(text)} in a stats CSV is negative")
    return value


def read_csv(path: Path, columns: tuple[str, ...]) -> list[dict[str, str]]:
    """The rows of a stats CSV; a missing file is empty, a foreign header is an error."""
    if not path.exists():
        return []
    with path.open(newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        if tuple(reader.fieldnames or ()) != columns:
            raise CollectError(f"{path.name} does not have the columns {', '.join(columns)}")
        rows = list(reader)
    if any(None in row or None in row.values() for row in rows):
        raise CollectError(f"{path.name} has a malformed row")
    return rows


def write_csv_if_changed(path: Path, columns: tuple[str, ...], rows: list[dict[str, str]]) -> bool:
    """Writes atomically and reports whether the file's content changed."""
    buffer = io.StringIO(newline="")
    writer = csv.DictWriter(buffer, fieldnames=columns, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    text = buffer.getvalue()
    if path.exists() and path.read_text(encoding="utf-8") == text:
        return False
    write_text_atomically(path, text)
    return True


def write_text_atomically(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle = tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="", dir=path.parent, delete=False, suffix=".tmp")
    try:
        with handle:
            handle.write(text)
        os.replace(handle.name, path)
    except BaseException:
        Path(handle.name).unlink(missing_ok=True)
        raise
