"""Tests for the contract of .github/workflows/pages.yml, checked on its text (the standard library has no YAML parser).

The rules every workflow shares (pinned actions, permissions, no expression in a run: block) are tested once, for all
workflows, in build/tests/WorkflowContract.ps1; this file keeps what is specific to the statistics workflow."""

from __future__ import annotations

import re
import unittest

from fakes import REPO_ROOT


# --------------------------------------------------------------------------
#  The workflow, checked on its text (the standard library has no YAML parser)
# --------------------------------------------------------------------------

class PagesWorkflow(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = (REPO_ROOT / ".github" / "workflows" / "pages.yml").read_text(encoding="utf-8")
        body = cls.text.split("\njobs:\n", 1)[1]
        cls.jobs = {name: chunk for name, chunk in re.findall(r"(?ms)^  (\w+):\n(.*?)(?=^  \w+:\n|\Z)", body)}
        cls.triggers = cls.text.split("\non:\n", 1)[1].split("\npermissions:", 1)[0]

    def step(self, name: str) -> str:
        match = re.search(rf"(?ms)^      - name: {re.escape(name)}\n(.*?)(?=^      - name: |\Z)", self.jobs["collect"])
        self.assertIsNotNone(match, name)
        return match.group(1)

    def test_it_runs_daily_just_after_midnight_utc_and_by_hand_and_nothing_else_starts_it(self):
        self.assertIn("cron: '17 0 * * *'", self.triggers)
        self.assertRegex(self.triggers, r"(?m)^  workflow_dispatch:")
        self.assertEqual(re.findall(r"(?m)^  (\w+):", self.triggers), ["schedule", "workflow_dispatch"])
        self.assertNotIn("workflow_run", self.triggers)
        self.assertNotIn("workflow_call", self.triggers)

    def test_no_other_workflow_refers_to_it(self):
        for other in (REPO_ROOT / ".github" / "workflows").glob("*.yml"):
            if other.name != "pages.yml":
                self.assertNotIn("pages.yml", other.read_text(encoding="utf-8"), other.name)

    def test_what_each_job_may_do_is_exactly_what_it_needs(self):
        self.assertEqual(set(self.jobs), {"collect", "deploy"})

        def permissions(job: str) -> list[tuple[str, str]]:
            block = re.search(r"(?m)^    permissions:\n((?:      .*\n)+)", self.jobs[job]).group(1)
            return re.findall(r"(?m)^      ([\w-]+): (\w+)", block)

        self.assertEqual(permissions("collect"), [("contents", "write"), ("actions", "read")])
        self.assertEqual(permissions("deploy"), [("pages", "write"), ("id-token", "write")])

    def test_deploying_is_a_separate_job_that_waits_for_collecting(self):
        self.assertIn("needs: collect", self.jobs["deploy"])
        self.assertIn("actions/deploy-pages@", self.jobs["deploy"])
        self.assertNotIn("deploy-pages", self.jobs["collect"])
        self.assertNotIn("collect.py", self.jobs["deploy"])

    def test_the_checkout_does_not_persist_the_token_and_git_gets_it_through_an_extra_header(self):
        self.assertIn("persist-credentials: false", self.text)
        self.assertNotIn("persist-credentials: true", self.text)
        for name in ("Check out the stats branch as a worktree", "Commit the statistics when they changed"):
            chunk = self.step(name)
            with self.subTest(name):
                self.assertIn("GH_TOKEN: ${{ github.token }}", chunk)
                self.assertIn("extraheader=AUTHORIZATION: basic", chunk)
                self.assertIn("::add-mask::", chunk)
        self.assertNotRegex(self.text, r"git config[^\n]*(extraheader|token)")

    def test_the_token_is_only_ever_given_to_steps_as_environment_variables(self):
        for line in self.text.splitlines():
            if "github.token" in line or "secrets." in line:
                with self.subTest(line=line.strip()):
                    self.assertRegex(line, r"^\s+(GH_TOKEN|GITHUB_TOKEN|STATS_TRAFFIC_TOKEN|HAS_TRAFFIC_TOKEN): \$\{\{ [\w. !=']+ \}\}|^\s*#")

    def test_the_traffic_token_reaches_only_the_collector_and_the_build_only_learns_whether_it_exists(self):
        lines = [line for line in self.text.splitlines() if "STATS_TRAFFIC_TOKEN" in line and not line.lstrip().startswith("#")]
        self.assertEqual(len(lines), 2)
        self.assertIn("STATS_TRAFFIC_TOKEN: ${{ secrets.STATS_TRAFFIC_TOKEN }}", self.step("Collect download counts"))
        self.assertIn("HAS_TRAFFIC_TOKEN: ${{ secrets.STATS_TRAFFIC_TOKEN != '' }}", self.step("Build the page"))
        self.assertNotIn("echo", "".join(lines))

    def test_the_build_passes_traffic_skipped_exactly_when_the_token_is_absent(self):
        build = self.step("Build the page")
        self.assertRegex(build, r'if \[ "\$HAS_TRAFFIC_TOKEN" != "true" \]; then args\+=\(--traffic-skipped\); fi')
        self.assertIn('"${args[@]}"', build)

    def test_the_scripts_it_runs_exist_and_the_page_is_built_before_the_push_and_the_upload(self):
        for script in re.findall(r"python3 (\.github/\S+\.py)", self.text):
            self.assertTrue((REPO_ROOT / script).is_file(), script)
        steps = [self.text.index(marker) for marker in ("stats/collect.py", "site/build.py", "git -c \"http.https", "upload-pages-artifact@")]
        self.assertEqual(steps, sorted(steps))

    def test_the_commit_step_checks_for_changes_before_it_commits_and_pushes(self):
        commit = self.step("Commit the statistics when they changed")
        positions = [commit.index(marker) for marker in ("diff --cached --quiet", "exit 0", "commit --quiet", "push origin")]
        self.assertEqual(positions, sorted(positions))
        self.assertEqual(len(set(positions)), 4)

    def test_a_missing_stats_branch_creates_an_orphan_and_any_other_git_failure_fails(self):
        chunk = self.step("Check out the stats branch as a worktree")
        self.assertIn('case "$status" in', chunk)
        self.assertRegex(chunk, r"(?m)^\s+0\)")
        self.assertRegex(chunk, r"(?ms)^\s+2\)\n.*checkout --orphan stats.*;;")
        self.assertRegex(chunk, r"(?ms)^\s+\*\)\n.*exit 1\n\s+;;")

    def test_nothing_in_the_collecting_job_can_swallow_a_failure(self):
        for forbidden in ("continue-on-error", "if: always()", "if: failure()", "|| true", "|| :"):
            self.assertNotIn(forbidden, self.jobs["collect"], forbidden)
        self.assertEqual(len(re.findall(r"set -euo pipefail", self.jobs["collect"])), 3)


if __name__ == "__main__":
    unittest.main()
