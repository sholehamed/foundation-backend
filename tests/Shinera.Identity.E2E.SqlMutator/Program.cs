using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Microsoft.Data.SqlClient;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Web.Util;
using Modules.System.Identity.Web.Endpoints;

// Deliberately separate from the Web API: no development mutation endpoint,
// authentication bypass, or production-registered service is introduced.
// This tool is run only from the disposable GitHub SQL Server HTTP E2E step.
const string optIn = "stale-jwt-ci-only";
var databaseName = Environment.GetEnvironmentVariable("DBNAME") ?? "";
var server = Environment.GetEnvironmentVariable("DataBaseServer") ?? "";
var isEnsureDatabase = args.Length == 1 && args[0] == "ensure-e2e-database";
var isTenantFixture = args.Length == 1 && args[0] == "seed-tenant-iam";
var isTenantAudit = args.Length == 1 && args[0] == "assert-tenant-iam-audit";
var isNavigationFixture = args.Length == 1 && args[0] == "seed-central-tenant-navigation";
var isRevoke = args.Length == 2 && args[0] == "revoke-seeded-superadmin";
var isAuditCheck = args.Length == 3 && args[0] == "assert-denied-audit";
Guid subject = Guid.Empty;
var subjectValid = (isEnsureDatabase || isTenantFixture || isTenantAudit || isNavigationFixture) ||
    (args.Length >= 2 && Guid.TryParse(args[1], out subject) &&
     subject != Guid.Empty);
if (!(isRevoke || isAuditCheck || isEnsureDatabase || isTenantFixture || isTenantAudit || isNavigationFixture) ||
    !subjectValid ||
    Environment.GetEnvironmentVariable("SHINERA_E2E_DB_MUTATION") != optIn ||
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" ||
    Environment.GetEnvironmentVariable("Identity__E2E__Enabled") != "true" ||
    !databaseName.StartsWith("Shinera_E2E_", StringComparison.Ordinal) ||
    server is not ("127.0.0.1,1433" or "localhost,1433"))
{
    throw new InvalidOperationException(
        "The CI-only database mutator requires an explicit disposable Development database and valid subject.");
}

// The central Menu writer is test-only and has an additional hard gate:
// official trusted-main browser runner plus exact disposable SQL catalog.
// Never permit this helper on a remote database or an arbitrary Development DB.
if (isNavigationFixture &&
    (Environment.GetEnvironmentVariable("FR15D_E2E_DYNAMIC_NAVIGATION") != "true" ||
     Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
     Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "sholehamed/foundation-backend" ||
     Environment.GetEnvironmentVariable("GITHUB_REF") != "refs/heads/main" ||
     databaseName != "Shinera_E2E_RealBffBrowser"))
    throw new InvalidOperationException(
        "Dynamic Menu fixture only allowed in trusted main disposable browser E2E.");

// All commands are gated to this disposable local prefix. The Tenant fixture
// commands are intentionally test-only: no new Web API or production seed.
var user = Environment.GetEnvironmentVariable("DataBaseUsername");
var password = Environment.GetEnvironmentVariable("DataBasePassword");
if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
    throw new InvalidOperationException("Missing disposable SQL Server credentials.");

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = server,
    InitialCatalog = isEnsureDatabase ? "master" : databaseName,
    UserID = user,
    Password = password,
    Encrypt = SqlConnectionEncryptOption.Optional,
    TrustServerCertificate = true
}.ConnectionString;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

