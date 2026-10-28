"""Tests for the download statistics collector: the net-install arithmetic (net_installs), the CSV rows and merge
rules (stats_csv), the collector run (collect.py) and its command line.

From the repository root:  python -m unittest discover -s .github/stats
From .github/stats:        python -m unittest
"""

from __future__ import annotations

import io
import tempfile
import unittest
from contextlib import ExitStack, redirect_stderr, redirect_stdout
from datetime import datetime, timedelta, timezone
from pathlib import Path

import collect
import net_installs
import stats_csv
from fakes import REPO, ZIP, FakeApi, artifact, artifact_url, artifact_zip, d, fixture_document, own, release, snapshots


# --------------------------------------------------------------------------
#  The domain
# --------------------------------------------------------------------------

class NetInstalls(unittest.TestCase):
    def test_own_downloads_of_two_runs_are_subtracted_from_the_raw_delta(self):
        records = [own("1", "2026-08-21", ZIP, 3, 1), own("2", "2026-08-21", ZIP, 3, 1)]
        result = net_installs.installs_per_day(snapshots(("2026-08-20", 100), ("2026-08-21", 120)), records)
        self.assertEqual(result, {d("2026-08-21"): {ZIP: 12}})

    def test_ignoring_drafts_subtracts_only_published_downloads(self):
        records = [own("1", "2026-08-21", ZIP, 3, 1), own("2", "2026-08-21", ZIP, 3, 1)]
        result = net_installs.installs_per_day(snapshots(("2026-08-20", 100), ("2026-08-21", 120)), records, subtract_drafts=False)
        self.assertEqual(result[d("2026-08-21")][ZIP], 18)

    def test_the_provisional_default_subtracts_drafts_until_the_first_release_is_measured(self):
        """Pins the provisional default; the plan's draft-download measurement confirms or flips it."""
        self.assertTrue(net_installs.SUBTRACT_DRAFT_DOWNLOADS)
        records = [own("1", "2026-08-21", ZIP, 5, 0)]
        result = net_installs.installs_per_day(snapshots(("2026-08-20", 10), ("2026-08-21", 20)), records)
        self.assertEqual(result[d("2026-08-21")][ZIP], 5)

    def test_subtraction_never_goes_negative(self):
        records = [own("1", "2026-08-21", ZIP, 9, 9)]
        result = net_installs.installs_per_day(snapshots(("2026-08-20", 100), ("2026-08-21", 102)), records)
        self.assertEqual(result[d("2026-08-21")][ZIP], 0)

    def test_the_first_snapshot_is_only_a_baseline(self):
        self.assertEqual(net_installs.installs_per_day(snapshots(("2026-08-20", 100)), []), {})
        self.assertEqual(net_installs.installs_per_day({}, []), {})

    def test_own_downloads_dated_outside_the_snapshot_window_are_not_subtracted(self):
        records = [own("1", "2026-08-20", ZIP, 0, 4), own("2", "2026-08-22", ZIP, 0, 4)]
        result = net_installs.installs_per_day(snapshots(("2026-08-20", 100), ("2026-08-21", 110)), records)
        self.assertEqual(result[d("2026-08-21")][ZIP], 10)

    def test_a_release_run_late_on_a_day_is_subtracted_from_that_days_snapshot_and_not_counted_the_next_day(self):
        grid = snapshots(("2026-08-19", 100), ("2026-08-20", 105), ("2026-08-21", 108))
        result = net_installs.installs_per_day(grid, [own("1", "2026-08-20", ZIP, 0, 2)])
        self.assertEqual({day: assets[ZIP] for day, assets in result.items()}, {d("2026-08-20"): 3, d("2026-08-21"): 3})

    def test_a_draft_verified_on_one_day_and_published_two_days_later_nets_zero_installs(self):
        grid = snapshots(("2026-08-19", 100), ("2026-08-20", 102), ("2026-08-21", 102), ("2026-08-22", 103))
        records = [own("1", "2026-08-20", ZIP, 2, 0), own("1", "2026-08-22", ZIP, 0, 1)]
        result = net_installs.installs_per_day(grid, records)
        self.assertEqual({day: assets[ZIP] for day, assets in result.items()},
                         {d("2026-08-20"): 0, d("2026-08-21"): 0, d("2026-08-22"): 0})

    def test_a_gap_between_snapshots_books_the_whole_delta_on_the_later_date(self):
        records = [own("1", "2026-08-22", ZIP, 0, 2), own("2", "2026-08-24", ZIP, 0, 3)]
        grid = snapshots(("2026-08-20", 100), ("2026-08-24", 130))
        self.assertEqual(net_installs.installs_per_day(grid, records), {d("2026-08-24"): {ZIP: 25}})
        self.assertEqual(net_installs.snapshot_predecessors(grid), {d("2026-08-24"): d("2026-08-20")})

    def test_a_new_release_counts_in_full_and_releases_are_summed_per_asset_name(self):
        before = {("v1.8.0", ZIP): 50}
        after = {("v1.8.0", ZIP): 53, ("v1.9.0", ZIP): 7}
        result = net_installs.installs_per_day({d("2026-08-20"): before, d("2026-08-21"): after}, [])
        self.assertEqual(result[d("2026-08-21")][ZIP], 10)

    def test_both_plugins_archives_are_counted_and_their_own_downloads_subtracted_per_asset(self):
        x64 = "teststand-mcp-x64.zip"
        before = {("v1.9.0", ZIP): 50, ("v1.9.0", x64): 20}
        after = {("v1.9.0", ZIP): 60, ("v1.9.0", x64): 29}
        records = [own("1", "2026-08-21", ZIP, 1, 2), own("1", "2026-08-21", x64, 1, 1)]
        result = net_installs.installs_per_day({d("2026-08-20"): before, d("2026-08-21"): after}, records)
        self.assertEqual(result[d("2026-08-21")], {ZIP: 7, x64: 7})
        counts, warnings = net_installs.asset_counts_from_releases([release("v1.9.0", {ZIP: 60, x64: 29})])
        self.assertEqual((sorted(c.asset for c in counts), warnings), (sorted([ZIP, x64]), []))

    def test_a_release_that_disappeared_does_not_make_installs_negative(self):
        before = {("v1.8.0", ZIP): 50, ("v1.9.0", ZIP): 5}
        after = {("v1.9.0", ZIP): 8}
        result = net_installs.installs_per_day({d("2026-08-20"): before, d("2026-08-21"): after}, [])
        self.assertEqual(result[d("2026-08-21")][ZIP], 3)

    def test_a_recreated_release_with_a_lower_count_does_not_cancel_other_releases(self):
        before = {("v1.8.0", ZIP): 50, ("v1.9.0", ZIP): 5}
        after = {("v1.8.0", ZIP): 3, ("v1.9.0", ZIP): 8}
        result = net_installs.installs_per_day({d("2026-08-20"): before, d("2026-08-21"): after}, [])
        self.assertEqual(result[d("2026-08-21")][ZIP], 3)

    def test_the_shared_fixture_subtracts_exactly_the_recorded_counts_for_all_four_assets(self):
        records = net_installs.own_records_from_json(fixture_document())
        recorded = {(r.day.isoformat(), r.asset): (r.draft, r.published) for r in records}
        assets = sorted({asset for _, asset in recorded})
        self.assertEqual(len(assets), 4)
        self.assertEqual(recorded[("2026-08-20", ZIP)], (2, 0))
        self.assertEqual(recorded[("2026-08-21", ZIP)], (0, 1))
        for asset in assets:
            grid = {d(day): {("v1.9.0", asset): count} for day, count in (("2026-08-19", 100), ("2026-08-20", 200), ("2026-08-21", 300))}
            for subtract_drafts in (True, False):
                with self.subTest(asset=asset, subtract_drafts=subtract_drafts):
                    result = net_installs.installs_per_day(grid, records, subtract_drafts)
                    for day in ("2026-08-20", "2026-08-21"):
                        draft, published = recorded.get((day, asset), (0, 0))
                        self.assertEqual(result[d(day)][asset], 100 - published - (draft if subtract_drafts else 0))

    def test_a_midnight_boundary_moment_belongs_to_its_utc_date(self):
        self.assertEqual(net_installs.utc_date(datetime(2026, 8, 20, 23, 59, 59, tzinfo=timezone.utc)), d("2026-08-20"))
        self.assertEqual(net_installs.utc_date(datetime(2026, 8, 21, 0, 0, 0, tzinfo=timezone.utc)), d("2026-08-21"))
        west = timezone(timedelta(hours=-2))
        self.assertEqual(net_installs.utc_date(datetime(2026, 8, 20, 23, 30, tzinfo=west)), d("2026-08-21"))

    def test_a_naive_datetime_is_refused(self):
        with self.assertRaises(ValueError):
            net_installs.utc_date(datetime(2026, 8, 20, 12, 0))

    def test_the_snapshot_is_dated_the_previous_utc_day(self):
        self.assertEqual(net_installs.snapshot_date_for(d("2026-08-21")), d("2026-08-20"))
        self.assertEqual(net_installs.snapshot_date_for(d("2026-03-01")), d("2026-02-28"))


