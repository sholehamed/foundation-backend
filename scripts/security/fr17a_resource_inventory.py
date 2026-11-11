#!/usr/bin/env python3
"""FR-17A-1: read-only, reproducible Module/Resource/EndpointGroup source inventory.

This deliberately REUSES FR-14A's strict route/authorization scanner rather than
inventing a second regex authorization policy. It does not connect to SQL, create
grants, change catalog definitions, or decide that a Tenant has purchased a module.

Use:
  python3 scripts/security/fr17a_resource_inventory.py --out /tmp/fr17a-resources.json

The output is source evidence only. Existing persisted DB reconciliation must
use the separate read-only SQL preflight and an operator-approved data review.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

from fr14a_audit import (
    ROOT, AuditError, ENDPOINTS, FEATURES, TENANT_CATALOG,
    audit_protocol_and_media, audit_sources, catalog_permissions,
    metadata_from, method_calls,
)

# Shared business Resource is an intentionally reviewed exception to approximate
# one Controller/EndpointGroup per Resource. Any additional sharing must be
# investigated rather than silently accepting broad Resource ownership.
REVIEWED_SHARED_RESOURCES = {
    "menu-management": {"Menus.cs", "MenuCategories.cs"},
}
PLATFORM_CORE = "platform_core"
TENANT_CORE = "tenant_core_administration"
PROTOCOL = "identity_protocol"
PUBLIC = "public_provisioning"
STORAGE = "authenticated_media"
DEVELOPMENT = "development_only"
BFF = "bff_protocol"


def read_sources(root: Path = ROOT) -> dict[str, str]:
    base = root / "src/modules/Modules.System.Identity/Web/Endpoints"
    sources = {p.stem: p.read_text(encoding="utf-8-sig")
               for p in sorted(base.glob("*.cs"))}
    # Audit the host-owned privileged trace endpoint as a real System
    # resource. Never treat it as an unbound/automatically saleable module.
    tower = root / "src/Web/ControlTowerEndpoints.cs"
    if tower.exists():
        host = (root / "src/Web/DependencyInjections.cs").read_text(encoding="utf-8-sig")
        if "app.MapControlTower();" not in host:
            raise AuditError("Control Tower host endpoint is not mapped")
        sources[tower.stem] = tower.read_text(encoding="utf-8-sig")
    return sources


def tenant_permissions(source: str) -> list[str]:
    codes = re.findall(
        r'public const string\s+\w+\s*=\s*"(TENANT\.[^"]+)"',
        source,
    )
    if len(codes) != 17 or len(set(codes)) != 17:
        raise AuditError(
            "FR-17A expected 17 unique central Tenant administrative permissions; "
            "review changes before classifying commercial modules")
    return sorted(codes)


def build_source_inventory(
    endpoint_sources: dict[str, str],
    feature_source: str,
    tenant_catalog_source: str,
    ancillary_rows: list | None = None,
) -> dict:
    """Produces a deterministic, validated source catalog; takes no DB input."""
    audited = audit_sources(
        endpoint_sources, feature_source, tenant_catalog_source)
    ancillaries = ancillary_rows or []
    features = catalog_permissions(feature_source)
    operations: dict[str, list[dict]] = defaultdict(list)

    for source_name, source in sorted(endpoint_sources.items()):
        for method, handler, body, _tail in method_calls(source):
            metadata = metadata_from(body + " " + _tail, source)
            if metadata is None:
                continue
            endpoint_key, resource, permission = metadata
            if resource not in features or permission not in features[resource]:
                raise AuditError(
                    "FR-17A metadata owner mismatch: " + endpoint_key)
            operations[resource].append({
                "endpointKey": endpoint_key,
                "endpointGroup": source_name + ".cs",
                "method": method,
                "handler": handler,
                "permissionCode": permission,
            })

    # FR-14A protects metadata uniqueness and policies; make sure our
    # commercial grouping projection has not silently dropped any endpoint.
    registered = {
        (op["endpointKey"], op["permissionCode"])
        for items in operations.values() for op in items
    }
    authorized = {
        (row.endpoint_key, row.permission_code)
        for row in audited if row.surface == "system"
    }
    if registered != authorized or (
        sum(len(items) for items in operations.values()) != len(authorized)
    ):
        raise AuditError(
            "FR-17A System EndpointGroup metadata projection diverged from "
            "FR-14A authenticated route inventory")

    result_resources = []
    for resource, permissions in sorted(features.items()):
        ops = sorted(
            operations[resource],
            key=lambda item: (item["endpointGroup"], item["endpointKey"]),
        )
        if not ops:
            raise AuditError("FR-17A unbound System resource: " + resource)
        groups = sorted({x["endpointGroup"] for x in ops})
        if len(groups) > 1 and set(groups) != REVIEWED_SHARED_RESOURCES.get(
            resource, set()
        ):
            raise AuditError(
                "FR-17A new unreviewed cross-EndpointGroup Resource: "
                + resource + " -> " + ",".join(groups))
        result_resources.append({
            "moduleCode": "system",
            "moduleClass": PLATFORM_CORE,
            "resourceCode": resource,
            "endpointGroups": groups,
            "crossGroupException": len(groups) > 1,
            "permissionCodes": sorted(permissions),
            "registeredEndpoints": ops,
        })

    external_surfaces = []
    classes = {
        "tenant": TENANT_CORE,
        "public": PUBLIC,
        "identity_protocol": PROTOCOL,
        "system_internal": PLATFORM_CORE,
        "bff_protocol": BFF,
        "media": STORAGE,
        "dev_tooling": DEVELOPMENT,
    }
    for row in sorted(
        [*audited, *ancillaries],
        key=lambda item: (item.surface, item.source, item.handler, item.method),
    ):
        if row.surface == "system":
            continue
        if row.surface not in classes:
            raise AuditError("FR-17A unknown route classification: " + row.surface)
        external_surfaces.append({
            "source": row.source,
            "endpointGroup": row.source,
            "surface": row.surface,
            "moduleClass": classes[row.surface],
            "httpMethod": row.method,
            "handler": row.handler,
            "securityBoundary": row.security_boundary,
            "entitlementBindingStatus": "not_applicable_core_or_unreviewed",
        })

    counts = Counter(row.surface for row in [*audited, *ancillaries])
    return {
        "schemaVersion": 1,
        "evidence": "source-only; live SQL, runtime EndpointDataSource and existing data not verified",
        "commercialEntitlementEnforced": False,
        "systemResources": result_resources,
        "tenantAdminCatalog": {
            "moduleCode": "tenant-workspace",
            "resourceCode": "tenant-administration",
            "moduleClass": TENANT_CORE,
            "permissionCodes": tenant_permissions(tenant_catalog_source),
            "reviewRequired": (
                "17 current Tenant administrative capabilities share one "
                "Resource; align actual endpoint groups and permission handlers "
                "before any reviewed migration"),
        },
        "otherEndpoints": external_surfaces,
        "counts": {
            "systemResources": len(result_resources),
            "systemEndpointRegistrations": len(authorized),
            "otherEndpointRegistrations": len(external_surfaces),
            "routesBySurface": dict(sorted(counts.items())),
        },
        "knownGaps": [
            "No persisted SQL database was inspected by this inventory",
            "Tenant workspace handler-to-Resource mapping is not yet exact",
            "TenantModules is legacy flags, not a paid subscription",
            "BusinessProfile.RequestedPlanKey is signup intent only",
            "Current effective permissions do not enforce a commercial plan",
            "Commercial operation-to-Feature/Limit bindings not implemented",
            "BFF/OIDC/media/dev endpoints are separately classified, not sold",
        ],
    }


def generate(root: Path = ROOT) -> dict:
    sources = read_sources(root)
    feature = (root / "src/modules/Modules.System.Identity/Web/Util/"
               "FeatureRegistryDefinitions.cs").read_text(encoding="utf-8-sig")
    tenant = (root / "src/modules/Modules.System.Identity/Web/Endpoints/"
              "TenantWorkspacePermissionCatalog.cs").read_text(encoding="utf-8-sig")
    return build_source_inventory(
        sources, feature, tenant, audit_protocol_and_media(root))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, help="Write UTF-8 JSON artifact")
    args = parser.parse_args()
    try:
        result = generate()
    except (AuditError, OSError, ValueError, KeyError) as exc:
        print("FR-17A SOURCE INVENTORY FAILED: " + str(exc), file=sys.stderr)
        return 1

    encoded = json.dumps(
        result, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(encoded, encoding="utf-8")
    else:
        sys.stdout.write(encoded)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
