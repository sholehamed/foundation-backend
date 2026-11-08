#!/usr/bin/env python3
"""Read-only Foundation FR-13 staging preflight (stdlib; no credentials or writes)."""

import argparse
import json
import re
import ssl
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPSHandler, HTTPRedirectHandler, ProxyHandler, Request, build_opener


class PreflightError(ValueError):
    """A staging configuration or anonymous read-only probe failed."""


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        return None


def _hostname(name, origin):
    if not isinstance(origin, str):
        raise PreflightError(f"{name} must be an HTTPS origin")
    try:
        parsed = urlsplit(origin)
        port = parsed.port
    except ValueError as exc:
        raise PreflightError(f"{name} has an invalid port/URL") from exc
    if (parsed.scheme != "https" or not parsed.hostname or
            parsed.username is not None or parsed.password is not None or
            parsed.path not in ("", "/") or parsed.query or parsed.fragment or
            not parsed.netloc or origin != f"https://{parsed.netloc}"):
        raise PreflightError(f"{name} must be an HTTPS origin without path, credentials, query or fragment")
    host = parsed.hostname.lower()
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?", host):
        raise PreflightError(f"{name} contains an invalid hostname")
    if port is not None and port <= 0:
        raise PreflightError(f"{name} has invalid port")
    return host


def validate(config):
    """Fail-closed, offline validation; does not prove deployed values match manifest."""
    if not isinstance(config, dict) or config.get("environment") != "Staging":
        raise PreflightError("environment must be exactly 'Staging'")
    stage = config.get("stage")
    if stage not in ("baseline", "tenant-bff-pilot"):
        raise PreflightError("stage must be baseline or tenant-bff-pilot")
    zone = config.get("tenant_base_domain")
    if not isinstance(zone, str) or zone != zone.lower() or not re.fullmatch(
            r"[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?", zone):
        raise PreflightError("tenant_base_domain must be a valid lowercase DNS suffix")
    if not (zone.startswith("staging.") or zone.endswith(".test") or zone.endswith(".localhost")):
        raise PreflightError("tenant_base_domain needs a dedicated staging.* or .test/.localhost zone")
    hosts = {}
    for key in ("system_origin", "identity_origin", "tenant_origin"):
        hosts[key] = _hostname(key, config.get(key))
        if not hosts[key].endswith("." + zone):
            raise PreflightError(f"{key} must be a subdomain of tenant_base_domain")
        slug = hosts[key][:-(len(zone) + 1)]
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", slug):
            raise PreflightError(f"{key} must use exactly one valid subdomain label")
    if len(set(hosts.values())) != 3:
        raise PreflightError("system, identity, and tenant origins require distinct hosts")

    backend = config.get("backend")
    frontend = config.get("frontend")
    if not isinstance(backend, dict) or not isinstance(frontend, dict):
        raise PreflightError("backend and frontend flag sets are required")
    for section, keys in (
        (backend, ("bff_enabled", "tenant_cookie_bridge_enabled", "registry_apply_enabled")),
        (frontend, ("tenant_bff_pilot_enabled", "system_ui_access_pilot_enabled")),
    ):
        for key in keys:
            if type(section.get(key)) is not bool:
                raise PreflightError(f"{key} must be an explicit boolean")
    if backend["registry_apply_enabled"]:
        raise PreflightError("Feature Registry Apply must be OFF outside an explicitly approved manual window")
    if frontend["system_ui_access_pilot_enabled"]:
        raise PreflightError("System UI access pilot requires a separately reviewed catalog/grant gate")
    expected_pilot = stage == "tenant-bff-pilot"
    for key, actual in (
        ("backend.bff_enabled", backend["bff_enabled"]),
        ("backend.tenant_cookie_bridge_enabled", backend["tenant_cookie_bridge_enabled"]),
        ("frontend.tenant_bff_pilot_enabled", frontend["tenant_bff_pilot_enabled"]),
    ):
        if actual != expected_pilot:
            raise PreflightError(f"{key} must be {str(expected_pilot).lower()} at stage {stage}")
    return config


def _anonymous_get(opener, url):
    request = Request(url, headers={"Accept": "application/json", "User-Agent": "Foundation-Staging-Preflight/1"})
    try:
        with opener.open(request, timeout=8) as response:
            return response.status, response.headers, response.read(65537)
    except HTTPError as exc:
        with exc:
            return exc.code, exc.headers, exc.read(65537)
    except (OSError, URLError) as exc:
        raise PreflightError(f"anonymous HTTPS probe failed for {url}: {type(exc).__name__}") from exc


def probe(config, opener=None):
    """Only GET, no auth/cookies/redirects, system trust store; no writes or TLS bypass."""
    if config["stage"] != "tenant-bff-pilot":
        raise PreflightError("live probes require stage tenant-bff-pilot; baseline cannot expose /bff/session")
    opener = opener or build_opener(ProxyHandler({}), HTTPSHandler(context=ssl.create_default_context()), NoRedirect())
    identity_url = config["identity_origin"] + "/.well-known/openid-configuration"
    code, _, body = _anonymous_get(opener, identity_url)
    if code != 200 or len(body) > 65536:
        raise PreflightError("Identity OpenID discovery must return bounded HTTP 200 JSON")
    try:
        issuer = json.loads(body)["issuer"]
    except (ValueError, KeyError, TypeError) as exc:
        raise PreflightError("Identity discovery is missing a valid issuer") from exc
    if issuer.rstrip("/") != config["identity_origin"]:
        raise PreflightError("Identity discovery issuer does not match the configured identity origin")
    for origin_name in ("tenant_origin", "system_origin"):
        url = config[origin_name] + "/bff/session"
        status, headers, _ = _anonymous_get(opener, url)
        if status != 401:
            raise PreflightError(f"{origin_name} anonymous /bff/session must be HTTP 401 (got {status})")
        if "no-store" not in headers.get("Cache-Control", "").lower():
            raise PreflightError(f"{origin_name} /bff/session must set Cache-Control: no-store")
    status, _, _ = _anonymous_get(opener, config["tenant_origin"] + "/api/tenant/workspace")
    if status != 401:
        raise PreflightError(f"anonymous Tenant Workspace must be HTTP 401 (got {status})")
    return True


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, help="operator-reviewed, NON-SECRET staging manifest JSON")
    parser.add_argument("--probe", action="store_true", help="optional anonymous HTTPS read-only checks")
    args = parser.parse_args(argv)
    try:
        with open(args.config, encoding="utf-8") as file:
            config = json.load(file)
        validate(config)
        if args.probe:
            probe(config)
    except (PreflightError, OSError, json.JSONDecodeError) as exc:
        print(f"FR-13 staging gate: FAIL: {exc}", file=sys.stderr)
        return 2
    print("FR-13 staging gate: PASS (manifest validation" + (" + anonymous HTTPS probes)" if args.probe else " only; deployment unverified)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