class RowsFromJson(unittest.TestCase):
    def test_the_assets_of_draft_releases_are_counted_and_the_rows_are_sorted(self):
        releases = [release("v1.9.0", {"b.zip": 2, "a.zip": 1}), release("v2.0.0", {"c.zip": 9}, draft=True), release("v1.8.0", {"a.zip": 5})]
        counts, warnings = net_installs.asset_counts_from_releases(releases)
        self.assertEqual([(c.tag, c.asset, c.count) for c in counts],
                         [("v1.8.0", "a.zip", 5), ("v1.9.0", "a.zip", 1), ("v1.9.0", "b.zip", 2), ("v2.0.0", "c.zip", 9)])
        self.assertEqual(warnings, [])

    def test_an_untrusted_asset_name_is_skipped_with_a_warning(self):
        bad_names = ["=cmd|calc.zip", "a.zip\n", ".hidden.zip", "-flag.zip", "x" * 201, "../x.zip", "a b.zip"]
        for name in bad_names:
            with self.subTest(name=name):
                counts, warnings = net_installs.asset_counts_from_releases([release("v1.9.0", {name: 3, "ok.zip": 1})])
                self.assertEqual([c.asset for c in counts], ["ok.zip"])
                self.assertEqual(len(warnings), 1)
                self.assertNotIn("calc", warnings[0])

    def test_an_untrusted_tag_name_skips_the_whole_release(self):
        counts, warnings = net_installs.asset_counts_from_releases([release("v1\n::error::x", {"ok.zip": 1})])
        self.assertEqual((counts, len(warnings)), ([], 1))

    def test_an_unexpected_shape_is_an_error(self):
        for bad in ({"message": "rate limited"}, [{"tag_name": "v1", "assets": "x"}], [release("v1", {"a.zip": -1})]):
            with self.subTest(bad=bad), self.assertRaises(stats_csv.CollectError):
                net_installs.asset_counts_from_releases(bad)

    def test_the_fixture_parses_into_one_record_per_asset_and_date(self):
        records = net_installs.own_records_from_json(fixture_document())
        self.assertEqual(len(records), 6)
        self.assertEqual({r.run_id for r in records}, {"1234567890"})

    def test_a_record_that_breaks_the_schema_is_rejected(self):
        good = fixture_document()
        broken = [
            [],
            {**good, "run_id": 7},
            {**good, "run_id": "1\n"},
            {**good, "counts": []},
            {**good, "counts": {"20260820": {}}},
            {**good, "counts": {"2026-08-20": {ZIP: 4}}},
            {**good, "counts": {"2026-08-20": {ZIP: {"draft": 1}}}},
            {**good, "counts": {"2026-08-20": {"../x.zip": {"draft": 1, "published": 0}}}},
            {**good, "counts": {"2026-08-20": {ZIP: {"draft": True, "published": 0}}}},
        ]
        for document in broken:
            with self.subTest(document=document), self.assertRaises(net_installs.OwnRecordError):
                net_installs.own_records_from_json(document)

    def test_a_record_must_name_the_run_that_uploaded_it(self):
        with self.assertRaisesRegex(net_installs.OwnRecordError, "run_id"):
            net_installs.own_records_from_json(fixture_document(), expected_run_id="42")
        self.assertEqual(len(net_installs.own_records_from_json(fixture_document(), expected_run_id="1234567890")), 6)

    def test_an_error_message_never_carries_a_raw_untrusted_value(self):
        hostile = "\n::error::injected"
        document = {"run_id": "1", "counts": {"2026-08-20": {"ok.zip": {"draft": hostile, "published": 0}}}}
        with self.assertRaises(net_installs.OwnRecordError) as raised:
            net_installs.own_records_from_json({**document, "counts": {hostile: {}}})
        self.assertNotIn("\n", str(raised.exception))
        with self.assertRaises(stats_csv.CollectError) as raised_csv:
            stats_csv.csv_int(hostile)
        self.assertNotIn("\n", str(raised_csv.exception))

    def test_traffic_days_are_read_by_metric(self):
        response = {"count": 5, "views": [{"timestamp": "2026-08-14T00:00:00Z", "count": 5, "uniques": 2}]}
        days = net_installs.traffic_from_json("views", response)
        self.assertEqual(days, [net_installs.TrafficDay(d("2026-08-14"), "views", 5, 2)])
        with self.assertRaises(stats_csv.CollectError):
            net_installs.traffic_from_json("clones", response)


