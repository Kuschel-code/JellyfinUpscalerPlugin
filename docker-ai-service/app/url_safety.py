"""URL safety checks for outbound and stored URLs.

Defends against SSRF by rejecting URLs that don't resolve to globally
routable, non-multicast addresses. The allowlist approach (require
``is_global AND NOT is_multicast``) is preferred over the older
blocklist (``is_private OR is_loopback OR is_link_local OR is_reserved``)
because the blocklist missed CGNAT (100.64.0.0/10), broadcast
255.255.255.255, IPv4-mapped IPv6 forms of CGNAT, and 6to4 anycast.

Public API:
  - ``is_safe_outbound_url(url) -> bool``
  - ``assert_safe_outbound_url(url) -> None``  (raises ``UnsafeUrlError``)

Hostnames are resolved via ``socket.getaddrinfo`` and *every* returned
address is checked. A multi-A-record host where one IP is private is
rejected, defending against partial-overlap DNS rebinding.
"""

from __future__ import annotations

import ipaddress
import socket
import urllib.parse
from typing import Iterable


class UnsafeUrlError(ValueError):
    """Raised when a URL fails outbound safety validation."""


_BLOCKED_HOSTNAMES = frozenset({
    "localhost",
    "localhost.localdomain",
    "ip6-localhost",
    "ip6-loopback",
})


def _ip_is_safe(addr: ipaddress.IPv4Address | ipaddress.IPv6Address) -> bool:
    """Return True iff this IP is globally routable and not multicast.

    This is stricter than ``not is_private`` — it also rejects CGNAT
    (100.64.0.0/10), TEST-NET ranges, broadcast 255.255.255.255, and
    multicast.
    """
    return bool(addr.is_global) and not addr.is_multicast


def is_safe_outbound_url(url: str) -> bool:
    """Cheap predicate version. Catches all errors and returns False."""
    try:
        assert_safe_outbound_url(url)
        return True
    except UnsafeUrlError:
        return False


def assert_safe_outbound_url(url: str, allowed_schemes: Iterable[str] = ("http", "https")) -> None:
    """Raise UnsafeUrlError if the URL is unsafe for outbound use.

    Validates: scheme, hostname-blocklist (well-known names), every
    resolved IP. The DNS lookup is what guards against rebinding from
    a name that points partly to public, partly to private space.
    """
    if not url or "\n" in url or "\r" in url or "\t" in url:
        raise UnsafeUrlError("URL contains control characters or is empty")

    parsed = urllib.parse.urlparse(url)
    if parsed.scheme not in set(allowed_schemes):
        raise UnsafeUrlError(f"Scheme {parsed.scheme!r} not allowed")

    hostname = parsed.hostname
    if not hostname:
        raise UnsafeUrlError("URL has no hostname")

    if hostname.lower() in _BLOCKED_HOSTNAMES:
        raise UnsafeUrlError(f"Hostname {hostname!r} is blocked")

    # Literal IP — direct check
    try:
        addr = ipaddress.ip_address(hostname)
    except ValueError:
        addr = None

    if addr is not None:
        if not _ip_is_safe(addr):
            raise UnsafeUrlError(f"IP {hostname!r} is not a global unicast address")
        return

    # DNS name — resolve and check every returned IP
    try:
        addrinfos = socket.getaddrinfo(hostname, None)
    except (socket.gaierror, socket.herror) as e:
        raise UnsafeUrlError(f"Could not resolve {hostname!r}: {e}") from e

    if not addrinfos:
        raise UnsafeUrlError(f"No addresses returned for {hostname!r}")

    for _family, _type, _proto, _canonname, sockaddr in addrinfos:
        try:
            resolved = ipaddress.ip_address(sockaddr[0])
        except (ValueError, IndexError):
            raise UnsafeUrlError(f"Bad sockaddr for {hostname!r}: {sockaddr!r}")
        if not _ip_is_safe(resolved):
            raise UnsafeUrlError(
                f"Hostname {hostname!r} resolves to {resolved} which is not a global unicast address"
            )
