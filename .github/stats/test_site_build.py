"""Tests for the statistics page builder (.github/site/build.py): the series, the markup and the command."""

from __future__ import annotations

import importlib.util
import io
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from datetime import datetime, timedelta, timezone
from html.parser import HTMLParser
from pathlib import Path

import stats_csv
from fakes import HEADER, REPO_ROOT, ZIP, d

SITE_BUILDER = Path(__file__).resolve().parents[1] / "site" / "build.py"
_spec = importlib.util.spec_from_file_location("stats_site_build", SITE_BUILDER)
site = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = site  # dataclasses resolves annotations through sys.modules
_spec.loader.exec_module(site)


# --------------------------------------------------------------------------
#  The site builder
# --------------------------------------------------------------------------

class PageParts(HTMLParser):
    """Collects what the accessibility contract talks about, from the real markup."""

    def __init__(self, page: str):
        super().__init__()
        self.order: list[str] = []
        self.svgs: list[dict] = []
        self.texts: dict[str, str] = {}
        self.direct_labels: list[str] = []
        self.polylines: list[dict] = []
        self.markers: list[str] = []
        self.regions: list[dict] = []
        self.tags: set[str] = set()
        self.table_rows: list[list[str]] = []
        self.column_headers: list[str] = []
        self.external: list[str] = []
        self._open: list[tuple[str, dict]] = []
        self._cell: list[str] | None = None
        self._cell_scope: str | None = None
        self.feed(page)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        self._open.append((tag, attrs))
        self.tags.add(tag)
        if tag in ("svg", "table", "figure"):
            self.order.append(tag)
        if tag == "svg":
            self.svgs.append(attrs)
        if tag == "polyline":
            self.polylines.append(attrs)
        if tag in ("circle", "rect", "polygon") and "marker" in attrs.get("class", "").split():
            self.markers.append(attrs["class"])
        if tag == "div" and attrs.get("class") == "scroll":
            self.regions.append(attrs)
        if tag == "tr":
            self.table_rows.append([])
        if tag in ("td", "th"):
            self._cell = []
            self._cell_scope = attrs.get("scope")
        for name in ("src", "href"):
            if name in attrs:
                self.external.append(attrs[name])
        if tag in ("script", "link", "img", "iframe"):
            self.external.append(f"<{tag}>")

    def handle_endtag(self, tag):
        if tag in ("td", "th") and self._cell is not None:
            text = "".join(self._cell)
            self.table_rows[-1].append(text)
            if self._cell_scope == "col":
                self.column_headers.append(text)
            self._cell = None
        while self._open:
            if self._open.pop()[0] == tag:
                break

    def handle_data(self, data):
        if not self._open:
            return
        tag, attrs = self._open[-1]
        if self._cell is not None:
            self._cell.append(data)
        if tag in ("title", "desc") and "id" in attrs:
            self.texts[attrs["id"]] = self.texts.get(attrs["id"], "") + data
        if tag == "text" and "label" in attrs.get("class", "").split():
            self.direct_labels.append(data)


def csv_text(header: str, *rows: str) -> str:
    return header + "".join(row + "\n" for row in rows)


def write_stats(directory: Path, downloads="", own="", traffic="") -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "downloads.csv").write_text(downloads or HEADER["downloads"], encoding="utf-8")
    (directory / "own-downloads.csv").write_text(own or HEADER["own"], encoding="utf-8")
    (directory / "traffic.csv").write_text(traffic or HEADER["traffic"], encoding="utf-8")
    return directory


def page_of(downloads="", own="", traffic="", run_date="2026-08-22", traffic_skipped=False, subtract_drafts=True) -> str:
    with tempfile.TemporaryDirectory() as directory:
        stats = write_stats(Path(directory), downloads, own, traffic)
        return site.build_page(
            stats_csv.read_csv(stats / "downloads.csv", stats_csv.DOWNLOADS_COLUMNS),
            stats_csv.read_csv(stats / "own-downloads.csv", stats_csv.OWN_DOWNLOADS_COLUMNS),
            stats_csv.read_csv(stats / "traffic.csv", stats_csv.TRAFFIC_COLUMNS),
            d(run_date), traffic_skipped, subtract_drafts,
        )