if (isEnsureDatabase)
{
    // Creating a database is never allowed by arbitrary user data. This
    // invocation is unavailable from the HTTP runtime and restricted to a
    // disposable Development SQL Server on loopback.
    if (!databaseName.StartsWith("Shinera_E2E_", StringComparison.Ordinal) ||
        databaseName.Length > 64 ||
        databaseName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
        throw new InvalidOperationException("Invalid disposable SQL database name.");

    await using var provision = new SqlCommand("""
        IF DB_ID(@name) IS NULL
        BEGIN
            DECLARE @statement nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name);
            EXEC sys.sp_executesql @statement;
        END
        """, connection);
    provision.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = databaseName;
    await provision.ExecuteNonQueryAsync();
    Console.WriteLine("PASS: disposable Tenant IAM SQL database exists.");
    return;
}


if (isNavigationFixture)
{
    var options = new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(connectionString).Options;
    await using var db = new IdentityDbContext(options, new TenantContext());
    await using var menuTransaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable);

    var reviewed = TenantWorkspaceNavigationService.RoutePermissions;
    if (reviewed.Count != 6)
        throw new InvalidOperationException("The reviewed six-route contract changed.");

    // On a genuinely fresh install FR-16 has already seeded the central
    // catalog. Reuse and verify it; do NOT try to create duplicate rows.
    // The older CI-only fixture path remains only for legacy disposable SQL.
    var presentRoutes = await db.Menus.AsNoTracking()
        .Where(x => x.Route != null && reviewed.Keys.Contains(x.Route))
        .Select(x => x.Route!).ToListAsync();
    if (presentRoutes.Count != 0)
    {
        var existing = await CentralTenantNavigationReadiness.ReadAsync(db);
        if (presentRoutes.Count != 6 ||
            presentRoutes.Distinct(StringComparer.Ordinal).Count() != 6 ||
            !existing.ReadyForDynamicPilot || existing.ReadyRoutes != 6)
            throw new InvalidOperationException(
                "Existing central Tenant menu bootstrap is partial or inconsistent.");
        Console.WriteLine(
            "PASS: six System-owned Tenant Menu roots already present from initial seed; no additional writes.");
        await menuTransaction.CommitAsync();
        return;
    }

    var roleGrantsBefore = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
        .CountAsync();
    var directGrantsBefore = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
        .CountAsync();

    var category = new MenuCategory(Guid.NewGuid())
    {
        Title = "CI-only central Tenant navigation",
        Order = 40,
        IsActive = true,
        RowVersion = [1]
    };
    db.MenuCategories.Add(category);
    var ordinal = 0;
    foreach (var (route, permissionCode) in reviewed)
    {
        Guid? permissionId = null;
        if (permissionCode is not null)
        {
            var definitions = await db.Permissions.Where(p =>
                p.Code == permissionCode && p.IsActive)
                .Select(p => p.Id).ToListAsync();
            if (definitions.Count != 1)
                throw new InvalidOperationException(
                    "Reviewed Tenant Permission missing or ambiguous.");
            permissionId = definitions[0];
        }
        db.Menus.Add(new Menu(Guid.NewGuid())
        {
            CategoryId = category.Id,
            Title = route switch
            {
                "/dashboard" => "داشبورد",
                "/dashboard/business" => "کسب‌وکار",
                "/dashboard/branches" => "شعبه‌ها",
                "/dashboard/members" => "اعضا",
                "/dashboard/roles" => "نقش‌ها",
                "/dashboard/groups" => "گروه‌ها",
                _ => throw new InvalidOperationException("Unregistered Tenant route.")
            },
            Route = route,
            Icon = "dashboard",
            PermissionId = permissionId,
            IsActive = true,
            IsHidden = false,
            Order = (short)(ordinal++ * 10),
            RowVersion = [1]
        });
    }
    await db.SaveChangesAsync();
    var readiness = await CentralTenantNavigationReadiness.ReadAsync(db);
    if (!readiness.ReadyForDynamicPilot ||
        readiness.ReadyRoutes != 6 || readiness.RequiredRoutes != 6 ||
        await db.RolePermissions.IgnoreQueryFilters(["tenant"]).CountAsync() != roleGrantsBefore ||
        await db.UserPermissions.IgnoreQueryFilters(["tenant"]).CountAsync() != directGrantsBefore)
        throw new InvalidOperationException(
            "Central Tenant menu fixture did not meet six-route readiness or changed grants.");
    await menuTransaction.CommitAsync();
    Console.WriteLine("PASS: exactly six System-owned Tenant Menu roots seeded in isolated browser SQL; no grants.");
    return;
}

