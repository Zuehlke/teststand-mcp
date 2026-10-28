"""A minimal GitHub REST client. Standard library only.

The token travels only in the Authorization header and only to the API root. A redirect is
never followed with credentials: an artifact download answers with a redirect to a signed
blob URL, which is fetched without the token and only over https.
"""

from __future__ import annotations

import http.client
import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Callable, Mapping, Protocol

from stats_csv import CollectError

API_ROOT = "https://api.github.com"
API_VERSION = "2022-11-28"
REQUEST_TIMEOUT_SECONDS = 30
MAX_JSON_BYTES = 8 * 1024 * 1024
MAX_ARTIFACT_BYTES = 1024 * 1024
REDIRECT_STATUSES = (301, 302, 303, 307, 308)


class GitHubApi(Protocol):
    def get_json(self, path: str, params: Mapping[str, str] | None = None) -> object: ...

    def download(self, url: str) -> bytes: ...


def https_only(target: urllib.parse.SplitResult) -> bool:
    """The production rule for where an artifact redirect may point."""
    return target.scheme == "https" and bool(target.hostname)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """Surfaces a redirect as an HTTPError so the caller decides whether credentials follow."""

    def redirect_request(self, *args, **kwargs):
        return None


class _Redirect(Exception):
    def __init__(self, location: str):
        super().__init__(location)
        self.location = location


class GitHubClient:
    def __init__(self, token: str, api_root: str = API_ROOT,
                 redirect_allowed: Callable[[urllib.parse.SplitResult], bool] = https_only):
        self._token = token
        self._api_root = api_root.rstrip("/")
        self._api = urllib.parse.urlsplit(self._api_root)
        self._redirect_allowed = redirect_allowed
        self._opener = urllib.request.build_opener(_NoRedirect)

    def get_json(self, path: str, params: Mapping[str, str] | None = None) -> object:
        query = f"?{urllib.parse.urlencode(params)}" if params else ""
        try:
            body = self._fetch(f"{self._api_root}{path}{query}", authorised=True, limit=MAX_JSON_BYTES)
        except _Redirect:
            raise CollectError(f"GET {path}: unexpected redirect") from None
        try:
            return json.loads(body)
        except (ValueError, RecursionError):
            raise CollectError(f"GET {path}: the response is not JSON") from None

    def download(self, url: str) -> bytes:
        target = urllib.parse.urlsplit(url)
        if (target.scheme, target.netloc) != (self._api.scheme, self._api.netloc):
            raise CollectError("refusing to send credentials to a URL outside the API")
        try:
            return self._fetch(url, authorised=True, limit=MAX_ARTIFACT_BYTES)
        except _Redirect as redirect:
            if not self._redirect_allowed(urllib.parse.urlsplit(redirect.location)):
                raise CollectError("the artifact redirect is not an https URL") from None
            return self._fetch(redirect.location, authorised=False, limit=MAX_ARTIFACT_BYTES)

    def _fetch(self, url: str, authorised: bool, limit: int) -> bytes:
        headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": API_VERSION, "User-Agent": "teststand-mcp-stats"}
        if authorised:
            headers["Authorization"] = f"Bearer {self._token}"
        label = urllib.parse.urlsplit(url).path
        try:
            with self._opener.open(urllib.request.Request(url, headers=headers), timeout=REQUEST_TIMEOUT_SECONDS) as response:
                body = response.read(limit + 1)
        except urllib.error.HTTPError as error:
            error.close()
            if error.code in REDIRECT_STATUSES and error.headers.get("Location"):
                raise _Redirect(error.headers["Location"]) from None
            raise CollectError(f"GET {label}: HTTP {error.code} {error.reason}") from None
        except (urllib.error.URLError, OSError, http.client.HTTPException) as error:
            raise CollectError(f"GET {label}: {getattr(error, 'reason', error)}") from None
        if len(body) > limit:
            raise CollectError(f"GET {label}: the response is larger than {limit} bytes")
        return body