TWO_SNAPSHOTS = csv_text(HEADER["downloads"], f"2026-08-20,v1.9.0,{ZIP},100", f"2026-08-21,v1.9.0,{ZIP},120")
TWO_RUNS_OF_FOUR = csv_text(HEADER["own"], f"1,2026-08-21,{ZIP},3,1", f"2,2026-08-21,{ZIP},3,1")
TRAFFIC_ROWS = csv_text(HEADER["traffic"], "2026-08-20,views,7,3", "2026-08-20,clones,2,1", "2026-08-21,views,4,2")


def parse_css(text: str, media: str | None = None) -> list[tuple[str | None, str, dict[str, str]]]:
    """The rules of a stylesheet as (media query, selector, declarations); one level of @media is enough here."""
    rules: list[tuple[str | None, str, dict[str, str]]] = []
    position = 0
    while (opening := text.find("{", position)) >= 0:
        head = text[position:opening].strip()
        depth, end = 1, opening + 1
        while depth:
            depth += {"{": 1, "}": -1}.get(text[end], 0)
            end += 1
        body = text[opening + 1:end - 1]
        if head.startswith("@media"):
            rules.extend(parse_css(body, head[len("@media"):].strip()))
        else:
            declarations = {name.strip(): value.strip() for name, value in (part.split(":", 1) for part in body.split(";") if ":" in part)}
            rules.extend((media, selector.strip(), declarations) for selector in head.split(","))
        position = end
    return rules


def declared(selector: str, media: str | None = None) -> dict[str, str]:
    """What the stylesheet of the page declares for exactly this selector (in the given media query)."""
    merged: dict[str, str] = {}
    for rule_media, rule_selector, declarations in parse_css(site.STYLE):
        if rule_selector == selector and rule_media == media:
            merged.update(declarations)
    return merged


def contrast(foreground: str, background: str) -> float:
    def luminance(color: str) -> float:
        channels = [int(color[i:i + 2], 16) / 255 for i in (1, 3, 5)]
        linear = [c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4 for c in channels]
        return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]

    lighter, darker = sorted((luminance(foreground), luminance(background)), reverse=True)
    return (lighter + 0.05) / (darker + 0.05)


class CssReader(unittest.TestCase):
    def test_the_reader_resolves_selectors_media_and_declarations(self):
        rules = parse_css(":root { a: 1; --b: #fff } @media (x: y) { :root { --b: #000 } } h1, h2 { font-size: 2px; color: red; }")
        self.assertEqual(rules, [
            (None, ":root", {"a": "1", "--b": "#fff"}),
            ("(x: y)", ":root", {"--b": "#000"}),
            (None, "h1", {"font-size": "2px", "color": "red"}),
            (None, "h2", {"font-size": "2px", "color": "red"}),
        ])

    def test_the_real_stylesheet_has_rules_for_what_the_page_tests_look_at(self):
        for selector in (":root", "body", ".scroll", ".scroll:focus-visible"):
            self.assertTrue(declared(selector), selector)
        self.assertTrue(declared(":root", "(prefers-color-scheme: dark)"))