class MergeRules(unittest.TestCase):
    COUNTS = [net_installs.AssetCount("v1.9.0", ZIP, 5)]

    def rows(self, day: str, count: int) -> list[dict]:
        return [{"date": day, "tag": "v1.9.0", "asset": ZIP, "download_count": str(count)}]

    def test_the_first_snapshot_is_appended(self):
        self.assertEqual(net_installs.merge_snapshot([], self.COUNTS, d("2026-08-20")), self.rows("2026-08-20", 5))

    def test_equal_counts_append_nothing(self):
        existing = self.rows("2026-08-20", 5)
        self.assertEqual(net_installs.merge_snapshot(existing, self.COUNTS, d("2026-08-21")), existing)

    def test_a_changed_count_appends_a_full_snapshot(self):
        existing = self.rows("2026-08-20", 4)
        merged = net_installs.merge_snapshot(existing, self.COUNTS, d("2026-08-21"))
        self.assertEqual(merged, existing + self.rows("2026-08-21", 5))

    def test_a_second_run_for_the_same_snapshot_date_replaces_that_dates_rows(self):
        merged = net_installs.merge_snapshot(self.rows("2026-08-20", 4), self.COUNTS, d("2026-08-20"))
        self.assertEqual(merged, self.rows("2026-08-20", 5))

    def test_no_assets_leave_the_rows_alone(self):
        self.assertEqual(net_installs.merge_snapshot([], [], d("2026-08-20")), [])

    def test_a_recorded_run_is_not_recorded_twice(self):
        records = net_installs.own_records_from_json(fixture_document())
        once = net_installs.merge_own_records([], records)
        self.assertEqual(len(once), 6)
        self.assertEqual(net_installs.merge_own_records(once, records), once)

    def test_the_newest_traffic_value_of_a_day_wins(self):
        existing = [{"date": "2026-08-14", "metric": "views", "count": "1", "uniques": "1"}]
        merged = net_installs.merge_traffic(existing, [net_installs.TrafficDay(d("2026-08-14"), "views", 4, 2), net_installs.TrafficDay(d("2026-08-13"), "clones", 3, 1)])
        self.assertEqual([(r["date"], r["metric"], r["count"]) for r in merged], [("2026-08-13", "clones", "3"), ("2026-08-14", "views", "4")])


