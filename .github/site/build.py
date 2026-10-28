"""Builds the static download-stats page from the stats branch CSV files.

    python3 .github/site/build.py --stats-dir <stats worktree> --out <site dir> [--traffic-skipped]

The page is one self-contained index.html: no script, no external CSS, JS, font or image.
Charts are inline SVG with a title, a description and a direct label per series (the series
also differ in line style and marker, so colour is not the only cue), each followed by a
visible table of the daily values. Chart and table sit in keyboard-focusable scroll regions,
so the chart keeps its natural width and stays legible on a 320 px screen. A dark scheme
follows prefers-color-scheme. Net installs come from net_installs.installs_per_day().
Standard library only.
"""

from __future__ import annotations

import argparse
import html
import math
import re
import sys
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Callable, Iterable, Mapping

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "stats"))
import net_installs as domain  # noqa: E402
import stats_csv  # noqa: E402  (the CSV schema constants live there)

# The asset names of build/ReleaseAssets.psm1; test_site_build.py greps them out of it. They are the x86
# plugin's; the x64 plugin's insert build/PluginIdentity.psm1's NameSuffix after the prefix.
PLUGIN_ARCHIVE_NAME = "teststand-mcp.zip"
VERSIONED_ARCHIVE_FORMAT = "teststand-mcp-v{}.zip"
X64_NAME_SUFFIX = "-x64"
_PREFIX = PLUGIN_ARCHIVE_NAME.removesuffix(".zip")
X64_ARCHIVE_NAME = _PREFIX + X64_NAME_SUFFIX + ".zip"
_VERSIONED_PREFIX, _VERSIONED_SUFFIX = VERSIONED_ARCHIVE_FORMAT.split("{}")
# Either plugin's versioned archive: teststand-mcp-v1.9.0.zip or teststand-mcp-x64-v1.9.0.zip.
VERSIONED_ARCHIVE_PATTERN = re.compile(re.escape(_PREFIX) + f"(?:{re.escape(X64_NAME_SUFFIX)})?"
                                       + re.escape(_VERSIONED_PREFIX.removeprefix(_PREFIX)) + r"\d+\.\d+\.\d+" + re.escape(_VERSIONED_SUFFIX))

MAX_CHART_DAYS = 366
MAX_Y_TICKS = 5
MARKERS_UP_TO_DAYS = 45
LABEL_GAP = 16

WIDTH, HEIGHT = 760, 340
MARGIN_LEFT, MARGIN_RIGHT, MARGIN_TOP, MARGIN_BOTTOM = 44, 150, 16, 34
MARKER_RADIUS = 4

# WCAG contrast ratios of every text and series colour against the background it sits on,
# computed with the formula test_collect.py uses (it fails below 4.5):
#   light  text/bg 17.4  muted/bg 10.37  text/note 15.68  muted/note 9.34
#          latest 8.35  latest64 7.72  versioned 6.18  views 6.12  clones 7.34   (on bg)
#   dark   text/bg 15.86  muted/bg 10.74  text/note 14.11  muted/note 9.56
#          latest 8.41  latest64 8.65  versioned 9.01  views 9.83  clones 8.7    (on bg)
PALETTE = {
    "light": {"bg": "#ffffff", "text": "#1a1a1a", "muted": "#404040", "note": "#f3f3f3", "grid": "#c8c8c8", "border": "#8a8a8a",
              "latest": "#00508a", "latest64": "#5b3fa8", "versioned": "#a64200", "views": "#00704f", "clones": "#8e3568"},
    "dark": {"bg": "#121212", "text": "#ececec", "muted": "#c4c4c4", "note": "#1e1e1e", "grid": "#444444", "border": "#8a8a8a",
             "latest": "#6cb4f0", "latest64": "#b9a4f5", "versioned": "#f0a35e", "views": "#5fd0a0", "clones": "#e59ac4"},
}
TEXT_PAIRS = (("text", "bg"), ("muted", "bg"), ("text", "note"), ("muted", "note"),
              ("latest", "bg"), ("latest64", "bg"), ("versioned", "bg"), ("views", "bg"), ("clones", "bg"))


@dataclass(frozen=True)
class SeriesStyle:
    label: str
    key: str  # the palette entry and the CSS class suffix
    dash: str
    marker: str