class SiteSeries(unittest.TestCase):
    def test_the_asset_names_match_the_release_asset_module(self):
        module = (REPO_ROOT / "build" / "ReleaseAssets.psm1").read_text(encoding="utf-8")
        prefix = re.search(r"-Name ReleaseAssetPrefix -Value '([^']+)'", module).group(1)
        archive = re.search(r'-Name ReleaseArchiveName -Value "\$ReleaseAssetPrefix([^"]+)"', module).group(1)
        versioned = re.search(r'Versioned = "\$Prefix-v\$Version([^"]+)"', module).group(1)
        self.assertEqual(site.PLUGIN_ARCHIVE_NAME, prefix + archive)
        self.assertEqual(site.VERSIONED_ARCHIVE_FORMAT, prefix + "-v{}" + versioned)
        identity = (REPO_ROOT / "build" / "PluginIdentity.psm1").read_text(encoding="utf-8")
        self.assertEqual(site.X64_NAME_SUFFIX, re.search(r"x64 = @\{ NameSuffix = '([^']+)'", identity).group(1))
        self.assertEqual(site.X64_ARCHIVE_NAME, "teststand-mcp-x64.zip")

    def test_only_a_versioned_archive_name_joins_the_versioned_series(self):
        pattern = site.VERSIONED_ARCHIVE_PATTERN
        self.assertTrue(pattern.fullmatch("teststand-mcp-v1.9.0.zip"))
        self.assertTrue(pattern.fullmatch("teststand-mcp-x64-v1.9.0.zip"))
        for name in (ZIP, "teststand-mcp-x64.zip", "teststand-mcp-arm64-v1.9.0.zip", "teststand-mcp-v1.9.0.zip.sha", "teststand-mcp-vX.zip", "xteststand-mcp-v1.9.0.zip", "teststand-mcp.sha256"):
            self.assertFalse(pattern.fullmatch(name), name)

    def test_install_series_sum_versioned_archives_and_ignore_other_assets(self):
        installs = {d("2026-08-21"): {ZIP: 4, "teststand-mcp-v1.9.0.zip": 2, "teststand-mcp-v1.9.1.zip": 1, "teststand-mcp.sha256": 9}}
        latest, latest64, versioned = site.install_series(installs)
        self.assertEqual(dict(latest.values), {d("2026-08-21"): 4})
        self.assertEqual(dict(latest64.values), {d("2026-08-21"): 0})
        self.assertEqual(dict(versioned.values), {d("2026-08-21"): 3})

    def test_each_plugin_has_its_own_series_and_both_versioned_archives_are_summed(self):
        installs = {d("2026-08-21"): {ZIP: 4, "teststand-mcp-x64.zip": 7, "teststand-mcp-v1.9.0.zip": 2, "teststand-mcp-x64-v1.9.0.zip": 5,
                                     "teststand-mcp-x64.sha256": 9}}
        latest, latest64, versioned = site.install_series(installs)
        self.assertEqual(latest64.style.label, "teststand-mcp-x64.zip")
        self.assertEqual((latest.values[d("2026-08-21")], latest64.values[d("2026-08-21")], versioned.values[d("2026-08-21")]), (4, 7, 7))

    def test_the_chart_runs_from_the_first_data_point_to_the_last_day_and_is_bounded(self):
        series = [site.Series(site.INSTALL_STYLES["latest"], {d("2026-08-20"): 1})]
        self.assertEqual(site.chart_days(series, d("2026-08-22")), [d("2026-08-20"), d("2026-08-21"), d("2026-08-22")])
        self.assertEqual(site.chart_days(series, d("2026-08-19")), [d("2026-08-20")])
        self.assertEqual(site.chart_days([], d("2026-08-22")), [])
        self.assertEqual(len(site.chart_days(series, d("2030-01-01"))), site.MAX_CHART_DAYS)

    def test_the_y_axis_uses_integer_ticks_with_at_most_five_steps(self):
        self.assertEqual(site.y_ticks(0), [0, 1])
        self.assertEqual(site.y_ticks(3), [0, 1, 2, 3])
        self.assertEqual(site.y_ticks(12), [0, 5, 10, 15])
        self.assertEqual(site.y_ticks(100), [0, 20, 40, 60, 80, 100])

    def test_direct_labels_that_would_overlap_are_pushed_apart_and_kept_inside_the_plot(self):
        placed = site.spread_labels([100.0, 100.0, 101.0], 10, 300)
        ordered = sorted(placed)
        self.assertTrue(all(b - a >= site.LABEL_GAP for a, b in zip(ordered, ordered[1:])))
        low = sorted(site.spread_labels([298.0, 299.0, 300.0], 10, 300))
        self.assertLessEqual(max(low), 300)
        self.assertTrue(all(b - a >= site.LABEL_GAP for a, b in zip(low, low[1:])))

    def test_the_series_differ_in_line_style_and_marker_not_only_in_colour(self):
        styles = [*site.INSTALL_STYLES.values(), *site.TRAFFIC_STYLES.values()]
        self.assertEqual(len({s.dash for s in styles}), len(styles))
        self.assertEqual(len({s.marker for s in styles}), len(styles))

    def test_every_text_and_series_colour_has_a_contrast_of_at_least_4_5_in_both_schemes(self):
        self.assertEqual(set(site.PALETTE["light"]), set(site.PALETTE["dark"]))
        for scheme, colors in site.PALETTE.items():
            for foreground, background in site.TEXT_PAIRS:
                with self.subTest(scheme=scheme, pair=f"{foreground}/{background}"):
                    self.assertGreaterEqual(contrast(colors[foreground], colors[background]), 4.5)

    def test_the_page_follows_the_preferred_colour_scheme(self):
        dark_query = "(prefers-color-scheme: dark)"
        self.assertEqual(declared(":root")["color-scheme"], "light dark")
        # The colours a reader gets: the light palette, overlaid by the dark one when the system prefers dark.
        light = {name: value for name, value in declared(":root").items() if name.startswith("--")}
        dark = {**light, **{name: value for name, value in declared(":root", dark_query).items() if name.startswith("--")}}
        for name, value in site.PALETTE["light"].items():
            self.assertEqual(light[f"--{name}"], value, f"light --{name}")
        for name, value in site.PALETTE["dark"].items():
            self.assertEqual(dark[f"--{name}"], value, f"dark --{name}")
        self.assertEqual(declared("body")["background"], "var(--bg)")
        self.assertEqual(declared("body")["color"], "var(--text)")