if (isTenantFixture)
{
    var passwordForFixture = Environment.GetEnvironmentVariable(
        "SHINERA_DEVELOPMENT_ADMIN_PASSWORD");
    if (string.IsNullOrWhiteSpace(passwordForFixture) || passwordForFixture.Length < 12)
        throw new InvalidOperationException("Explicit CI-only fixture password required.");

    var tenantContext = new TenantContext();
    var options = new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(connectionString).Options;
    await using var db = new IdentityDbContext(options, tenantContext);
    await using var tx = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable);
    var tenant = await db.Tenants.SingleAsync(x =>
        x.Slug == "e2e-http-ci" && x.IsActive);
    var owner = await db.Users.SingleAsync(x =>
        x.TenantId == tenant.Id && x.UserType == UserType.Tenant &&
        x.NormalizedUserName == "E2E-TENANT" && x.IsActive);
    tenantContext.InitializeTenant(owner.Id, tenant.Id, [tenant.Id], [tenant.Id], tenant.Id);
    var ownerRole = await db.Roles.SingleOrDefaultAsync(x =>
        x.TenantId == tenant.Id && x.NormalizedName == "OWNER");
    if (ownerRole is null)
    {
        ownerRole = new Role(tenant.Id, "Owner", "Disposable E2E fixture Owner")
            { Scope = RoleScope.Tenant };
        db.Roles.Add(ownerRole);
        await db.SaveChangesAsync();
    }
    if (ownerRole.Scope != RoleScope.Tenant || !ownerRole.IsActive ||
        await db.Roles.CountAsync(x =>
            x.TenantId == tenant.Id && x.NormalizedName == "OWNER") != 1)
        throw new InvalidOperationException("Ambiguous fixture Owner definition.");
    if (!await db.UserRoles.AnyAsync(x => x.UserId == owner.Id &&
        x.TenantId == tenant.Id && x.RoleId == ownerRole.Id))
    {
        db.UserRoles.Add(new UserRole(tenant.Id, owner.Id, ownerRole.Id));
        await db.SaveChangesAsync();
    }

    // The old disposable fixture created an Owner role with ZERO
    // RolePermissions, unlike FR-16 real Tenant signup (17 initial scoped
    // grants). That bypassed the legacy Owner fallback in endpoint evaluation
    // but hid the three gated links in database-driven Angular navigation.
    // Seed exactly the reviewed code-owned Tenant permissions, never System.
    // This path is deliberately unavailable from the application runtime.
    var reviewedCodes = TenantWorkspacePermissionCatalog.Definitions
        .Select(x => x.Key).ToArray();
    if (reviewedCodes.Length != 17)
        throw new InvalidOperationException("Reviewed Tenant initial access catalog changed.");

    var tenantPermissions = await db.Permissions.AsNoTracking()
        .Where(p => reviewedCodes.Contains(p.Code) && p.IsActive &&
            p.Resource != null &&
            p.Resource.IsActive &&
            p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
            p.Resource.Module != null &&
            p.Resource.Module.IsActive &&
            p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode)
        .Select(p => new { p.Id, p.Code }).ToArrayAsync();
    if (tenantPermissions.Length != reviewedCodes.Length ||
        !tenantPermissions.Select(p => p.Code).ToHashSet(StringComparer.Ordinal)
            .SetEquals(reviewedCodes))
        throw new InvalidOperationException(
            "Disposable Owner grant fixture requires all 17 globally owned Tenant definitions.");

    var existingGrants = await db.RolePermissions.AsNoTracking()
        .Where(x => x.TenantId == tenant.Id && x.RoleId == ownerRole.Id)
        .Select(x => x.PermissionId).ToArrayAsync();
    var expectedIds = tenantPermissions.Select(p => p.Id).ToHashSet();
    if (existingGrants.Length != 0 &&
        (existingGrants.Length != expectedIds.Count ||
         !existingGrants.ToHashSet().SetEquals(expectedIds)))
        throw new InvalidOperationException(
            "Fixture Owner permissions drifted; never silently restore or broaden.");

    if (existingGrants.Length == 0)
    {
        db.RolePermissions.AddRange(tenantPermissions.Select(p =>
            new RolePermission(tenant.Id, ownerRole.Id, p.Id)));
        await db.SaveChangesAsync();
    }

    var staff = await db.Users.SingleOrDefaultAsync(x =>
        x.TenantId == tenant.Id && x.NormalizedUserName == "E2E-IAM-STAFF");
    if (staff is null)
    {
        staff = new User(tenant.Id, "e2e-iam-staff",
            "e2e-iam-staff@example.test", "E2E", "Staff", UserType.Tenant)
        {
            IsActive = true,
            EmailConfirmed = true,
            NormalizedUserName = "E2E-IAM-STAFF",
            NormalizedEmail = "E2E-IAM-STAFF@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        staff.PasswordHash = new PasswordHasher<User>()
            .HashPassword(staff, passwordForFixture);
        db.Users.Add(staff);
        await db.SaveChangesAsync();
    }
    if (!staff.IsActive || staff.UserType != UserType.Tenant ||
        await db.UserRoles.AnyAsync(x => x.TenantId == tenant.Id &&
            x.UserId == staff.Id))
        throw new InvalidOperationException("Fixture staff must start without roles.");
    await tx.CommitAsync();
    Console.WriteLine("PASS: disposable Tenant IAM fixture provisioned (Owner with exactly 17 scoped grants; role-free staff).");
    return;
}

