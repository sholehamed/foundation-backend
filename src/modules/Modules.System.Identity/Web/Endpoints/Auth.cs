using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using Web.SharedKernel.Attributes;


namespace Modules.System.Identity.Web.Endpoints;

public class Auth : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this)

            .MapPost(Login, "login", configure: x => x.AllowAnonymous())
            .MapGet(NewCaptcha, "/captcha/new", configure: x => x.AllowAnonymous())
            .MapGet(GetCaptchaImage, "/captcha/image", configure: x => x.AllowAnonymous())
            .MapGet(GetUserInfo, "/userinfo", configure: x => x.RequireAuthorization())
            .MapMethods(Logout, ["GET", "POST"], "logout");
        ;
    }
    public async Task<IResult> GetUserInfo(
   HttpContext context)
    {


        var result = await context.AuthenticateAsync(
         OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }

        var user = result.Principal;

        return Results.Ok(new
        {
            sub = user.GetClaim(OpenIddictConstants.Claims.Subject),
            username = user.GetClaim(OpenIddictConstants.Claims.Username),
            given_name = user.GetClaim(OpenIddictConstants.Claims.GivenName),
            picture = user.GetClaim(OpenIddictConstants.Claims.Picture),
            tenant = user.GetClaim("tenant"),
            tenant_id = user.GetClaim("tenant_id"),
            account_type = user.GetClaim("account_type")
        });
    }
    [Resouce("", "newcaptcha")]
    public IResult NewCaptcha(
    CaptchaService captchaService,
    LinkGenerator linkGenerator,
    HttpContext httpContext)
    {
        var challenge = captchaService.CreateChallenge();

        var imageUrl = $"/System/Auth/captcha/image?token={Uri.EscapeDataString(challenge.Token)}";

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return Results.Problem("Captcha image route was not found.");
        }

        return Results.Ok(new
        {
            token = challenge.Token,
            imageUrl
        });
    }
    [Resouce("", "newcaptcha")]
    public IResult GetCaptchaImage(
    string token,
    CaptchaService captchaService)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest();
        }

        try
        {
            var bytes = captchaService.GenerateImage(token);
            return Results.File(bytes, "image/png");
        }
        catch
        {
            return Results.BadRequest();
        }
    }

    [Resouce("", "logout")]
    public async Task<IResult> Logout(HttpContext httpContext)
    {
        var request = httpContext.GetOpenIddictServerRequest();
        if (request is null)
        {
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.InvalidRequest,
                error_description = "OpenIddict request is missing."
            });
        }

        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return Results.SignOut(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties
            {
                RedirectUri = request.PostLogoutRedirectUri ?? "/"
            });
    }
    [Resouce("", "login")]
    public async Task<IResult> Login(HttpContext httpContext, CaptchaService captchaService,
IIdentityDbContext db,
RefreshGrantValidator refreshValidator,
IPasswordHasher<User> passwordHasher,
CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest();


        if (request is null)
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.InvalidRequest,
                error_description = "OpenIddict request is missing."
            });

        var token = httpContext.Request.Form["captchaid"];
        var input = httpContext.Request.Form["userEnteredCaptchaCode"];
        if (request.IsRefreshTokenGrantType())
        {
            var result = await httpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // The refresh token is not proof that the account, tenant and role
            // assignments are still valid. Reject stale principals: the client
            // must authenticate again with its current permissions.
            if (!result.Succeeded || result.Principal is null ||
                !await refreshValidator.IsStillAuthorizedAsync(result.Principal, cancellationToken))
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The session is no longer valid."
                    }));
            }

            return Results.SignIn(result.Principal,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        if (!request.IsPasswordGrantType())
        {
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.UnsupportedGrantType,
                error_description = "The specified grant type is not supported."
            });
        }

        var login_context = request.GetParameter("login_context")?.ToString();
        if (login_context is not ("system" or "tenant"))
            return Results.BadRequest(new
            {
                error = "invalid_request",
                error_description = "Invalid login context."
            });
        if (!captchaService.Validate(token.ToString(), input.ToString()))
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid or expired CAPTCHA."
                }));
        }
        if (login_context == "tenant")
        {

            var tenantSlug = request.GetParameter("tenant")?.ToString();
            if (string.IsNullOrWhiteSpace(tenantSlug))
            {
                return Results.BadRequest(new
                {
                    error = OpenIddictConstants.Errors.InvalidRequest,
                    error_description = "The 'tenant' parameter is required."
                });
            }

            var tenant = await db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Slug == tenantSlug && x.IsActive, cancellationToken);

            if (tenant is null)
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid tenant or credentials."
                    }));
            }

            var normalizedUserName = request.Username?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedUserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid credentials."
                    }));
            }

            var user = await db.Users.IgnoreQueryFilters(["tenant"])
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenant.Id &&
                    x.NormalizedUserName == normalizedUserName &&
                    x.IsActive, cancellationToken);

            if (user is null)
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid tenant or credentials."
                    }));
            }

            if (user.IsLockedOut && user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User account is locked."
                    }));
            }
            var passwordVerification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (passwordVerification == PasswordVerificationResult.Failed)
            {
                user.AccessFailedCount++;

                if (user.AccessFailedCount >= 5)
                {
                    user.IsLockedOut = true;
                    user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                }

                user.LastModifiedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid tenant or credentials."
                    }));
            }

            if (user.AccessFailedCount > 0 || user.IsLockedOut)
            {
                user.AccessFailedCount = 0;
                user.IsLockedOut = false;
                user.LockoutEndUtc = null;
                user.LastModifiedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            // ۱. استخراج تمامی دسترسی‌های معتبر کاربر (مستقیم + نقش‌های مستقیم + نقش‌های گروه‌ها)
            var permissions = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
                .Where(x => x.TenantId == tenant.Id && x.UserId == user.Id && x.IsGranted)
                .Select(x => x.Permission.Name)
                .Union(
                    // دسترسی‌های ناشی از نقش‌های مستقیم کاربر
                    db.UserRoles.IgnoreQueryFilters(["tenant"])
                        .Where(x => x.TenantId == tenant.Id && x.UserId == user.Id)
                        .SelectMany(x => x.Role.RolePermissions.Select(rp => rp.Permission.Name))
                )
                .Union(
                    // دسترسی‌های ناشی از نقش‌های انتساب‌داده‌شده به گروه‌های کاربر
                    db.UserGroups.IgnoreQueryFilters(["tenant"])
                        .Where(x => x.TenantId == tenant.Id && x.UserId == user.Id)
                        .SelectMany(x => x.Group.GroupRoles
                            .SelectMany(gr => gr.Role.RolePermissions.Select(rp => rp.Permission.Name)))
                )
                .Distinct()
                .ToListAsync(cancellationToken);

            // ۲. استخراج تمامی نقش‌های کاربر (نقش‌های مستقیم + نقش‌های به ارث رسیده از گروه‌ها)
            var roles = await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .Where(x => x.TenantId == tenant.Id && x.UserId == user.Id)
                .Select(x => x.Role.Name)
                .Union(
                    // نقش‌هایی که کاربر از طریق عضویت در گروه‌ها به دست آورده است
                    db.UserGroups.IgnoreQueryFilters(["tenant"])
                        .Where(x => x.TenantId == tenant.Id && x.UserId == user.Id)
                        .SelectMany(x => x.Group.GroupRoles.Select(gr => gr.Role.Name))
                )
                .Distinct()
                .ToListAsync(cancellationToken);


            var claims = new List<Claim>
    {
        new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
        new(OpenIddictConstants.Claims.Username, user.UserName),
        new(OpenIddictConstants.Claims.Email, user.Email),
        new(OpenIddictConstants.Claims.Picture, user.ImageId.ToString()!),
        new(OpenIddictConstants.Claims.GivenName, $"{user.FirstName} {user.LastName}"),
        new("tenant_id", tenant.Id.ToString()),
        new("tenant", tenant.Slug),
        new Claim("account_type", "tenant"),
        new Claim("security_stamp", user.SecurityStamp ?? "")
    };

            claims.AddRange(roles.Select(role => new Claim(OpenIddictConstants.Claims.Role, role)));
            claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

            var identity = new ClaimsIdentity(
                claims,
                TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Name,
                OpenIddictConstants.Claims.Role);

            var principal = new ClaimsPrincipal(identity);

            var allowedScopes = request.GetScopes().Intersect(new[]
            {
        OpenIddictConstants.Scopes.OpenId,
    OpenIddictConstants.Scopes.Profile,
    OpenIddictConstants.Scopes.Email,
    OpenIddictConstants.Scopes.OfflineAccess,
    "api"
    });

            principal.SetScopes(allowedScopes);
            principal.SetResources("resource_server");

            foreach (var claim in principal.Claims)
            {
                switch (claim.Type)
                {
                    case OpenIddictConstants.Claims.Subject:
                    case OpenIddictConstants.Claims.Username:
                    case OpenIddictConstants.Claims.Email:
                    case OpenIddictConstants.Claims.Picture:
                    case OpenIddictConstants.Claims.GivenName:
                    case OpenIddictConstants.Claims.Role:
                    case "tenant_id":
                    case "tenant":
                    case "permission":
                        claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
                        break;
                    case "account_type":
                        claim.SetDestinations(
                            OpenIddictConstants.Destinations.AccessToken);
                        break;
                }
            }

            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        else
        {





            var normalizedUserName = request.Username?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedUserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid credentials."
                    }));
            }

            var user = await db.Users.IgnoreQueryFilters(["tenant"])
                .FirstOrDefaultAsync(x =>
        x.NormalizedUserName == normalizedUserName &&
        x.UserType == UserType.System &&
        x.TenantId == null &&
        x.IsActive,
        cancellationToken);

            if (user is null)
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid tenant or credentials."
                    }));
            }

            if (user.IsLockedOut && user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User account is locked."
                    }));
            }
            var passwordVerification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (passwordVerification == PasswordVerificationResult.Failed)
            {
                user.AccessFailedCount++;

                if (user.AccessFailedCount >= 5)
                {
                    user.IsLockedOut = true;
                    user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                }

                user.LastModifiedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid tenant or credentials."
                    }));
            }

            if (user.AccessFailedCount > 0 || user.IsLockedOut)
            {
                user.AccessFailedCount = 0;
                user.IsLockedOut = false;
                user.LockoutEndUtc = null;
                user.LastModifiedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            // ۱. استخراج تمامی دسترسی‌های معتبر کاربر (مستقیم + نقش‌های مستقیم + نقش‌های گروه‌ها)
            var permissions = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
                .Where(x => x.UserId == user.Id && x.IsGranted)
                .Select(x => x.Permission.Name)
                .Union(
                    // دسترسی‌های ناشی از نقش‌های مستقیم کاربر
                    db.UserRoles.IgnoreQueryFilters(["tenant"])
                        .Where(x => x.UserId == user.Id)
                        .SelectMany(x => x.Role.RolePermissions.Select(rp => rp.Permission.Name))
                )
                .Union(
                    // دسترسی‌های ناشی از نقش‌های انتساب‌داده‌شده به گروه‌های کاربر
                    db.UserGroups.IgnoreQueryFilters(["tenant"])
                        .Where(x => x.UserId == user.Id)
                        .SelectMany(x => x.Group.GroupRoles
                            .SelectMany(gr => gr.Role.RolePermissions.Select(rp => rp.Permission.Name)))
                )
                .Distinct()
                .ToListAsync(cancellationToken);

            // ۲. استخراج تمامی نقش‌های کاربر (نقش‌های مستقیم + نقش‌های به ارث رسیده از گروه‌ها)
            var roles = await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .Where(x => x.UserId == user.Id)
                .Select(x => x.Role.Name)
                .Union(
                    // نقش‌هایی که کاربر از طریق عضویت در گروه‌ها به دست آورده است
                    db.UserGroups.IgnoreQueryFilters(["tenant"])
                        .Where(x => x.UserId == user.Id)
                        .SelectMany(x => x.Group.GroupRoles.Select(gr => gr.Role.Name))
                )
                .Distinct()
                .ToListAsync(cancellationToken);


            var claims = new List<Claim>
    {
        new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
        new(OpenIddictConstants.Claims.Username, user.UserName),
        new(OpenIddictConstants.Claims.Email, user.Email),
        new(OpenIddictConstants.Claims.Picture, user.ImageId.ToString()!),
        new(OpenIddictConstants.Claims.GivenName, $"{user.FirstName} {user.LastName}"),
        new Claim("account_type", "system"),
        new Claim("security_stamp", user.SecurityStamp ?? "")
    };

            claims.AddRange(roles.Select(role => new Claim(OpenIddictConstants.Claims.Role, role)));
            claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

            var identity = new ClaimsIdentity(
                claims,
                TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Name,
                OpenIddictConstants.Claims.Role);

            var principal = new ClaimsPrincipal(identity);

            var allowedScopes = request.GetScopes().Intersect(new[]
            {
        OpenIddictConstants.Scopes.OpenId,
    OpenIddictConstants.Scopes.Profile,
    OpenIddictConstants.Scopes.Email,
    OpenIddictConstants.Scopes.OfflineAccess,
    "api"
    });

            principal.SetScopes(allowedScopes);
            principal.SetResources("resource_server");

            foreach (var claim in principal.Claims)
            {
                switch (claim.Type)
                {
                    case OpenIddictConstants.Claims.Subject:
                    case OpenIddictConstants.Claims.Username:
                    case OpenIddictConstants.Claims.Email:
                    case OpenIddictConstants.Claims.Picture:
                    case OpenIddictConstants.Claims.GivenName:
                    case OpenIddictConstants.Claims.Role:
                    case "tenant_id":
                    case "tenant":
                    case "permission":
                        claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
                        break;
                    case "account_type":
                        claim.SetDestinations(
                            OpenIddictConstants.Destinations.AccessToken);
                        break;
                }
            }

            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

    }



}