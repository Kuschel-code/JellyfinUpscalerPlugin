"""Tests for the URL safety helpers (SSRF defense)."""
from unittest.mock import patch

import pytest

from app.url_safety import UnsafeUrlError, assert_safe_outbound_url, is_safe_outbound_url


class TestSchemeAndShape:
    def test_rejects_empty_url(self):
        with pytest.raises(UnsafeUrlError):
            assert_safe_outbound_url("")

    def test_rejects_control_characters(self):
        with pytest.raises(UnsafeUrlError, match="control characters"):
            assert_safe_outbound_url("http://example.com\nHost: evil.com")

    def test_rejects_file_scheme(self):
        with pytest.raises(UnsafeUrlError, match="Scheme"):
            assert_safe_outbound_url("file:///etc/passwd")

    def test_rejects_gopher_scheme(self):
        with pytest.raises(UnsafeUrlError, match="Scheme"):
            assert_safe_outbound_url("gopher://example.com:70/")

    def test_rejects_url_without_hostname(self):
        with pytest.raises(UnsafeUrlError):
            assert_safe_outbound_url("http://")


class TestHostnameBlocklist:
    @pytest.mark.parametrize("name", [
        "localhost",
        "LOCALHOST",
        "localhost.localdomain",
        "ip6-localhost",
        "ip6-loopback",
    ])
    def test_blocks_well_known_hostnames(self, name):
        with pytest.raises(UnsafeUrlError, match="blocked"):
            assert_safe_outbound_url(f"http://{name}/x")


class TestLiteralIPv4:
    @pytest.mark.parametrize("ip", [
        "127.0.0.1",          # loopback
        "10.0.0.5",           # RFC 1918
        "172.16.0.1",         # RFC 1918
        "192.168.1.1",        # RFC 1918
        "169.254.169.254",    # link-local (cloud metadata service)
        "100.64.0.1",         # CGNAT — the gap the old code missed
        "100.127.255.254",    # CGNAT upper edge
        "0.0.0.0",            # this-net / unspecified
        "255.255.255.255",    # broadcast
        "224.0.0.1",          # multicast
    ])
    def test_rejects_non_global_ipv4(self, ip):
        with pytest.raises(UnsafeUrlError):
            assert_safe_outbound_url(f"http://{ip}/")

    @pytest.mark.parametrize("ip", [
        "8.8.8.8",
        "1.1.1.1",
        "100.63.255.255",     # one below CGNAT — still public
        "100.128.0.0",        # one above CGNAT — public
    ])
    def test_allows_global_ipv4(self, ip):
        assert is_safe_outbound_url(f"http://{ip}/")


class TestLiteralIPv6:
    @pytest.mark.parametrize("ip", [
        "[::1]",                          # loopback
        "[fe80::1]",                      # link-local
        "[::ffff:127.0.0.1]",             # IPv4-mapped loopback
        "[::ffff:192.168.1.1]",           # IPv4-mapped RFC 1918
        "[::ffff:100.64.0.1]",            # IPv4-mapped CGNAT
        "[fc00::1]",                      # Unique-local fc00::/7
    ])
    def test_rejects_non_global_ipv6(self, ip):
        with pytest.raises(UnsafeUrlError):
            assert_safe_outbound_url(f"http://{ip}/")

    @pytest.mark.parametrize("ip", [
        "[2001:4860:4860::8888]",         # Google Public DNS
        "[2606:4700:4700::1111]",         # Cloudflare
        "[::ffff:8.8.8.8]",               # IPv4-mapped global
    ])
    def test_allows_global_ipv6(self, ip):
        assert is_safe_outbound_url(f"http://{ip}/")


class TestDnsResolution:
    @patch("app.url_safety.socket.getaddrinfo")
    def test_rejects_when_dns_resolves_to_private(self, mock_gai):
        # Simulates a hostname that resolves to RFC1918 (e.g. attacker DNS rebinding)
        mock_gai.return_value = [(2, 1, 6, "", ("192.168.1.50", 0))]
        with pytest.raises(UnsafeUrlError, match="not a global"):
            assert_safe_outbound_url("http://attacker.example/x")

    @patch("app.url_safety.socket.getaddrinfo")
    def test_rejects_when_one_of_many_dns_results_is_private(self, mock_gai):
        # Mixed-address attack: one public, one private — must reject the whole host
        mock_gai.return_value = [
            (2, 1, 6, "", ("8.8.8.8", 0)),
            (2, 1, 6, "", ("169.254.169.254", 0)),
        ]
        with pytest.raises(UnsafeUrlError, match="not a global"):
            assert_safe_outbound_url("http://attacker.example/x")

    @patch("app.url_safety.socket.getaddrinfo")
    def test_allows_when_all_dns_results_are_public(self, mock_gai):
        mock_gai.return_value = [
            (2, 1, 6, "", ("8.8.8.8", 0)),
            (10, 1, 6, "", ("2001:4860:4860::8888", 0, 0, 0)),
        ]
        assert is_safe_outbound_url("https://example.com/x")

    @patch("app.url_safety.socket.getaddrinfo", side_effect=__import__("socket").gaierror("nope"))
    def test_rejects_unresolvable_hostname(self, _mock_gai):
        with pytest.raises(UnsafeUrlError, match="Could not resolve"):
            assert_safe_outbound_url("http://nonexistent.invalid/")
