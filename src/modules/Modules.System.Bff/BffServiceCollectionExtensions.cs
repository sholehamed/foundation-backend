using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Modules.System.Bff;

/// <summary>
/// Opt-in confidential OIDC client and opaque server-side browser session.
/// Does not alter the host's default authentication or challenge schemes.
/// </summary>
public static class BffServiceCollectionExtensions
{
    public static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("Identity:Bff:Enabled"))
            return services;

        var settings = BffOidcSettings.FromConfiguration(configuration, environment);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IDistributedCache)))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "BFF is enabled but no IDistributedCache provider is registered. Configure a shared distributed cache for production.");
            }

            services.AddDistributedMemoryCache();
        }

        services.AddSingleton(settings);
        services.AddBffSessionInfrastructure();

        services.AddAuthentication()
            .AddOpenIdConnect(BffDefaults.OidcScheme, options =>
            {
                options.Authority = settings.Authority.GetLeftPart(UriPartial.Authority);
                options.RequireHttpsMetadata = settings.Authority.Scheme == Uri.UriSchemeHttps;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.SignInScheme = BffDefaults.CookieScheme;
                options.CallbackPath = BffDefaults.CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;
                options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
                options.BackchannelTimeout = TimeSpan.FromSeconds(30);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    NameClaimType = "given_name",
                    RoleClaimType = "role"
                };

                options.Scope.Clear();
                options.Scope.Add(OpenIdConnectScope.OpenId);
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.Scope.Add("api");
                options.Scope.Add("offline_access");
                options.Scope.Add("roles");
                options.Scope.Add("permissions");

                options.ClaimActions.MapUniqueJsonKey("account_type", "account_type");
                options.ClaimActions.MapUniqueJsonKey("tenant", "tenant");
                options.ClaimActions.MapUniqueJsonKey("tenant_id", "tenant_id");
                options.ClaimActions.MapUniqueJsonKey("username", "username");
                options.ClaimActions.MapUniqueJsonKey("given_name", "given_name");
                options.ClaimActions.MapUniqueJsonKey("picture", "picture");

                options.CorrelationCookie.SameSite = SameSiteMode.None;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.NonceCookie.SameSite = SameSiteMode.None;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;

                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        context.HttpContext.Response.Headers.CacheControl = "no-store";

                        if (context.Properties is null ||
                            !context.Properties.Items.TryGetValue(
                                BffDefaults.LoginContextProperty, out var loginContext) ||
                            loginContext is not ("system" or "tenant") ||
                            !settings.IsRegisteredRedirectUri(context.ProtocolMessage.RedirectUri))
                        {
                            context.HandleResponse();
                            context.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            return Task.CompletedTask;
                        }

                        context.ProtocolMessage.SetParameter("login_context", loginContext);
                        if (context.Properties.Items.TryGetValue(
                                BffDefaults.TenantProperty, out var tenant) &&
                            !string.IsNullOrWhiteSpace(tenant))
                        {
                            context.ProtocolMessage.SetParameter("tenant", tenant);
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrWhiteSpace(
                            context.Principal?.FindFirst("sub")?.Value))
                        {
                            context.Fail("The Identity Provider did not return a subject.");
                        }

                        return Task.CompletedTask;
                    },
                    OnUserInformationReceived = context =>
                    {
                        if (context.Principal?.Identity is not ClaimsIdentity identity)
                            return Task.CompletedTask;

                        AddArrayClaims(context.User.RootElement, "roles", "role", identity);
                        AddArrayClaims(context.User.RootElement, "permissions", "permission", identity);
                        return Task.CompletedTask;
                    },
                    OnTicketReceived = context =>
                    {
                        if (!BffSessionContextValidator.TryValidate(
                                context.Principal,
                                context.Properties,
                                context.HttpContext.Request,
                                settings,
                                context.ReturnUri,
                                out _))
                        {
                            context.Fail("The authenticated account or tenant does not match the BFF host.");
                        }

                        context.HttpContext.Response.Headers.CacheControl = "no-store";
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = async context =>
                    {
                        context.HandleResponse();
                        context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.HttpContext.Response.Headers.CacheControl = "no-store";
                        await context.HttpContext.Response.WriteAsJsonAsync(
                            new { error = "bff_oidc_callback_failed" });
                    }
                };
            });

        return services;
    }

    public static IServiceCollection AddBffSessionInfrastructure(
        this IServiceCollection services)
    {
        services.AddDataProtection();

        // The consuming host MUST register IDistributedCache explicitly:
        // memory for single-instance local dev; shared Redis/SQL for multi-node.
        services.AddSingleton<ITicketStore, DistributedBffTicketStore>();

        // Existing OpenIddict default bearer scheme is not changed.
        services.AddAuthentication().AddCookie(BffDefaults.CookieScheme, options =>
        {
            options.Cookie.Name = BffDefaults.CookieName;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.IsEssential = true;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);

            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            };
        });

        // Only store an opaque reference in the auth cookie, never the
        // user claims or bearer credentials.
        services.AddOptions<CookieAuthenticationOptions>(BffDefaults.CookieScheme)
            .Configure<ITicketStore>((options, tickets) =>
                options.SessionStore = tickets);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-Foundation.Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        return services;
    }

    private static void AddArrayClaims(
        global::System.Text.Json.JsonElement user,
        string property,
        string claimType,
        ClaimsIdentity identity)
    {
        if (!user.TryGetProperty(property, out var values) ||
            values.ValueKind != global::System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != global::System.Text.Json.JsonValueKind.String)
                continue;

            var claim = value.GetString();
            if (!string.IsNullOrWhiteSpace(claim) &&
                !identity.HasClaim(claimType, claim))
            {
                identity.AddClaim(new Claim(claimType, claim));
            }
        }
    }
}