@dataclass(frozen=True)
class Series:
    style: SeriesStyle
    values: Mapping[date, int]


@dataclass(frozen=True)
class Chart:
    figure_id: str
    title: str
    unit: str
    days: list[date]
    series: list[Series]
    since: Mapping[date, date] = field(default_factory=dict)  # a value that covers several days


INSTALL_STYLES = {
    "latest": SeriesStyle(PLUGIN_ARCHIVE_NAME, "latest", "", "circle"),
    "latest64": SeriesStyle(X64_ARCHIVE_NAME, "latest64", "6 2 1 2", "down"),
    "versioned": SeriesStyle("Versioned archives", "versioned", "8 4", "square"),
}
TRAFFIC_STYLES = {
    "views": SeriesStyle("Views", "views", "2 3", "triangle"),
    "clones": SeriesStyle("Clones", "clones", "10 3 2 3", "diamond"),
}


# --------------------------------------------------------------------------
#  Pure: CSV data -> series
# --------------------------------------------------------------------------

def install_series(installs: Mapping[date, Mapping[str, int]]) -> list[Series]:
    """Net installs per day: each plugin's latest-archive asset, and every versioned archive of both summed."""
    latest = {day: assets.get(PLUGIN_ARCHIVE_NAME, 0) for day, assets in installs.items()}
    latest64 = {day: assets.get(X64_ARCHIVE_NAME, 0) for day, assets in installs.items()}
    versioned = {
        day: sum(count for asset, count in assets.items() if VERSIONED_ARCHIVE_PATTERN.fullmatch(asset))
        for day, assets in installs.items()
    }
    return [Series(INSTALL_STYLES["latest"], latest), Series(INSTALL_STYLES["latest64"], latest64), Series(INSTALL_STYLES["versioned"], versioned)]


def traffic_series(rows: Iterable[Mapping[str, str]]) -> list[Series]:
    values: dict[str, dict[date, int]] = {metric: {} for metric in stats_csv.TRAFFIC_METRICS}
    for row in rows:
        if row["metric"] in values:
            values[row["metric"]][stats_csv.parse_day(row["date"], stats_csv.CollectError)] = stats_csv.csv_int(row["count"])
    return [Series(TRAFFIC_STYLES[metric], values[metric]) for metric in stats_csv.TRAFFIC_METRICS]


def chart_days(series: list[Series], last_day: date) -> list[date]:
    """Every day from the first data point to last_day (or the last data point, if later),
    bounded to the last MAX_CHART_DAYS."""
    recorded = [day for item in series for day in item.values]
    if not recorded:
        return []
    last = max(max(recorded), last_day)
    first = max(min(recorded), last - timedelta(days=MAX_CHART_DAYS - 1))
    return [first + timedelta(days=offset) for offset in range((last - first).days + 1)]


def multi_day_values(snapshots: Mapping[date, object]) -> dict[date, date]:
    """Snapshot dates whose installs cover more than one day, with the date they reach back to."""
    return {day: previous for day, previous in domain.snapshot_predecessors(snapshots).items() if (day - previous).days > 1}


# --------------------------------------------------------------------------
#  Pure: SVG
# --------------------------------------------------------------------------

def esc(text: object) -> str:
    return html.escape(str(text), quote=True)


def y_ticks(maximum: int) -> list[int]:
    """Integer ticks from 0 with at most MAX_Y_TICKS steps (a 1-2-5 progression)."""
    top = max(maximum, 1)
    for magnitude in (10 ** power for power in range(0, 12)):
        for factor in (1, 2, 5):
            step = factor * magnitude
            if math.ceil(top / step) <= MAX_Y_TICKS:
                return list(range(0, math.ceil(top / step) * step + 1, step))
    raise ValueError("count too large for the chart")


def spread_labels(wanted: list[float], low: float, high: float) -> list[float]:
    """Moves label baselines apart so that no two are closer than LABEL_GAP."""
    order = sorted(range(len(wanted)), key=lambda index: wanted[index])
    placed = [0.0] * len(wanted)
    previous = low - LABEL_GAP
    for index in order:
        previous = max(wanted[index], previous + LABEL_GAP)
        placed[index] = previous
    overflow = max(placed, default=low) - high
    return [position - overflow for position in placed] if overflow > 0 else placed