class SitePage(unittest.TestCase):
    def test_own_downloads_are_subtracted_on_the_page_by_the_contract(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR))
        row = next(r for r in parts.table_rows if r and r[0] == "2026-08-21")
        self.assertEqual(row, ["2026-08-21", "12", "0", "0"])

    def test_the_draft_switch_changes_what_is_subtracted(self):
        page = page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, subtract_drafts=False)
        row = next(r for r in PageParts(page).table_rows if r and r[0] == "2026-08-21")
        self.assertEqual(row[1], "18")

    def test_a_negative_result_is_plotted_as_zero(self):
        own_rows = csv_text(HEADER["own"], f"1,2026-08-21,{ZIP},30,30")
        row = next(r for r in PageParts(page_of(TWO_SNAPSHOTS, own_rows)).table_rows if r and r[0] == "2026-08-21")
        self.assertEqual(row[1], "0")

    def test_a_value_that_covers_several_days_says_since_when_in_the_table_and_the_description(self):
        gap = csv_text(HEADER["downloads"], f"2026-08-20,v1.9.0,{ZIP},100", f"2026-08-24,v1.9.0,{ZIP},130")
        parts = PageParts(page_of(gap, run_date="2026-08-25"))
        rows = {r[0]: r[1] for r in parts.table_rows if r and r[0].startswith("2026-08")}
        self.assertEqual(rows["2026-08-24 (since 2026-08-20)"], "30")
        desc = parts.texts[parts.svgs[0]["aria-labelledby"].split()[1]]
        self.assertIn("2026-08-24 since 2026-08-20", desc)

    def test_consecutive_days_do_not_claim_to_cover_several_days(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR))
        self.assertFalse([r for r in parts.table_rows if r and "since" in r[0]])
        self.assertNotIn("since", parts.texts[parts.svgs[0]["aria-labelledby"].split()[1]])

    def test_every_chart_has_a_title_a_description_and_direct_labels(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS))
        self.assertEqual(len(parts.svgs), 2)
        for svg in parts.svgs:
            self.assertEqual(svg["role"], "img")
            title_id, desc_id = svg["aria-labelledby"].split()
            self.assertTrue(parts.texts[title_id].strip())
            self.assertTrue(parts.texts[desc_id].strip())
        installs_desc = parts.texts[parts.svgs[0]["aria-labelledby"].split()[1]]
        self.assertIn("12 in total", installs_desc)
        self.assertEqual(parts.direct_labels, [ZIP, "teststand-mcp-x64.zip", "Versioned archives", "Views", "Clones"])

    def test_every_series_has_a_marker_element_in_the_markup(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS))
        for key in ("latest", "versioned", "views", "clones"):
            with self.subTest(key):
                self.assertTrue([m for m in parts.markers if f"s-{key}" in m.split()])

    def test_a_visible_table_of_the_daily_counts_follows_each_chart(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS))
        self.assertEqual(parts.order, ["figure", "svg", "table", "figure", "svg", "table"])
        self.assertEqual(parts.column_headers[:4], ["Date (UTC)", ZIP, "teststand-mcp-x64.zip", "Versioned archives"])

    def test_no_table_or_chart_is_hidden_from_sighted_users(self):
        page = page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS)
        self.assertNotRegex(page, r"(?i)display\s*:\s*none|visibility\s*:\s*hidden|sr-only|visually-hidden|clip\s*:|clip-path")
        self.assertNotRegex(page, r"(?i)\shidden[\s>=]|aria-hidden")

    def test_charts_and_tables_sit_in_focusable_labelled_scroll_regions(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS))
        self.assertEqual(len(parts.regions), 4)
        for region in parts.regions:
            self.assertEqual((region["tabindex"], region["role"]), ("0", "region"))
            self.assertTrue(region["aria-label"].strip())
        self.assertEqual(declared(".scroll").get("overflow-x"), "auto")
        self.assertIn("outline", declared(".scroll:focus-visible"))   # a keyboard user sees which region has the focus

    def test_the_chart_keeps_its_natural_width_so_the_labels_stay_legible_on_a_narrow_screen(self):
        svg = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR)).svgs[0]
        self.assertEqual(svg["width"], str(site.WIDTH))
        for media, selector, declarations in parse_css(site.STYLE):
            if ".chart" in selector:
                self.assertNotIn("max-width", declarations, selector)
                self.assertNotIn("width", declarations, selector)

    def test_a_chart_is_not_followed_by_a_caption_that_repeats_the_heading(self):
        self.assertNotIn("figcaption", page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS))

    def test_each_series_is_drawn_with_its_own_dash_pattern(self):
        parts = PageParts(page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR))
        dashes = [line.get("stroke-dasharray", "") for line in parts.polylines]
        self.assertEqual(len(dashes), 3)
        self.assertEqual(len(set(dashes)), len(dashes))

    def test_no_data_shows_an_empty_state_instead_of_an_empty_chart(self):
        page = page_of()
        self.assertNotIn("<svg", page)
        self.assertIn("No download snapshot has been recorded yet", page)

    def test_a_baseline_only_page_says_so_instead_of_drawing_a_chart(self):
        page = page_of(csv_text(HEADER["downloads"], f"2026-08-20,v1.9.0,{ZIP},100"))
        self.assertNotIn("<svg", page)
        self.assertIn("only a baseline", page)

    def test_a_skipped_traffic_collection_is_stated_in_visible_text(self):
        page = page_of(TWO_SNAPSHOTS, traffic_skipped=True)
        self.assertIn("STATS_TRAFFIC_TOKEN secret is not set", page)
        self.assertEqual(len(PageParts(page).svgs), 1)

    def test_recorded_traffic_is_charted_and_the_token_is_not_mentioned(self):
        page = page_of(TWO_SNAPSHOTS, traffic=TRAFFIC_ROWS)
        self.assertEqual(len(PageParts(page).svgs), 2)
        self.assertNotIn("STATS_TRAFFIC_TOKEN", page)

    def test_a_present_token_without_data_says_nothing_was_recorded_yet(self):
        self.assertIn("No views or clones have been recorded yet", page_of(TWO_SNAPSHOTS))

    def test_the_page_loads_nothing_from_outside_and_runs_no_script(self):
        page = page_of(TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR, traffic=TRAFFIC_ROWS)
        self.assertEqual(PageParts(page).external, [])
        self.assertNotRegex(page, r"https?://")
        self.assertIn("default-src 'none'", page)

    def test_series_labels_are_escaped_in_the_chart_and_the_table(self):
        hostile = site.SeriesStyle("<img src=x onerror=alert(1)>&", "latest", "", "circle")
        chart = site.Chart("c", "t<script>", "u", [d("2026-08-21")], [site.Series(hostile, {d("2026-08-21"): 1})])
        markup = site.render_chart(chart) + site.render_table(chart) + site.render_section("h<b>", chart, "")
        self.assertNotIn("<img", markup)
        self.assertNotIn("<script", markup)
        self.assertNotIn("<b>", markup)
        self.assertIn("&lt;img src=x onerror=alert(1)&gt;&amp;", markup)

    def test_a_single_day_chart_still_renders(self):
        chart = site.Chart("c", "t", "u", [d("2026-08-21")], [site.Series(site.INSTALL_STYLES["latest"], {d("2026-08-21"): 3})])
        self.assertIn("<polyline", site.render_chart(chart))