if (isTenantAudit)
{
    await using var tenantLookup = new SqlCommand("""
        SELECT [Id] FROM [dbo].[Tenants]
        WHERE [Slug] = N'e2e-http-ci' AND [IsActive] = 1
        """, connection);
    if (await tenantLookup.ExecuteScalarAsync() is not Guid tenantId)
        throw new InvalidOperationException("Disposable IAM Tenant not found.");

    foreach (var (table, action, minimum) in new[]
    {
        ("TenantRoleChangeAuditEntries", "create", 1),
        ("TenantRoleChangeAuditEntries", "state", 2),
        ("TenantRoleGrantAuditEntries", "grant", 1),
        ("TenantRoleGrantAuditEntries", "revoke", 1),
        ("TenantMemberRoleAuditEntries", "grant", 1),
        ("TenantMemberRoleAuditEntries", "revoke", 1)
    })
    {
        var sql = $"SELECT COUNT_BIG(*) FROM [dbo].[{table}] " +
                  "WHERE [TenantId] = @tenant AND [Action] = @action";
        await using var check = new SqlCommand(sql, connection);
        check.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        check.Parameters.Add("@action", SqlDbType.NVarChar, 16).Value = action;
        if (Convert.ToInt64(await check.ExecuteScalarAsync()) < minimum)
            throw new InvalidOperationException(
                $"Missing persisted Tenant IAM audit: {table}/{action}");
    }

    try
    {
        await using var update = new SqlCommand("""
            UPDATE TOP (1) [dbo].[TenantMemberRoleAuditEntries]
            SET [Action] = N'grant'
            WHERE [TenantId] = @tenant
            """, connection);
        update.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        await update.ExecuteNonQueryAsync();
        throw new InvalidOperationException("SQL accepted tampering with member role audit.");
    }
    catch (SqlException e) when (e.Number == 51051) { }

    Console.WriteLine("PASS: persisted Tenant IAM audit grants/revokes/state and immutable SQL trigger.");
    return;
}