def marker_svg(style: SeriesStyle, x: float, y: float) -> str:
    r = MARKER_RADIUS
    css = f'class="marker s-{style.key}"'
    if style.marker == "circle":
        return f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" {css}/>'
    if style.marker == "square":
        return f'<rect x="{x - r:.1f}" y="{y - r:.1f}" width="{2 * r}" height="{2 * r}" {css}/>'
    outline = {
        "triangle": [(x, y - r - 1), (x + r + 1, y + r), (x - r - 1, y + r)],
        "diamond": [(x, y - r - 1), (x + r + 1, y), (x, y + r + 1), (x - r - 1, y)],
        "down": [(x, y + r + 1), (x + r + 1, y - r), (x - r - 1, y - r)],
    }[style.marker]
    points = " ".join(f"{px:.1f},{py:.1f}" for px, py in outline)
    return f'<polygon points="{points}" {css}/>'


def describe(chart: Chart) -> str:
    """The text alternative of the chart: range, per-series totals and peaks, multi-day values."""
    parts = [f"Line chart of {chart.unit} from {chart.days[0]} to {chart.days[-1]}."]
    for item in chart.series:
        total = sum(item.values.values())
        peak_day = max(item.values, key=lambda day: (item.values[day], day), default=None)
        peak = f", highest {item.values[peak_day]} on {peak_day}" if peak_day and item.values[peak_day] else ""
        parts.append(f"{item.style.label}: {total} in total{peak}.")
    shown_since = [f"{day} since {previous}" for day, previous in sorted(chart.since.items()) if day in chart.days]
    if shown_since:
        parts.append("Some values cover several days: " + ", ".join(shown_since) + ".")
    parts.append("A table with every daily value follows the chart.")
    return " ".join(parts)


