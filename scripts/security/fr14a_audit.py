#!/usr/bin/env python3
"""FR-14A static authorization *inventory*, not proof of live grant enforcement.

Checks declared System feature endpoints and Tenant/public boundary, produces a
machine-readable security matrix. Sources other than the Identity Endpoint
group are classified as separate protocols, never silently counted as covered.
No GitHub token, environment, DB, network, mutation or production Apply.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENDPOINTS = ROOT / "src/modules/Modules.System.Identity/Web/Endpoints"
FEATURES = ROOT / "src/modules/Modules.System.Identity/Web/Util/FeatureRegistryDefinitions.cs"
TENANT_CATALOG = ENDPOINTS / "TenantWorkspacePermissionCatalog.cs"
PUBLIC_TENANT_HANDLERS = {
    "ResolveTenantLogin",
    "StartFirstLogin",
    "CompleteFirstLogin",
    "ActivateInvitedMember",
}
SPECIAL_SYSTEM_HANDLERS = {
    "UiAccess": {"GetMyUiAccessSnapshot", "GetMyUiAccess"},
    "Tenants": {"TenantResolveBySlug"},
    # /system/Menus/me is a current-user, System/Tenant-scoped lookup with
    # its own live grant query; NOT a SystemAdministrator-only admin route.
    "Menus": {"GetMyNavigation"},
}
PROTOCOL_ENDPOINT_GROUPS = {"Auth", "Connect", "PublicSignup", "PublicPlans"}
TENANT_ENDPOINT_GROUPS = {"TenantWorkspace"}
# TenantWorkspace is intentionally NOT ignored: it is a mandatory scanned
# surface even though it is not part of the System Feature Registry.
SYSTEM_IGNORED = {"TenantWorkspacePermissionCatalog",
                  "TenantWorkspacePermissionEvaluator"}
# Legacy POST search uses a request body for paging but never changes grants.
REVIEWED_POST_READ_EXCEPTIONS = {"SYSTEM.GROUPS.MEMBERS-LIST"}


@dataclass(frozen=True)
class Row:
    surface: str
    source: str
    method: str
    handler: str
    endpoint_key: str | None
    permission_code: str | None
    security_boundary: str


class AuditError(RuntimeError):
    pass


def method_calls(source: str):
    """Yield registrations with balanced call expressions (not handler bodies)."""
    exp = re.compile(r"\.Map(Get|Post|Put|Patch|Delete|Methods|File)\s*\(")
    matches = list(exp.finditer(source))
    for ordinal, match in enumerate(matches):
        depth = 1
        index = match.end()
        quote = None
        while index < len(source) and depth > 0:
            char = source[index]
            if quote:
                if char == "\\" and index + 1 < len(source):
                    index += 2
                    continue
                if char == quote:
                    quote = None
            elif char in ('"', "'"):
                quote = char
            elif char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
            index += 1
        if depth != 0:
            raise AuditError("Unbalanced route registration")
        # Fluent MapGroup chains put the NEXT Map* before the final
        # semicolon. Never attribute AllowAnonymous/RequireAuthorization
        # on a later endpoint to the current endpoint.
        until = source.find(";", index)
        next_call = matches[ordinal + 1].start() if ordinal + 1 < len(matches) else len(source)
        tail_end = min(until if until >= 0 else len(source), next_call)
        tail = source[index:tail_end]
        body = source[match.end():index - 1]
        handler = re.match(r"\s*(?:\$?\"[^\"]+\"\s*,\s*)?(\w+)", body)
        yield match.group(1).upper(), (handler.group(1) if handler else "?"), body, tail


def catalog_permissions(source: str):
    section = source.split("IReadOnlyList<Feature> Features", 1)[1].split("UiElements =", 1)[0]
    features = {}
    regex = re.compile(
        r'new\("system",\s*"System",\s*"(?P<resource>[^"]+)"'
        r'.*?\[(?P<permissions>[^\]]+)\]\s*\)', re.DOTALL)
    for m in regex.finditer(section):
        resource = m.group("resource")
        permissions = set(re.findall(r'"([A-Z][A-Z0-9.-]+)"', m.group("permissions")))
        if not permissions or resource in features:
            raise AuditError("Malformed/duplicate code-owned System feature: " + resource)
        features[resource] = permissions
    if len(features) < 13:
        raise AuditError("System code-owned feature inventory missing/failed parse")
    return features


def metadata_from(body: str, source: str):
    # Current endpoints use either inline configure:e=>e.WithFeatureEndpoint()
    # or local Action<RouteHandlerBuilder> requireCreate/Update/etc delegates.
    direct = re.search(
        r'WithFeatureEndpoint\s*\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"', body)
    if direct:
        return direct.groups()
    indirect = re.search(r'configure\s*:\s*(require\w+)\b', body)
    if indirect:
        callback = re.search(
            r"Action<RouteHandlerBuilder>\s+" + re.escape(indirect.group(1)) +
            r"\s*=\s*\w+\s*=>\s*\{(?P<body>.*?)\};", source, re.DOTALL)
        if callback is None:
            raise AuditError("Missing configured System metadata callback: " +
                             indirect.group(1))
        match = re.search(
            r'WithFeatureEndpoint\s*\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"',
            callback.group("body"))
        if match:
            return match.groups()
    return None


def audit_sources(endpoint_sources: dict[str, str], feature_source: str,
                  tenant_catalog_source: str) -> list[Row]:
    features = catalog_permissions(feature_source)
    tenant_keys = set(re.findall(
        r'public const string \w+ = "(TENANT\.[^"]+)"', tenant_catalog_source))
    if len(tenant_keys) < 17:
        raise AuditError("Tenant key catalog missing or duplicate")
    seen = set()
    rows = []
    for name, source in sorted(endpoint_sources.items()):
        if name in SYSTEM_IGNORED:
            continue
        registrations = list(method_calls(source))
        if not registrations:
            if name not in ("TenantWorkspacePermissionEvaluator",
                            "TenantWorkspacePermissionCatalog"):
                raise AuditError(f"{name}: no route registration recognized")
            continue

        group_system = "MapGroup(this)" in source and (
            'RequireAuthorization("SystemAdministrator")' in source)
        if name not in PROTOCOL_ENDPOINT_GROUPS and name not in TENANT_ENDPOINT_GROUPS and not group_system:
            raise AuditError(name + ": System group lost SystemAdministrator policy")

        for method, handler, body, tail in registrations:
            meta = metadata_from(body, source)
            if name in TENANT_ENDPOINT_GROUPS:
                if name == "TenantWorkspace" and handler in PUBLIC_TENANT_HANDLERS:
                    if ".AllowAnonymous()" not in tail or ".RequireRateLimiting(" not in tail:
                        raise AuditError("Public Tenant first-login route missing rate-limit/anonymous declaration: " + handler)
                    boundary = "PUBLIC_THROTTLED"
                else:
                    if ".RequireAuthorization()" not in tail:
                        raise AuditError("Tenant workspace endpoint lost authentication: " + handler)
                    boundary = "TENANT_HANDLER_LIVE"
                if meta:
                    raise AuditError("Tenant workspace misclassified as System feature: " + handler)
                rows.append(Row("tenant", name + ".cs", method, handler, None, None, boundary))
                continue

            if name == "PublicPlans":
                if ".AllowAnonymous()" not in tail or '.RequireRateLimiting("public-plans")' not in tail:
                    raise AuditError("PublicPlans must be anonymous + throttled: " + handler)
                rows.append(Row("public", name + ".cs", method, handler, None, None,
                                "PUBLIC_PLANS_READONLY_THROTTLED"))
                continue

            if name == "PublicSignup":
                if ".AllowAnonymous()" not in tail or ".RequireRateLimiting(" not in tail:
                    raise AuditError("PublicSignup must be anonymous + throttled: " + handler)
                rows.append(Row("public", name + ".cs", method, handler, None, None,
                                "PUBLIC_SIGNUP_THROTTLED"))
                continue

            if name in ("Auth", "Connect"):
                # Login/captcha/cookie/OIDC exchanges are purpose-built auth
                # endpoints, NOT System admin feature endpoints. A separate
                # protocol review remains mandatory and is explicitly reported.
                rows.append(Row("identity_protocol", name + ".cs", method, handler,
                                None, None, "PROTOCOL_OWN_GATES"))
                continue

            if not group_system:
                raise AuditError("Expected SystemAdministrator group: " + name)
            if name in SPECIAL_SYSTEM_HANDLERS and handler in SPECIAL_SYSTEM_HANDLERS[name]:
                if meta:
                    raise AuditError("System internal exception unexpectedly cataloged")
                if name == "Tenants" and ".AllowAnonymous()" not in body:
                    raise AuditError("Tenant resolve exception missing explicit AllowAnonymous")
                if name == "Menus" and "app.MapGroup(this).RequireAuthorization()" not in source:
                    raise AuditError("Current-user navigation must require authenticated scope")
                boundary = ("PUBLIC_RESOLVE" if name == "Tenants" else
                            "CURRENT_USER_LIVE_NAV" if name == "Menus" else
                            "ADMIN_READ_INTERNAL")
                rows.append(Row("system_internal", name + ".cs", method, handler,
                                None, None, boundary))
                continue

            if not meta:
                raise AuditError(f"{name}.{handler}: missing registered System feature metadata")
            key, resource, permission = meta
            if not key.startswith("SYSTEM."):
                raise AuditError("Incorrect feature key scope: " + key)
            if resource not in features or permission not in features[resource]:
                raise AuditError("Unregistered/wrong-resource System feature: " +
                                 key + " -> " + resource + "/" + permission)
            if key in seen:
                raise AuditError("Duplicate System endpoint feature key: " + key)
            seen.add(key)
            if ".AllowAnonymous()" in body or ".AllowAnonymous()" in tail:
                raise AuditError("System feature endpoint cannot AllowAnonymous: " + key)
            if method in {"PUT", "PATCH", "DELETE"} and permission.endswith(".READ"):
                raise AuditError("Mutation accidentally mapped to READ: " + key)
            # POST paged searches are explicitly read-only; every POST mapped
            # to READ must be reviewed in this restricted set.
            if method == "POST" and permission.endswith(".READ") and (
                    not handler.lower().endswith(("pagedlist", "lookup")) and
                    key not in REVIEWED_POST_READ_EXCEPTIONS):
                raise AuditError("Review POST-to-READ endpoint: " + key)
            rows.append(Row("system", name + ".cs", method, handler,
                            key, permission, "SYSTEM_ADMIN_PLUS_GRANULAR_OPT_IN"))
    if len(rows) < 125:
        raise AuditError("Suspiciously small endpoint inventory; check parser/checkout")
    return rows


def audit_protocol_and_media(repo_root: Path) -> list[Row]:
    """Audit separately-owned BFF, authenticated media, dev-only Scalar maps.

    These cannot be given System Feature permissions. They remain explicit,
    non-granular boundaries and media ownership is NOT certified here.
    """
    result = []
    bff = repo_root / "src/modules/Modules.System.Bff"
    for file in sorted(bff.glob("*Endpoints*.cs")) + sorted(
            bff.glob("BffEndpointRouteBuilderExtensions.cs")):
        contents = file.read_text(encoding="utf-8-sig")
        if "BffEndpointRouteBuilderExtensions" in file.name and (
                'Identity:Bff:Enabled' not in contents):
            raise AuditError("BFF feature flag boundary removed")
        for method, handler, body, tail in method_calls(contents):
            if ".AllowAnonymous()" not in tail:
                raise AuditError("BFF protocol route changed cookie boundary: " +
                                 file.name + "/" + handler)
            result.append(Row("bff_protocol", file.name, method, handler,
                              None, None, "BFF_EXPLICIT_COOKIE_CSRF_LIFECYCLE"))

    media = (repo_root / "src/modules/Modules.System.FileStorage/Web/Endpoints/"
             "MediaStorageEndpoints.cs")
    media_source = media.read_text(encoding="utf-8-sig")
    for method, handler, body, tail in method_calls(media_source):
        if ".RequireAuthorization()" not in body + tail:
            raise AuditError("Media route lost RequireAuthorization: " + handler)
        result.append(Row("media", media.name, method, handler, None, None,
                          "AUTHENTICATED_MEDIA_OWNERSHIP_NOT_CERTIFIED"))

    host = (repo_root / "src/Web/DependencyInjections.cs").read_text(
        encoding="utf-8-sig")
    if "if (app.Environment.IsDevelopment())" not in host or (
            "app.MapScalarClientGenerationAssets();" not in host):
        raise AuditError("Dev-only Scalar route registration gate changed")
    scalar = (repo_root / "src/modules/Modules.System.Scalar.ClientGeneration/"
              "ScalarEndpointExtensions.cs")
    for method, handler, body, tail in method_calls(
            scalar.read_text(encoding="utf-8-sig")):
        result.append(Row("dev_tooling", scalar.name, method, handler,
                          None, None, "DEVELOPMENT_ONLY_OPENAPI_SCALAR"))
    if len(result) != 11:
        raise AuditError("BFF/media/developer protocol inventory unexpectedly changed: " +
                         str(len(result)))
    return result


def audit(repo_root: Path = ROOT) -> list[Row]:
    source_dir = repo_root / "src/modules/Modules.System.Identity/Web/Endpoints"
    files = {x.stem: x.read_text(encoding="utf-8-sig")
             for x in source_dir.glob("*.cs")}
    identity_rows = audit_sources(
        files,
        (repo_root / "src/modules/Modules.System.Identity/Web/Util/FeatureRegistryDefinitions.cs")
        .read_text(encoding="utf-8-sig"),
        (source_dir / "TenantWorkspacePermissionCatalog.cs").read_text(encoding="utf-8-sig"),
    )
    return identity_rows + audit_protocol_and_media(repo_root)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, help="Optional JSON artifact; no git write")
    args = parser.parse_args()
    try:
        rows = audit()
    except (AuditError, OSError, ValueError) as exc:
        print(f"FR-14A SECURITY INVENTORY FAILED: {exc}", file=sys.stderr)
        return 2
    payload = {
        "schemaVersion": 1,
        "scope": "identity-bff-media-dev-tools-source-maps",
        "limitations": [
            "Live System granular API grants require opt-in and reviewed Master catalog.",
            "Tenant route handler authorization is covered by separate HTTP/SQL tests.",
            "OAuth/CAPTCHA/BFF protocols require independent lifecycle validation.",
            "Authenticated media ID ownership is not proven by endpoint authentication.",
            "Scalar/OpenAPI tooling is gated to Development only in the Web host.",
            "This is a source contract audit, not production permission inventory.",
        ],
        "counts": {name: sum(x.surface == name for x in rows)
                   for name in sorted(set(x.surface for x in rows))},
        "rows": [asdict(x) for x in rows],
    }
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n",
                            encoding="utf-8")
    print("FR-14A source authorization inventory PASSED: " +
          json.dumps(payload["counts"], sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