class CollectRun(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.stats = Path(directory.name) / "stats"

    def run_collect(self, api, traffic_api=None, run_date="2026-08-21"):
        return collect.collect(api, traffic_api, REPO, self.stats, d(run_date))

    def text(self, name: str) -> str:
        return (self.stats / name).read_text(encoding="utf-8")

    def snapshot(self):
        return {p.name: p.read_bytes() for p in self.stats.iterdir()}

    def test_the_first_run_without_releases_creates_header_only_files(self):
        result = self.run_collect(FakeApi())
        self.assertEqual(self.text("downloads.csv"), "date,tag,asset,download_count\n")
        self.assertEqual(self.text("own-downloads.csv"), "run_id,date,asset,draft,published\n")
        self.assertEqual(self.text("traffic.csv"), "date,metric,count,uniques\n")
        self.assertEqual(len(result.changed_files), 3)

    def test_the_snapshot_is_dated_the_previous_utc_day(self):
        self.run_collect(FakeApi(releases=[release("v1.9.0", {ZIP: 4, "teststand-mcp.sha256": 1})]), run_date="2026-08-21")
        self.assertEqual(self.text("downloads.csv").splitlines(), [
            "date,tag,asset,download_count",
            "2026-08-20,v1.9.0,teststand-mcp.sha256,1",
            f"2026-08-20,v1.9.0,{ZIP},4",
        ])

    def test_the_assets_of_a_draft_release_are_in_the_snapshot(self):
        self.run_collect(FakeApi(releases=[release("v1.9.0", {ZIP: 2}, draft=True)]))
        self.assertIn(f"2026-08-20,v1.9.0,{ZIP},2", self.text("downloads.csv"))

    def test_a_second_run_with_equal_counts_changes_nothing(self):
        api = FakeApi(releases=[release("v1.9.0", {ZIP: 4})])
        self.run_collect(api)
        before = self.snapshot()
        result = self.run_collect(api, run_date="2026-08-22")
        self.assertEqual(result.changed_files, [])
        self.assertEqual(self.snapshot(), before)

    def test_an_api_error_on_the_artifact_listing_writes_nothing(self):
        api = FakeApi(releases=[release("v1.9.0", {ZIP: 4})], failing=[f"/repos/{REPO}/actions/artifacts"])
        with self.assertRaisesRegex(stats_csv.CollectError, "rate limit"):
            self.run_collect(api)
        self.assertFalse(self.stats.exists())

    def test_an_error_on_a_later_day_leaves_the_existing_files_untouched(self):
        self.run_collect(FakeApi(releases=[release("v1.9.0", {ZIP: 4})]))
        before = self.snapshot()
        with self.assertRaises(stats_csv.CollectError):
            self.run_collect(FakeApi(releases=[release("v1.9.0", {ZIP: 9})], failing=[f"/repos/{REPO}/releases"]), run_date="2026-08-22")
        self.assertEqual(self.snapshot(), before)

    def test_without_a_traffic_client_no_traffic_is_requested_or_recorded(self):
        api = FakeApi()
        self.run_collect(api, traffic_api=None)
        self.assertFalse([p for p in api.requested if "traffic" in p])
        self.assertEqual(self.text("traffic.csv"), "date,metric,count,uniques\n")

    def test_with_a_traffic_client_views_and_clones_are_recorded(self):
        views = {"views": [{"timestamp": "2026-08-19T00:00:00Z", "count": 7, "uniques": 3}]}
        clones = {"clones": [{"timestamp": "2026-08-19T00:00:00Z", "count": 2, "uniques": 1}]}
        traffic_api = FakeApi(traffic={"views": views, "clones": clones})
        self.run_collect(FakeApi(), traffic_api=traffic_api)
        self.assertEqual(self.text("traffic.csv").splitlines(), [
            "date,metric,count,uniques", "2026-08-19,clones,2,1", "2026-08-19,views,7,3"])

    def test_a_failing_traffic_call_fails_the_run_before_anything_is_written(self):
        traffic_api = FakeApi(failing=[f"/repos/{REPO}/traffic/views"])
        with self.assertRaises(stats_csv.CollectError):
            self.run_collect(FakeApi(releases=[release("v1.9.0", {ZIP: 4})]), traffic_api=traffic_api)
        self.assertFalse(self.stats.exists())

    def test_a_corrupt_stats_file_is_an_error(self):
        self.stats.mkdir(parents=True)
        (self.stats / "downloads.csv").write_text("date,asset\n2026-08-20,x\n", encoding="utf-8")
        with self.assertRaisesRegex(stats_csv.CollectError, "columns"):
            self.run_collect(FakeApi())

    def test_an_endless_listing_stops_at_the_page_bound(self):
        class Endless(FakeApi):
            def get_json(self, path, params=None):
                return [release("v1", {})] * collect.PAGE_SIZE

        with self.assertRaisesRegex(stats_csv.CollectError, "MAX_PAGES"):
            self.run_collect(Endless())


class ListPagination(unittest.TestCase):
    """Every list is read to its end, for the sizes where an off-by-one hides: one item, one full page, one more."""

    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.stats = Path(directory.name) / "stats"

    def collect(self, api):
        return collect.collect(api, None, REPO, self.stats, d("2026-08-21"))

    def test_the_fake_serves_the_pages_of_a_list_like_github(self):
        api = FakeApi(releases=[release(f"v1.0.{i}", {}) for i in range(101)])
        path = f"/repos/{REPO}/releases"
        self.assertEqual(len(api.get_json(path, {"per_page": "100", "page": "1"})), 100)
        self.assertEqual([r["tag_name"] for r in api.get_json(path, {"per_page": "100", "page": "2"})], ["v1.0.100"])
        self.assertEqual(api.get_json(path, {"per_page": "100", "page": "3"}), [])
        self.assertEqual(len(api.get_json(path, {"per_page": "30", "page": "2"})), 30)

    def test_releases_are_all_read_for_every_size_around_a_page_boundary(self):
        # size -> list requests: a short page ends the listing; a full page needs one more request to learn that it was the last
        for count, requests in ((0, 1), (1, 1), (99, 1), (100, 2), (101, 2), (200, 3)):
            with self.subTest(releases=count):
                self.stats = self.stats.parent / f"stats-{count}"
                api = FakeApi(releases=[release(f"v1.0.{i}", {ZIP: i + 1}) for i in range(count)])
                self.collect(api)
                rows = (self.stats / "downloads.csv").read_text(encoding="utf-8").splitlines()[1:]
                self.assertEqual(len(rows), count)
                self.assertEqual(api.requested.count(f"/repos/{REPO}/releases"), requests)

    def test_artifacts_are_all_read_for_every_size_around_a_page_boundary(self):
        for count, requests in ((0, 1), (1, 1), (100, 2), (101, 2)):
            with self.subTest(artifacts=count):
                self.stats = self.stats.parent / f"stats-{count}"
                found = [artifact(run_id) for run_id in range(1, count + 1)]
                downloads = {artifact_url(run_id): artifact_zip({"run_id": str(run_id), "counts": {}}) for run_id in range(1, count + 1)}
                api = FakeApi(artifacts=found, downloads=downloads)
                self.collect(api)
                rows = (self.stats / "own-downloads.csv").read_text(encoding="utf-8").splitlines()[1:]
                self.assertEqual(len(rows), count)
                self.assertEqual(api.requested.count(f"/repos/{REPO}/actions/artifacts"), requests)


class OwnDownloadArtifacts(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.stats = Path(directory.name) / "stats"

    def run_collect(self, api, run_date="2026-08-21"):
        return collect.collect(api, None, REPO, self.stats, d(run_date))

    def own_rows(self) -> list[str]:
        return (self.stats / "own-downloads.csv").read_text(encoding="utf-8").splitlines()[1:]

    def api(self, *artifacts, payloads=None, **kwargs):
        downloads = {artifact_url(a["workflow_run"]["id"]): (payloads or {}).get(a["workflow_run"]["id"], artifact_zip(fixture_document()))
                     for a in artifacts if a.get("workflow_run")}
        return FakeApi(releases=[release("v1.9.0", {ZIP: 4})], artifacts=list(artifacts), downloads=downloads, **kwargs)

    def test_an_artifact_of_a_release_run_is_recorded(self):
        result = self.run_collect(self.api(artifact(1234567890)))
        self.assertEqual(len(self.own_rows()), 6)
        self.assertEqual(result.warnings, [])

    def test_a_run_is_recorded_once_and_not_downloaded_again(self):
        self.run_collect(self.api(artifact(1234567890)))
        again = self.api(artifact(1234567890), payloads={1234567890: b"must not be read"})
        self.run_collect(again, run_date="2026-08-22")
        self.assertEqual(len(self.own_rows()), 6)
        self.assertNotIn(artifact_url(1234567890), again.requested)

    def test_two_artifacts_naming_one_run_count_once_and_the_run_is_looked_up_once(self):
        first, second = artifact(1234567890), {**artifact(1234567890), "id": 999}
        api = self.api(first, second)
        self.run_collect(api)
        self.assertEqual(len(self.own_rows()), 6)
        self.assertEqual(api.requested.count(f"/repos/{REPO}/actions/runs/1234567890"), 1)
        self.assertEqual(api.requested.count(artifact_url(1234567890)), 1)

    def test_a_record_without_any_download_still_marks_its_run_as_known(self):
        empty = {"run_id": "55", "counts": {}}
        self.run_collect(self.api(artifact(55), payloads={55: artifact_zip(empty)}))
        self.assertEqual(self.own_rows(), ["55,2026-08-21,,0,0"])
        again = self.api(artifact(55), payloads={55: artifact_zip(empty)})
        self.run_collect(again, run_date="2026-08-22")
        self.assertNotIn(artifact_url(55), again.requested)
        self.assertEqual(len(self.own_rows()), 1)

    def test_zero_matching_runs_are_fine(self):
        result = self.run_collect(FakeApi(artifacts=[]))
        self.assertEqual(result.warnings, [])

    def test_an_expired_artifact_is_not_downloaded(self):
        api = self.api(artifact(5, expired=True))
        self.run_collect(api)
        self.assertFalse([r for r in api.requested if r.startswith("https://") or "/runs/" in r])

    def test_a_record_naming_another_run_is_rejected(self):
        api = self.api(artifact(5), payloads={5: artifact_zip(fixture_document())})
        result = self.run_collect(api)
        self.assertEqual((len(result.warnings), self.own_rows()), (1, []))

    def test_each_failed_provenance_check_rejects_the_artifact_without_downloading_it(self):
        cases = {
            "from a fork": (artifact(5, head_repository_id=8), {}),
            "without repository ids": ({**artifact(5), "workflow_run": {"id": 5}}, {}),
            "another workflow": (artifact(5), {5: {"path": ".github/workflows/ci.yml", "event": "push"}}),
            "a look-alike path": (artifact(5), {5: {"path": "x.github/workflows/release.yml", "event": "push"}}),
            "a pull request": (artifact(5), {5: {"path": ".github/workflows/release.yml", "event": "pull_request"}}),
            "a schedule": (artifact(5), {5: {"path": ".github/workflows/release.yml", "event": "schedule"}}),
            "an unreadable run": (artifact(5), {5: ["not", "a", "run"]}),
        }
        for name, (item, runs) in cases.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as directory:
                self.stats = Path(directory) / "stats"
                api = self.api(item, runs=runs)
                result = self.run_collect(api)
                self.assertEqual(len(result.warnings), 1, name)
                self.assertEqual(self.own_rows(), [], name)
                self.assertNotIn(artifact_url(5), api.requested, name)

    def test_a_rejected_run_is_looked_up_once_however_many_artifacts_name_it(self):
        first, second = artifact(5), {**artifact(5), "id": 999}
        api = self.api(first, second, runs={5: {"path": ".github/workflows/ci.yml", "event": "push"}})
        result = self.run_collect(api)
        self.assertEqual(len(result.warnings), 2)
        self.assertEqual(api.requested.count(f"/repos/{REPO}/actions/runs/5"), 1)

    def test_a_run_path_with_a_ref_suffix_and_a_manual_start_are_accepted(self):
        runs = {5: {"path": ".github/workflows/release.yml@refs/tags/v1.9.0", "event": "workflow_dispatch"}}
        record = artifact_zip({"run_id": "5", "counts": {"2026-08-20": {ZIP: {"draft": 1, "published": 0}}}})
        self.run_collect(self.api(artifact(5), runs=runs, payloads={5: record}))
        self.assertEqual(self.own_rows(), [f"5,2026-08-20,{ZIP},1,0"])

    def test_a_tag_push_run_is_accepted_whatever_its_branch_name(self):
        item = artifact(5, head_branch="v1.9.0")
        record = artifact_zip({"run_id": "5", "counts": {"2026-08-20": {ZIP: {"draft": 1, "published": 0}}}})
        self.run_collect(self.api(item, payloads={5: record}))
        self.assertEqual(len(self.own_rows()), 1)

    def test_an_artifact_without_a_workflow_run_is_skipped_with_a_warning(self):
        result = self.run_collect(self.api({**artifact(5), "workflow_run": None}, payloads={}))
        self.assertEqual(len(result.warnings), 1)

    def test_one_bad_artifact_never_fails_the_run_or_hides_a_good_one(self):
        deep = artifact_zip("[" * 100000)
        broken = {
            5: b"not a zip",
            6: artifact_zip({}, name="other.json"),
            7: artifact_zip({"run_id": "7", "counts": []}),
            8: deep,
            9: stats_csv.CollectError("GET /x: the response is larger than 1048576 bytes"),
            10: artifact_zip({"run_id": "10", "counts": {"2026-08-20": {ZIP: {"draft": 1, "published": 0}}}}),
        }
        api = self.api(*[artifact(n) for n in broken], payloads=broken)
        result = self.run_collect(api)
        self.assertEqual(len(result.warnings), 5)
        self.assertEqual(self.own_rows(), [f"10,2026-08-20,{ZIP},1,0"])
        self.assertIn(f"2026-08-20,v1.9.0,{ZIP},4", (self.stats / "downloads.csv").read_text(encoding="utf-8"))

    def test_a_failing_run_lookup_is_a_warning_for_that_artifact_only(self):
        api = self.api(artifact(5), failing=[f"/repos/{REPO}/actions/runs/5"])
        result = self.run_collect(api)
        self.assertEqual(len(result.warnings), 1)
        self.assertEqual(len(self.stats.joinpath("downloads.csv").read_text(encoding="utf-8").splitlines()), 2)

    def test_a_warning_never_carries_a_raw_untrusted_value(self):
        hostile = {"run_id": "5", "counts": {"2026-08-20": {"bad\n::error::x": {"draft": 1, "published": 0}}}}
        result = self.run_collect(self.api(artifact(5), payloads={5: artifact_zip(hostile)}))
        self.assertEqual(len(result.warnings), 1)
        self.assertNotIn("\n", result.warnings[0])


class CommandLine(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.stats = Path(directory.name) / "stats"
        self.tokens: list[str] = []
        self.failing: list[str] = []
        quiet = ExitStack()
        quiet.enter_context(redirect_stdout(io.StringIO()))
        quiet.enter_context(redirect_stderr(io.StringIO()))
        self.addCleanup(quiet.close)

    def factory(self, token):
        self.tokens.append(token)
        empty_traffic = {"views": {"views": []}, "clones": {"clones": []}}
        return FakeApi(releases=[release("v1.9.0", {ZIP: 4})], traffic=empty_traffic, failing=self.failing)

    def run_main(self, env, clock=None, extra=()):
        args = ["--stats-dir", str(self.stats), *extra]
        kwargs = {"clock": clock} if clock else {}
        return collect.main(args, env=env, client_factory=self.factory, **kwargs)

    def test_the_snapshot_is_dated_the_previous_utc_day_across_the_midnight_boundary(self):
        env = {"GITHUB_TOKEN": "t", "GITHUB_REPOSITORY": REPO}
        before = datetime(2026, 8, 20, 23, 59, 59, tzinfo=timezone.utc)
        self.assertEqual(self.run_main(env, clock=lambda: before), 0)
        self.assertIn("2026-08-19,v1.9.0", (self.stats / "downloads.csv").read_text(encoding="utf-8"))
        (self.stats / "downloads.csv").unlink()
        self.assertEqual(self.run_main(env, clock=lambda: before + timedelta(seconds=1)), 0)
        self.assertIn("2026-08-20,v1.9.0", (self.stats / "downloads.csv").read_text(encoding="utf-8"))

    def test_today_names_the_run_date_and_the_snapshot_is_the_day_before(self):
        self.assertEqual(self.run_main({"GITHUB_TOKEN": "t", "GITHUB_REPOSITORY": REPO}, extra=["--today", "2026-08-21"]), 0)
        self.assertIn("2026-08-20,v1.9.0", (self.stats / "downloads.csv").read_text(encoding="utf-8"))

    def test_only_a_set_traffic_token_creates_a_traffic_client(self):
        env = {"GITHUB_TOKEN": "main", "GITHUB_REPOSITORY": REPO, "STATS_TRAFFIC_TOKEN": "  "}
        self.assertEqual(self.run_main(env, extra=["--today", "2026-08-21"]), 0)
        self.assertEqual(self.tokens, ["main"])

    def test_a_set_traffic_token_creates_the_main_client_first_and_a_traffic_client(self):
        env = {"GITHUB_TOKEN": "main", "GITHUB_REPOSITORY": REPO, "STATS_TRAFFIC_TOKEN": "traffic"}
        self.assertEqual(self.run_main(env, extra=["--today", "2026-08-21"]), 0)
        self.assertEqual(self.tokens, ["main", "traffic"])

    def test_an_api_failure_returns_one_and_leaves_no_stats_directory(self):
        self.failing = [f"/repos/{REPO}/releases"]
        self.assertEqual(self.run_main({"GITHUB_TOKEN": "t", "GITHUB_REPOSITORY": REPO}, extra=["--today", "2026-08-21"]), 1)
        self.assertFalse(self.stats.exists())

    def test_a_missing_token_a_bad_repo_and_a_bad_date_exit_non_zero_without_files(self):
        cases = [({"GITHUB_REPOSITORY": REPO}, ()), ({"GITHUB_TOKEN": "t"}, ()),
                 ({"GITHUB_TOKEN": "t", "GITHUB_REPOSITORY": REPO + "\n"}, ()),
                 ({"GITHUB_TOKEN": "t", "GITHUB_REPOSITORY": "-owner/name"}, ()),
                 ({"GITHUB_TOKEN": "t", "GITHUB_REPOSITORY": REPO}, ["--today", "20-08-2026"])]
        for env, extra in cases:
            with self.subTest(env=sorted(env.items()), extra=extra):
                self.assertEqual(self.run_main(env, extra=extra), 1)
                self.assertFalse(self.stats.exists())


if __name__ == "__main__":
    unittest.main()
