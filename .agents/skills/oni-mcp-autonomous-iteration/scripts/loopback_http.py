#!/usr/bin/env python3
"""HTTP helpers that bypass ambient proxies only for explicit loopback targets."""

import ipaddress
import urllib.error
import urllib.request
from urllib.parse import urlsplit


_DIRECT_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def is_loopback_url(url):
    try:
        parsed = urlsplit(url)
    except ValueError:
        return False
    if parsed.scheme not in {"http", "https"}:
        return False
    host = parsed.hostname
    if not host:
        return False
    if host.lower() == "localhost":
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def _request_url(request_or_url):
    if isinstance(request_or_url, urllib.request.Request):
        return request_or_url.full_url
    return str(request_or_url)


def open_url(request_or_url, timeout=None):
    """Open a URL, bypassing process proxy settings only for loopback hosts."""
    url = _request_url(request_or_url)
    direct = is_loopback_url(url)
    try:
        if direct:
            return _DIRECT_OPENER.open(request_or_url, timeout=timeout)
        return urllib.request.urlopen(request_or_url, timeout=timeout)
    except urllib.error.HTTPError as exc:
        raise RuntimeError(f"HTTP {exc.code} from {url}: {exc.reason}") from exc
    except urllib.error.URLError as exc:
        scope = "loopback MCP" if direct else "HTTP"
        raise RuntimeError(f"{scope} request to {url} failed: {exc.reason}") from exc
    except TimeoutError as exc:
        scope = "loopback MCP" if direct else "HTTP"
        raise RuntimeError(f"{scope} request to {url} timed out") from exc
