using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;
using IdentityHttpMethod = Modules.System.Identity.Domain.Entities.HttpMethod;

namespace Shinera.Identity.Tests;

public sealed class SystemEndpointRegistryIntegrityTests
{
    [Fact]
    public async Task System_feature_runtime_and_stored_links_must_match_exactly()
    {
        await using var db = CreateDb();
        var feature = Feature();
        var endpoints = Source(feature);
        var before = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.False(before.ReadyForGranularEnforcement);
        Assert.Contains("api_not_registered", Assert.Single(before.Endpoints).Issues);
        Assert.Empty(await db.ApiResources.ToListAsync());

        var sync = new FeatureRegistrySyncService(db, endpoints);
        Assert.True((await sync.PreviewAsync()).CanApply);
        await sync.ApplyAsync();

        var clean = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.True(clean.ReadyForGranularEnforcement);
        Assert.Equal(1, clean.DiscoveredFeatureEndpoints);
        Assert.Equal(1, clean.ReadyFeatureEndpoints);
        Assert.Equal(0, clean.OutOfScopeRouteCount);
        Assert.Empty(clean.UncatalogedSystemAdminRoutes);
        Assert.Empty(clean.UnreviewedActiveSystemApiKeys);
        Assert.Empty(await db.RolePermissions.ToListAsync());
        Assert.Empty(await db.UserPermissions.ToListAsync());

        var api = await db.ApiResources.SingleAsync();
        api.IsActive = false;
        await db.SaveChangesAsync();
        var inactive = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.Contains("api_inactive_deprecated_or_anonymous",
            Assert.Single(inactive.Endpoints).Issues);
        api.IsActive = true;
        await db.SaveChangesAsync();

        var wrongPermission = await db.Permissions.SingleAsync(x => x.Code == "USERS.READ");
        var unexpected = new PermissionApiResource(wrongPermission.Id, api.Id)
            { RowVersion = [1] };
        db.PermissionApiResources.Add(unexpected);
        await db.SaveChangesAsync();
        var broadened = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.False(broadened.ReadyForGranularEnforcement);
        Assert.Contains("api_permission_link_missing_or_extra",
            Assert.Single(broadened.Endpoints).Issues);
        db.PermissionApiResources.Remove(unexpected);
        await db.SaveChangesAsync();

        var owner = await db.Permissions.SingleAsync(x => x.Code == "ROLES.CREATE");
        var expectedResource = owner.ResourceId;
        owner.ResourceId = (await db.Resources.SingleAsync(x => x.Code == "users")).Id;
        await db.SaveChangesAsync();
        var moved = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.Contains("permission_missing_inactive_or_wrong_owner",
            Assert.Single(moved.Endpoints).Issues);
        owner.ResourceId = expectedResource;
        await db.SaveChangesAsync();

        api.RouteTemplate = "/api/System/Roles/changed";
        await db.SaveChangesAsync();
        var staleRoute = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.Contains("api_route_or_verb_mismatch",
            Assert.Single(staleRoute.Endpoints).Issues);
        api.RouteTemplate = "/api/System/Roles";
        await db.SaveChangesAsync();

        // A retired active SYSTEM.* row without a live code descriptor is not
        // silently accepted or deleted by the read-only auditor.
        db.ApiResources.Add(new ApiResource(Guid.NewGuid(), api.ResourceId,
            "SYSTEM.ROLES.OLD", "Obsolete but active endpoint", null,
            IdentityHttpMethod.POST, "/api/System/Roles/old")
        { Source = ApiSource.Auto, IsActive = true, IsDeprecated = false });
        await db.SaveChangesAsync();
        var extra = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints);
        Assert.Contains("SYSTEM.ROLES.OLD", extra.UnreviewedActiveSystemApiKeys);
        Assert.False(extra.ReadyForGranularEnforcement);
    }

    [Fact]
    public async Task Runtime_policy_drift_and_unmapped_SystemAdministrator_routes_fail_closed()
    {
        await using var db = CreateDb();
        var valid = Feature();
        await new FeatureRegistrySyncService(db, Source(valid)).ApplyAsync();

        var missingAdmin = Feature(addSystemAdministrator: false);
        var admin = await SystemEndpointRegistryIntegrity.ReadAsync(
            db, Source(missingAdmin));
        Assert.Contains("system_administrator_policy_missing",
            Assert.Single(admin.Endpoints).Issues);

        var missingGranular = Feature(addGranular: false);
        var gran = await SystemEndpointRegistryIntegrity.ReadAsync(
            db, Source(missingGranular));
        Assert.Contains("granular_requirement_missing_or_mismatched",
            Assert.Single(gran.Endpoints).Issues);

        var anonymous = Feature(anonymous: true);
        var anon = await SystemEndpointRegistryIntegrity.ReadAsync(
            db, Source(anonymous));
        Assert.Contains("anonymous_system_feature",
            Assert.Single(anon.Endpoints).Issues);

        var duplicate = await SystemEndpointRegistryIntegrity.ReadAsync(
            db, Source(valid, Feature(route: "/api/System/Roles/duplicate")));
        Assert.Contains(duplicate.Endpoints, x =>
            x.Issues.Contains("duplicate_endpoint_key"));
        Assert.False(duplicate.ReadyForGranularEnforcement);

        var untagged = Simple("/api/System/Roles/undeclared", true);
        var uncovered = await SystemEndpointRegistryIntegrity.ReadAsync(
            db, Source(valid, untagged));
        Assert.Single(uncovered.UncatalogedSystemAdminRoutes);
        Assert.False(uncovered.ReadyForGranularEnforcement);

        // Only these three reviewed route shapes are exempt, no broad
        // System namespace exception is introduced.
        var internalOnly = await SystemEndpointRegistryIntegrity.ReadAsync(
            db, Source(valid, Simple("/api/System/UiAccess/me", true),
                Simple("/api/System/UiAccess/me/{key}", true),
                Simple("/api/System/Tenants/resolve", true, anonymous: true),
                Simple("/api/System/Auth/interactive-login", false)));
        Assert.True(internalOnly.ReadyForGranularEnforcement);
        Assert.Equal(3, internalOnly.ReviewedInternalSystemRoutes);
        Assert.Equal(1, internalOnly.OutOfScopeRouteCount);

        var context = new DefaultHttpContext();
        var result = await FeatureRegistry.SystemEndpointIntegrity(
            new TenantContext(), db, Source(valid), context, default);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Empty_endpoint_source_cannot_claim_readiness()
    {
        await using var db = CreateDb();
        var report = await SystemEndpointRegistryIntegrity.ReadAsync(db, Source());
        Assert.False(report.ReadyForGranularEnforcement);
        Assert.Empty(report.Endpoints);
    }

    private static RouteEndpoint Feature(string route = "/api/System/Roles",
        bool addSystemAdministrator = true, bool addGranular = true, bool anonymous = false)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(route), 0);
        builder.Metadata.Add(new HttpMethodMetadata(["POST"]));
        builder.Metadata.Add(new FeatureEndpointMetadata(
            "SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE", "Create role"));
        if (addSystemAdministrator)
            builder.Metadata.Add(new AuthorizeAttribute("SystemAdministrator"));
        if (addGranular)
            builder.Metadata.Add(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new SystemFeatureEndpointPermissionRequirement(
                    "roles", "ROLES.CREATE")).Build());
        if (anonymous) builder.Metadata.Add(new AllowAnonymousAttribute());
        return (RouteEndpoint)builder.Build();
    }

    private static RouteEndpoint Simple(
        string route, bool systemAdministrator, bool anonymous = false)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(route), 0);
        builder.Metadata.Add(new HttpMethodMetadata(["GET"]));
        if (systemAdministrator)
            builder.Metadata.Add(new AuthorizeAttribute("SystemAdministrator"));
        if (anonymous) builder.Metadata.Add(new AllowAnonymousAttribute());
        return (RouteEndpoint)builder.Build();
    }

    private static EndpointDataSource Source(params Endpoint[] endpoints) =>
        new DefaultEndpointDataSource(endpoints);

    private static IdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new IdentityDbContext(options, new TenantContext());
    }
}
