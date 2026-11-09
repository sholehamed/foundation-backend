using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// Scoped, independently gated Tenant-owned menu configuration. These
/// endpoints never call the global Menu CRUD endpoints or trust a TenantId
/// in the request. TenantNavigationOwnerWriter re-authorizes every mutation.
/// </summary>
public static class TenantNavigationManagement
{
    public static void Map(WebApplication app, string backendPrefix)
    {
        var root = "/" + backendPrefix.Trim('/') + "/tenant/workspace/navigation/catalog";
        app.MapGet(root, Read).RequireAuthorization()
            .WithTags("Tenant Navigation");
        app.MapPost(root + "/categories", CreateCategory).RequireAuthorization()
            .WithTags("Tenant Navigation");
        app.MapPut(root + "/categories/{id:guid}", UpdateCategory).RequireAuthorization()
            .WithTags("Tenant Navigation");
        app.MapPost(root + "/items", CreateItem).RequireAuthorization()
            .WithTags("Tenant Navigation");
        app.MapPut(root + "/items/{id:guid}", UpdateItem).RequireAuthorization()
            .WithTags("Tenant Navigation");
    }

    private static bool Enabled(HttpContext http, IConfiguration config) =>
        http.Request.IsHttps && config.GetValue<bool>(
            "Identity:TenantNavigation:OwnerEditorEnabled");

    private static IResult Denied() =>
        Results.StatusCode(StatusCodes.Status403Forbidden);

    private static IResult Error(Exception error) => error switch
    {
        TenantNavigationInputException => Results.Problem(
            title: "Invalid Tenant navigation input", statusCode: 400),
        TenantNavigationVersionConflictException => Results.Problem(
            title: "Tenant navigation version conflict", statusCode: 409),
        UnauthorizedAccessException => Denied(),
        SqlException sql when sql.Number is 2601 or 2627 or 547 or 1205 or 3960 =>
            Results.Problem(title: "Tenant navigation write conflict", statusCode: 409),
        _ => throw error
    };

    public static async Task<IResult> Read(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration config, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!Enabled(http, config)) return Denied();
        try
        {
            return Results.Ok(await TenantNavigationOwnerWriter.ReadAsync(
                current, db, http.User, enabled: true, ct));
        }
        catch (UnauthorizedAccessException) { return Denied(); }
    }

    public static async Task<IResult> CreateCategory(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration config,
        TenantNavigationOwnerWriter.CreateCategoryRequest request,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!Enabled(http, config)) return Denied();
        try
        {
            return Results.Ok(await TenantNavigationOwnerWriter.CreateCategoryAsync(
                current, db, http.User, request, enabled: true, ct));
        }
        catch (Exception e) when (e is TenantNavigationInputException
            or TenantNavigationVersionConflictException or UnauthorizedAccessException
            or SqlException)
        { return Error(e); }
    }

    public static async Task<IResult> UpdateCategory(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration config, Guid id,
        TenantNavigationOwnerWriter.UpdateCategoryRequest request,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!Enabled(http, config)) return Denied();
        try
        {
            return Results.Ok(await TenantNavigationOwnerWriter.UpdateCategoryAsync(
                current, db, http.User, id, request, enabled: true, ct));
        }
        catch (Exception e) when (e is TenantNavigationInputException
            or TenantNavigationVersionConflictException or UnauthorizedAccessException
            or SqlException)
        { return Error(e); }
    }

    public static async Task<IResult> CreateItem(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration config,
        TenantNavigationOwnerWriter.CreateItemRequest request,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!Enabled(http, config)) return Denied();
        try
        {
            return Results.Ok(await TenantNavigationOwnerWriter.CreateItemAsync(
                current, db, http.User, request, enabled: true, ct));
        }
        catch (Exception e) when (e is TenantNavigationInputException
            or TenantNavigationVersionConflictException or UnauthorizedAccessException
            or SqlException)
        { return Error(e); }
    }

    public static async Task<IResult> UpdateItem(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration config, Guid id,
        TenantNavigationOwnerWriter.UpdateItemRequest request,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!Enabled(http, config)) return Denied();
        try
        {
            return Results.Ok(await TenantNavigationOwnerWriter.UpdateItemAsync(
                current, db, http.User, id, request, enabled: true, ct));
        }
        catch (Exception e) when (e is TenantNavigationInputException
            or TenantNavigationVersionConflictException or UnauthorizedAccessException
            or SqlException)
        { return Error(e); }
    }
}