def render_chart(chart: Chart) -> str:
    plot_w = WIDTH - MARGIN_LEFT - MARGIN_RIGHT
    plot_h = HEIGHT - MARGIN_TOP - MARGIN_BOTTOM
    ticks = y_ticks(max((v for s in chart.series for v in s.values.values()), default=0))
    top = ticks[-1]
    count = len(chart.days)

    def x_of(index: int) -> float:
        return MARGIN_LEFT + (plot_w * index / (count - 1) if count > 1 else plot_w / 2)

    def y_of(value: int) -> float:
        return MARGIN_TOP + plot_h * (1 - value / top)

    ident = esc(chart.figure_id)
    out = [
        f'<svg role="img" aria-labelledby="{ident}-title {ident}-desc" viewBox="0 0 {WIDTH} {HEIGHT}" width="{WIDTH}" height="{HEIGHT}" class="chart">',
        f'<title id="{ident}-title">{esc(chart.title)}</title>',
        f'<desc id="{ident}-desc">{esc(describe(chart))}</desc>',
    ]
    for tick in ticks:
        y = y_of(tick)
        out.append(f'<line x1="{MARGIN_LEFT}" x2="{WIDTH - MARGIN_RIGHT}" y1="{y:.1f}" y2="{y:.1f}" class="grid"/>')
        out.append(f'<text x="{MARGIN_LEFT - 6}" y="{y + 4:.1f}" text-anchor="end" class="tick">{tick}</text>')
    for index in sorted({0, (count - 1) // 2, count - 1}):
        anchor = "start" if index == 0 and count > 1 else "end" if index == count - 1 and count > 1 else "middle"
        out.append(f'<text x="{x_of(index):.1f}" y="{HEIGHT - 10}" text-anchor="{anchor}" class="tick">{chart.days[index]}</text>')

    label_wanted = [y_of(item.values.get(chart.days[-1], 0)) + 4 for item in chart.series]
    label_y = spread_labels(label_wanted, MARGIN_TOP + 10, HEIGHT - MARGIN_BOTTOM)
    for item, text_y in zip(chart.series, label_y):
        style = item.style
        points = [(x_of(i), y_of(item.values.get(day, 0))) for i, day in enumerate(chart.days)]
        dash = f' stroke-dasharray="{style.dash}"' if style.dash else ""
        path = " ".join(f"{x:.1f},{y:.1f}" for x, y in points)
        out.append(f'<polyline points="{path}" class="line s-{style.key}"{dash}/>')
        shown = points if count <= MARKERS_UP_TO_DAYS else points[-1:]
        out.extend(marker_svg(style, x, y) for x, y in shown)
        out.append(f'<text x="{x_of(count - 1) + 10:.1f}" y="{text_y:.1f}" class="label s-{style.key}">{esc(style.label)}</text>')
    out.append("</svg>")
    return "\n".join(out)


def render_table(chart: Chart) -> str:
    head = "".join(f'<th scope="col">{esc(item.style.label)}</th>' for item in chart.series)

    def day_cell(day: date) -> str:
        note = f" (since {chart.since[day]})" if day in chart.since else ""
        return f'<th scope="row">{day}{note}</th>'

    rows = "\n".join(
        f"<tr>{day_cell(day)}" + "".join(f"<td>{item.values.get(day, 0)}</td>" for item in chart.series) + "</tr>"
        for day in chart.days
    )
    return (
        f"<table>\n<caption>{esc(chart.title)}: daily values</caption>\n"
        f'<thead><tr><th scope="col">Date (UTC)</th>{head}</tr></thead>\n<tbody>\n{rows}\n</tbody>\n</table>'
    )


# --------------------------------------------------------------------------
#  Pure: the page
# --------------------------------------------------------------------------

def _variables(colors: Mapping[str, str]) -> str:
    return " ".join(f"--{name}: {value};" for name, value in colors.items())


STYLE = f"""
:root {{ color-scheme: light dark; {_variables(PALETTE['light'])} }}
@media (prefers-color-scheme: dark) {{ :root {{ {_variables(PALETTE['dark'])} }} }}
body {{ margin: 0; background: var(--bg); color: var(--text); font: 16px/1.5 system-ui, sans-serif; }}
main {{ max-width: 52rem; margin: 0 auto; padding: 1rem; }}
h1 {{ font-size: 1.6rem; }} h2 {{ font-size: 1.2rem; margin-top: 2rem; }}
figure {{ margin: 0; }}
.scroll {{ overflow-x: auto; }}
.scroll:focus-visible {{ outline: 3px solid var(--text); outline-offset: 2px; }}
.chart {{ display: block; }}
.grid {{ stroke: var(--grid); stroke-width: 1; }}
.tick {{ font-size: 11px; fill: var(--muted); }}
.label {{ font-size: 12px; font-weight: 600; }}
.s-latest {{ --c: var(--latest); }} .s-latest64 {{ --c: var(--latest64); }} .s-versioned {{ --c: var(--versioned); }}
.s-views {{ --c: var(--views); }} .s-clones {{ --c: var(--clones); }}
.line {{ fill: none; stroke: var(--c); stroke-width: 2; }}
.marker {{ fill: var(--c); stroke: var(--bg); stroke-width: 1; }}
text.label {{ fill: var(--c); }}
.note {{ border-left: 4px solid var(--muted); padding: 0.25rem 0.75rem; background: var(--note); }}
table {{ border-collapse: collapse; margin-top: 1rem; }}
caption {{ text-align: left; font-weight: 600; padding-bottom: 0.25rem; }}
th, td {{ border: 1px solid var(--border); padding: 0.15rem 0.6rem; text-align: right; }}
th[scope=row], thead th:first-child {{ text-align: left; }}
footer {{ margin-top: 2rem; font-size: 0.9rem; color: var(--muted); }}
"""


def scroll_region(label: str, content: str) -> str:
    return f'<div class="scroll" tabindex="0" role="region" aria-label="{esc(label)}">\n{content}\n</div>'


def render_section(heading: str, chart: Chart | None, empty_message: str) -> str:
    if chart is None:
        return f'<section>\n<h2>{esc(heading)}</h2>\n<p class="note">{esc(empty_message)}</p>\n</section>'
    return (
        f"<section>\n<h2>{esc(heading)}</h2>\n"
        f"<figure>\n{scroll_region(chart.title + ' (chart)', render_chart(chart))}\n</figure>\n"
        f"{scroll_region(chart.title + ': daily values (table)', render_table(chart))}\n</section>"
    )


def render_page(installs: Chart | None, traffic: Chart | None, traffic_skipped: bool, data_through: date | None,
                installs_empty: str, subtract_drafts: bool) -> str:
    if traffic_skipped:
        traffic_section = render_section(
            "Repository traffic", None,
            "Traffic (views and clones) is not recorded: the optional STATS_TRAFFIC_TOKEN secret is not set, so GitHub's traffic API was skipped.")
    else:
        traffic_section = render_section("Repository traffic", traffic, "No views or clones have been recorded yet.")
    drafts = "including" if subtract_drafts else "excluding"
    through = f"Data through {data_through} (UTC)." if data_through else "No data has been collected yet."
    return (
        '<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
        '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src \'unsafe-inline\'">\n'
        f"<title>TestStand MCP plugin: download statistics</title>\n<style>{STYLE}</style>\n</head>\n<body>\n<main>\n"
        "<h1>TestStand MCP plugin: download statistics</h1>\n"
        f"<p>Installs per day are the growth of the release assets' cumulative download counts between two daily snapshots (each taken at the end of a UTC day), "
        f"minus the downloads made by this repository's own release workflow ({drafts} its verification downloads of the draft release). {esc(through)}</p>\n"
        f'{render_section("Installs per day", installs, installs_empty)}\n{traffic_section}\n'
        "<footer>Generated from the <code>stats</code> branch by the pages workflow.</footer>\n</main>\n</body>\n</html>\n"
    )


def build_page(downloads: list[dict[str, str]], own: list[dict[str, str]], traffic: list[dict[str, str]],
               run_date: date, traffic_skipped: bool, subtract_drafts: bool) -> str:
    last_day = domain.snapshot_date_for(run_date)
    snapshots = domain.snapshots_from_rows(downloads)
    installs = domain.installs_per_day(snapshots, domain.own_records_from_rows(own), subtract_drafts)
    install_items = install_series(installs)
    install_days = chart_days(install_items, last_day)
    traffic_items = traffic_series(traffic)
    traffic_days = chart_days(traffic_items, last_day)

    if not snapshots:
        installs_empty = "No download snapshot has been recorded yet. The first daily run creates it."
    else:
        installs_empty = "The first snapshot is only a baseline. Installs per day appear once a later snapshot differs from it."
    installs_chart = (Chart("installs", "Installs per day", "net installs per day", install_days, install_items, multi_day_values(snapshots))
                      if install_days else None)
    traffic_chart = Chart("traffic", "Repository traffic per day", "views and clones per day", traffic_days, traffic_items) if traffic_days else None
    through = max([*snapshots, *(day for item in traffic_items for day in item.values)], default=None)
    return render_page(installs_chart, traffic_chart, traffic_skipped, through, installs_empty, subtract_drafts)


# --------------------------------------------------------------------------
#  I/O
# --------------------------------------------------------------------------

def build_site(stats_dir: Path, out_dir: Path, run_date: date, traffic_skipped: bool, subtract_drafts: bool) -> Path:
    page = build_page(
        stats_csv.read_csv(stats_dir / stats_csv.DOWNLOADS_FILE, stats_csv.DOWNLOADS_COLUMNS),
        stats_csv.read_csv(stats_dir / stats_csv.OWN_DOWNLOADS_CSV, stats_csv.OWN_DOWNLOADS_COLUMNS),
        stats_csv.read_csv(stats_dir / stats_csv.TRAFFIC_FILE, stats_csv.TRAFFIC_COLUMNS),
        run_date, traffic_skipped, subtract_drafts,
    )
    target = out_dir / "index.html"
    stats_csv.write_text_atomically(target, page)
    return target


def main(argv: list[str] | None = None, clock: Callable[[], datetime] = lambda: datetime.now(timezone.utc)) -> int:
    parser = argparse.ArgumentParser(description="Build the download-stats page.")
    parser.add_argument("--stats-dir", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--traffic-skipped", action="store_true", help="say on the page that traffic is not recorded")
    parser.add_argument("--subtract-drafts", action=argparse.BooleanOptionalAction, default=domain.SUBTRACT_DRAFT_DOWNLOADS,
                        help="subtract the workflow's draft downloads as well as the published ones")
    parser.add_argument("--today", help="override the run date (YYYY-MM-DD); the chart ends the day before")
    args = parser.parse_args(argv)
    try:
        run_date = stats_csv.parse_day(args.today, stats_csv.CollectError) if args.today else domain.utc_date(clock())
        target = build_site(args.stats_dir, args.out, run_date, args.traffic_skipped, args.subtract_drafts)
    except stats_csv.CollectError as error:
        print(f"build failed: {error}", file=sys.stderr)
        return 1
    print(f"wrote {target}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