class SiteCommand(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.out = self.root / "site"

    def run_build(self, *extra, clock=None) -> int:
        kwargs = {"clock": clock} if clock else {}
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            return site.main(["--stats-dir", str(self.root / "stats"), "--out", str(self.out), *extra], **kwargs)

    def page(self) -> str:
        return (self.out / "index.html").read_text(encoding="utf-8")

    def test_a_build_from_csv_files_writes_the_page(self):
        write_stats(self.root / "stats", TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR)
        self.assertEqual(self.run_build("--today", "2026-08-22"), 0)
        self.assertIn("<td>12</td>", self.page())

    def test_an_empty_stats_directory_builds_the_empty_state(self):
        self.assertEqual(self.run_build("--today", "2026-08-22"), 0)
        self.assertIn("No download snapshot", self.page())

    def test_the_no_subtract_drafts_flag_reaches_the_calculation(self):
        write_stats(self.root / "stats", TWO_SNAPSHOTS, TWO_RUNS_OF_FOUR)
        self.assertEqual(self.run_build("--today", "2026-08-22", "--no-subtract-drafts"), 0)
        self.assertIn("<td>18</td>", self.page())

    def test_the_traffic_skipped_flag_reaches_the_page(self):
        self.assertEqual(self.run_build("--today", "2026-08-22", "--traffic-skipped"), 0)
        self.assertIn("STATS_TRAFFIC_TOKEN", self.page())

    def test_a_corrupt_csv_fails_the_build_without_writing_a_page(self):
        write_stats(self.root / "stats", csv_text(HEADER["downloads"], f"2026-08-21,v1.9.0,{ZIP},lots"))
        self.assertEqual(self.run_build("--today", "2026-08-22"), 1)
        self.assertFalse((self.out / "index.html").exists())

    def test_the_chart_ends_on_the_day_before_the_run_date_across_midnight(self):
        write_stats(self.root / "stats", TWO_SNAPSHOTS)
        before = datetime(2026, 8, 22, 23, 59, 59, tzinfo=timezone.utc)
        self.assertEqual(self.run_build(clock=lambda: before), 0)
        self.assertNotIn('<th scope="row">2026-08-22', self.page())
        self.assertEqual(self.run_build(clock=lambda: before + timedelta(seconds=1)), 0)
        self.assertIn('<th scope="row">2026-08-22', self.page())


if __name__ == "__main__":
    unittest.main()
