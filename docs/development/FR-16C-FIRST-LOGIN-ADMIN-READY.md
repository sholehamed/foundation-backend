# FR-16C — Fully usable admin menus on first login

Date: 2026-11-10. Purpose: ensure the app is **usable immediately after a fresh installation**, without an administrator having to navigate to a page they cannot access in order to grant access to themselves.

## First-start contract

**System administrator**: on a clean database, startup migrations and FR-16 initial catalog seed register the 13 code-owned global System menu roots, all code-owned System PAGE/ACTION permission definitions and one Master-scoped active `SUPERADMIN` role with all such privileges. No Tenant permissions are assigned to System roles. On **Development**, startup's existing `admin` User is assigned this role and can immediately log in and navigate all 13 roots. System Angular sidebar always uses the live, current-user-filtered `GET /api/system/Menus/me`; it never grants rights on the browser.

**Tenant administrator**: a Tenant does not exist before registration, so startup must **not** create an arbitrary real Tenant with predictable demo credentials. On the *first public signup of each Tenant*, its Owner User, scoped `Owner` role, UserRole membership, 17 centrally defined Tenant PAGE/ACTION RolePermissions, main Branch and profile are committed atomically. On the Owner's **first login**, all six central Tenant menu roots and all 17 privileges are available immediately, without self-grant. The 13 System menus must never leak into a Tenant session. The runtime's dynamic Tenant navigation flag remains default-OFF on shipped frontend until an independently approved rollout; the fallback sidebar still uses live `ui-access/me` and displays the same six authorized links. The Actions-only dynamic navigation pilot has already passed with the genuine seed.

## Bootstrap the first System login on Production/Staging

No predictable Production password is created. For a brand-new installation, set **deployment secrets** (not code, source control or public environment files):

```text
Identity__Bootstrap__InitialSystemAdmin__Enabled=true
Identity__Bootstrap__InitialSystemAdmin__UserName=<chosen-unique-system-admin>
Identity__Bootstrap__InitialSystemAdmin__Email=<chosen-email>
Identity__Bootstrap__InitialSystemAdmin__Password=<strong-generated-secret>
```

The password must be 15–128 characters with letters, digits and symbols and must not contain the username. Use a unique credential from a secret manager; never commit or log it. This is an explicit **one-time** provisioning action on a new, Master-only database with the complete preseeded System menu/role/permission catalog. Once a System user exists—even if disabled—startup never creates another or restores deleted UserRole grants. **Remove the bootstrap secret and disable the flag immediately after successful first sign-in**; later administrator changes use the regular reviewed System Admin flows. Do not configure these values on real existing databases or tenant hosts. The first administrator has real standard credentials and no bypass in authorization endpoints.

This feature does not give administrators unchecked abilities beyond code-owned permissions. A disabled subpage/blocked feature remains disabled; "all" means **all currently available System/Tenant management surfaces**, not forcing an intentionally disabled identity writer/UI workflow active.

If initial settings are missing, no unsafe default admin is manufactured in Production. The app will have a bootstrapped System role/catalog but **cannot be logged into as a System administrator until the approved one-time credential bootstrap is performed**. Supplying secrets via deployment configuration is necessary.

## Mandatory real acceptance

`tests/http/public_signup_e2e.py` runs with disposable loopback SQL and signed OpenIddict auth inside the standard `Tenant IAM real HTTP + SQL E2E` Actions workflow. It verifies:
- First login for seeded Development `admin`: exactly 13 System menu roots; every one of the 13 System root UI PAGE access decisions `allowed: true`; Tenant Workspace 403.
- Real public Tenant signup, no fake Owner fixture: first login with newly-created Owner; exactly six global Tenant menu roots from both menu endpoints; **17/17** Tenant PAGE/ACTION access entries allowed; all main Tenant Admin capabilities and protected Members/Roles/Groups/Branches GETs 200, System UI 403.
- Concurrent/repeated signup cannot create a duplicate Tenant.
- Dedicated actual SQL Server Feature Registry tests verify the one-time non-development System credential bootstrap accepts strong passwords, creates an active System User and Master UserRole with all code-owned permissions, and **never** reinstates an intentionally deactivated user or revoked role on restart. Weak credentials or missing role grant fail closed without user creation.

All tests run only against disposable Development/CI SQL. They are not evidence of a remote deployment, and no global Tenant catalog authoring by Tenant Owners is introduced.