if (isAuditCheck)
{
    if (!Guid.TryParse(args[2], out var viewerId) || viewerId == Guid.Empty ||
        viewerId == subject)
        throw new InvalidOperationException("Invalid signed System viewer subject.");

    // These checks query committed SQL Server rows, not in-memory loggers,
    // mocked principals or the same DbContext as the live HTTP request.
    var checks = new (string Name, string Sql)[]
    {
        ("anonymous challenge", """
            SELECT COUNT_BIG(*) FROM [dbo].[PlatformAccessDenialAuditEntries]
            WHERE [ActorUserId] IS NULL AND [Outcome] = 'challenge'
              AND [HttpMethod] = 'GET' AND [RouteTemplate] LIKE '%Permissions%'
            """),
        ("System viewer forbidden", """
            SELECT COUNT_BIG(*) FROM [dbo].[PlatformAccessDenialAuditEntries]
            WHERE [ActorUserId] = @viewer AND [Outcome] = 'forbidden'
              AND [HttpMethod] = 'POST' AND [RouteTemplate] LIKE '%Permissions%'
            """),
        ("reserved catalog forbidden after authentication", """
            SELECT COUNT_BIG(*) FROM [dbo].[PlatformAccessDenialAuditEntries]
            WHERE [ActorUserId] = @subject AND [Outcome] = 'forbidden'
              AND [HttpMethod] = 'POST' AND [RouteTemplate] LIKE '%Permissions%'
            """),
        ("stale role forbidden after persisted revocation", """
            SELECT COUNT_BIG(*) FROM [dbo].[PlatformAccessDenialAuditEntries]
            WHERE [ActorUserId] = @subject AND [Outcome] = 'forbidden'
              AND [HttpMethod] = 'GET' AND [RouteTemplate] LIKE '%Roles%'
            """),
    };

    foreach (var (name, sql) in checks)
    {
        await using var check = new SqlCommand(sql, connection);
        check.Parameters.Add("@subject", SqlDbType.UniqueIdentifier).Value = subject;
        check.Parameters.Add("@viewer", SqlDbType.UniqueIdentifier).Value = viewerId;
        if (Convert.ToInt64(await check.ExecuteScalarAsync()) < 1)
            throw new InvalidOperationException($"Missing persisted SQL security decision: {name}");
    }

    // The dedicated SQL trigger must prevent retroactive rewriting of decisions.
    try
    {
        await using var tamper = new SqlCommand("""
            UPDATE TOP (1) [dbo].[PlatformAccessDenialAuditEntries]
            SET [Outcome] = 'forbidden';
            """, connection);
        await tamper.ExecuteNonQueryAsync();
        throw new InvalidOperationException("SQL Server accepted an audit history UPDATE.");
    }
    catch (SqlException exception) when (exception.Number == 51002)
    {
        // Expected: append-only SQL Server trigger refused the update.
    }

    Console.WriteLine("PASS: committed denied-access audit rows and SQL append-only trigger.");
    return;
}

await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
    IsolationLevel.Serializable);

// Only the existing seeded, null-tenant System "admin" may be targeted.
// Do not touch Tenant users or other identities even in CI.
await using (var verify = new SqlCommand("""
    SELECT COUNT_BIG(*)
    FROM [dbo].[Users] WITH (UPDLOCK, HOLDLOCK)
    WHERE [Id] = @subject AND [TenantId] IS NULL AND [UserTpe] = 1
      AND [NormalizedUserName] = N'ADMIN' AND [IsActive] = 1;
    """, connection, transaction))
{
    verify.Parameters.Add("@subject", SqlDbType.UniqueIdentifier).Value = subject;
    var matching = Convert.ToInt64(await verify.ExecuteScalarAsync());
    if (matching != 1)
        throw new InvalidOperationException("Refusing to mutate a non-seeded or non-System identity.");
}

// Intentionally model persisted SQL-level revocation independently of the
// public grant APIs, which must not permit SuperAdmin self-revocation.
// Direct SQL is allowed ONLY in this disposable E2E DB. It does NOT constitute
// a tested or approved production grant-mutation/audit path.
await using (var revoke = new SqlCommand("""
    DELETE ur
    FROM [dbo].[UserRoles] AS ur
    INNER JOIN [dbo].[Roles] AS role ON role.[Id] = ur.[RoleId]
    WHERE ur.[UserId] = @subject AND ur.[TenantId] = @master
      AND role.[TenantId] = @master
      AND role.[NormalizedName] = N'SUPERADMIN';
    """, connection, transaction))
{
    revoke.Parameters.Add("@subject", SqlDbType.UniqueIdentifier).Value = subject;
    revoke.Parameters.Add("@master", SqlDbType.UniqueIdentifier).Value =
        DefaultSeedData.MasterTenant.Id;
    var affected = await revoke.ExecuteNonQueryAsync();
    if (affected != 1)
        throw new InvalidOperationException(
            $"Expected exactly one seeded SuperAdmin assignment, found {affected}; transaction rolled back.");
}

await transaction.CommitAsync();
Console.WriteLine("PASS: disposable SQL Server removed exactly one seeded SuperAdmin assignment.");
