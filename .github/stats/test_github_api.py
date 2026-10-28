"""Tests for github_api.py: the real client against a local HTTP server (credentials, redirects, limits)."""

from __future__ import annotations

import http.client
import http.server
import threading
import time
import unittest
import urllib.parse
from unittest import mock

import github_api
import stats_csv


# --------------------------------------------------------------------------
#  The HTTP client, against a local server
# --------------------------------------------------------------------------

class _Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        self.server.seen.append((self.path, self.headers.get("Authorization")))
        port = self.server.server_port
        if self.path.startswith("/limited"):
            self.send_response(429, "rate limit exceeded")
            self.end_headers()
        elif self.path.startswith("/forbidden"):
            self.send_response(403, "forbidden")
            self.end_headers()
        elif self.path.startswith("/redirect"):
            self.send_response(302)
            self.send_header("Location", f"http://127.0.0.1:{port}/blob")
            self.end_headers()
        elif self.path.startswith("/artifact-to-http"):
            self.send_response(302)
            self.send_header("Location", f"http://127.0.0.1:{port}/blob")
            self.end_headers()
        elif self.path.startswith("/artifact-to-ftp"):
            self.send_response(302)
            self.send_header("Location", "ftp://files.invalid/blob")
            self.end_headers()
        elif self.path.startswith("/slow"):
            self.server.release.wait(timeout=10)
            self.send_response(200)
            self.end_headers()
        elif self.path.startswith("/huge"):
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"x" * (github_api.MAX_ARTIFACT_BYTES + 10))
        elif self.path.startswith("/deep"):
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"[" * 100000)
        else:
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b'{"ok": true}' if self.path.startswith("/json") else b"payload")


def loopback_http(target) -> bool:
    return target.scheme == "http" and target.hostname == "127.0.0.1"


class RealClient(unittest.TestCase):
    def setUp(self):
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), _Handler)
        server.seen = []
        server.release = threading.Event()
        threading.Thread(target=server.serve_forever, daemon=True).start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        self.addCleanup(server.release.set)  # runs first: frees a handler that is still waiting
        self.server = server
        self.root = f"http://127.0.0.1:{server.server_port}"
        self.client = github_api.GitHubClient("SECRET-TOKEN", api_root=self.root, redirect_allowed=loopback_http)
        self.strict_client = github_api.GitHubClient("SECRET-TOKEN", api_root=self.root)

    def test_the_token_goes_in_the_header_and_never_in_the_url(self):
        self.assertEqual(self.client.get_json("/json", {"per_page": "100"}), {"ok": True})
        path, header = self.server.seen[0]
        self.assertEqual(header, "Bearer SECRET-TOKEN")
        self.assertNotIn("SECRET", path)

    def test_an_error_response_fails_without_leaking_the_token(self):
        with self.assertRaises(stats_csv.CollectError) as raised:
            self.client.get_json("/forbidden")
        self.assertIn("403", str(raised.exception))
        self.assertNotIn("SECRET", str(raised.exception))

    def test_a_rate_limit_response_is_an_error_naming_the_status(self):
        with self.assertRaisesRegex(stats_csv.CollectError, "429"):
            self.client.get_json("/limited")

    def test_a_redirect_from_a_json_call_is_an_error_and_is_not_followed(self):
        with self.assertRaisesRegex(stats_csv.CollectError, "redirect"):
            self.client.get_json("/redirect")
        self.assertEqual([path for path, _ in self.server.seen], ["/redirect"])

    def test_an_artifact_redirect_is_fetched_without_credentials(self):
        self.assertEqual(self.client.download(f"{self.root}/artifact-to-http/1/zip"), b"payload")
        self.assertEqual(self.server.seen[0][1], "Bearer SECRET-TOKEN")
        self.assertEqual(self.server.seen[1], ("/blob", None))

    def test_production_accepts_only_an_https_redirect_target(self):
        for path in ("/artifact-to-http/1/zip", "/artifact-to-ftp/1/zip"):
            with self.subTest(path), self.assertRaisesRegex(stats_csv.CollectError, "not an https URL"):
                self.strict_client.download(f"{self.root}{path}")
        self.assertNotIn("/blob", [seen for seen, _ in self.server.seen])

    def test_the_https_rule_judges_the_scheme_and_a_host(self):
        for url, expected in (("https://pipelines.example.invalid/blob?sig=1", True), ("http://x.example.invalid/", False),
                              ("https:///nohost", False), ("javascript:alert(1)", False), ("//host.invalid/x", False)):
            with self.subTest(url):
                self.assertEqual(github_api.https_only(urllib.parse.urlsplit(url)), expected)

    def test_credentials_are_never_sent_to_a_url_outside_the_api(self):
        with mock.patch.object(self.client._opener, "open", side_effect=AssertionError("the network must not be touched")):
            for url in ("https://evil.invalid/artifact/1/zip", f"{self.root}.evil.invalid/x", "http://127.0.0.1:1/x",
                        f"{self.root.replace('http', 'https')}/x"):
                with self.subTest(url), self.assertRaisesRegex(stats_csv.CollectError, "outside the API"):
                    self.client.download(url)
        self.assertEqual(self.server.seen, [])

    def test_an_oversized_response_is_refused(self):
        with self.assertRaisesRegex(stats_csv.CollectError, "larger"):
            self.client.download(f"{self.root}/huge")

    def test_a_non_json_body_and_a_deeply_nested_one_are_errors(self):
        for path in ("/text", "/deep"):
            with self.subTest(path), self.assertRaisesRegex(stats_csv.CollectError, "not JSON"):
                self.client.get_json(path)

    def test_a_broken_http_exchange_is_an_error(self):
        with mock.patch.object(self.client._opener, "open", side_effect=http.client.IncompleteRead(b"")):
            with self.assertRaises(stats_csv.CollectError):
                self.client.get_json("/json")

    def test_a_server_that_stops_answering_fails_at_the_timeout(self):
        with mock.patch.object(github_api, "REQUEST_TIMEOUT_SECONDS", 0.3):
            started = time.monotonic()
            with self.assertRaises(stats_csv.CollectError):
                self.client.get_json("/slow")
        self.assertLess(time.monotonic() - started, 5)


if __name__ == "__main__":
    unittest.main()
