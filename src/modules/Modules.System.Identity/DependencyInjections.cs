using Application.SharedKernel;
using Infrastructure.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.ApiResources.Services;
using Modules.System.Identity.Application.Mapping;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Modules.System.Identity.Web.Middlewares;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Modules.System.Identity.Web.Endpoints;
namespace Modules.System.Identity
{
    public static class DependencyInjections
    {
        public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("public-signup", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5, Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0, AutoReplenishment = true
                        }));
            });
            services.AddScoped<ICurrentUser, CurrentUser>();

            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<ITenantAccessResolver, TenantAccessResolver>();
            services.AddScoped<TrustedTenantWriteScope>();
            services.AddScoped<AuthorizedPlatformMasterWrite>();
            services.AddScoped<PlatformRoleWriteExecutor>();
            services.AddScoped<PlatformGroupMembershipWriteExecutor>();
            services.AddScoped<PlatformGroupMembershipRemovalService>();
            services.AddScoped<PlatformUserRoleWriteService>();
            services.AddScoped<PlatformUserPermissionWriteService>();
            services.AddScoped<PlatformGroupRoleWriteService>();
            services.AddScoped<PlatformPermissionWriteExecutor>();
            services.AddScoped<TenantSaveChangesInterceptor>();
            services.AddBaseInfrastructureServices<IdentityDbContext>(configuration, "Identity", (sp,options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
            options.UseOpenIddict();
        });
            services.AddScoped<ApiResourceSyncService, ApiResourceSyncService>();
            var assembly = Assembly.GetExecutingAssembly();
            services.AddCustomCqrs<AppMappingProfile>(assembly); services.AddDataProtection();
            services.AddScoped<CaptchaService>();
            services.AddScoped<RefreshGrantValidator>();
            services.AddScoped<IAuthorizationHandler, ActiveSystemAdministratorHandler>();
            // Scoped append-only writer, independent of the rejected grant transaction.
            services.AddScoped<PlatformAccessDenialAudit>();
            services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
            services.AddScoped<IAuthorizationHandler, SystemPermissionHandler>();
            services.AddScoped<IIdentityDbContext>(provider =>
           provider.GetRequiredService<IdentityDbContext>());
            services.AddScoped<IPermissionResolver, RoutePermissionResolver>();
            services.AddScoped<IPermissionChecker, DbPermissionChecker>();
            services.AddOpenIddict()

                .AddCore(options =>
                {
                    options.UseEntityFrameworkCore()
                           .UseDbContext<IdentityDbContext>();
                })

                .AddServer(options =>
                {
                    options.SetTokenEndpointUris("api/system/Auth/login");
                    options.SetAuthorizationEndpointUris("api/connect/authorize");
                    options.SetEndSessionEndpointUris("api/system/Auth/logout");
                    options.SetRevocationEndpointUris("api/system/Auth/revoke");
                    options.SetUserInfoEndpointUris("api/system/Auth/userinfo");

                    options.AllowPasswordFlow();
                    options.AllowRefreshTokenFlow();
                    options.AllowAuthorizationCodeFlow()
                           .RequireProofKeyForCodeExchange();

                    options.AcceptAnonymousClients();

                    options.RegisterScopes("api", "profile", "email", "roles", "permissions", OpenIddictConstants.Scopes.OfflineAccess);

                    options.SetAccessTokenLifetime(TimeSpan.FromMinutes(30));
                    options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));

                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();

                    var aspNetCoreOptions = options.UseAspNetCore()
                           .EnableTokenEndpointPassthrough()
                           .EnableAuthorizationEndpointPassthrough()
                           .EnableEndSessionEndpointPassthrough()
                           .EnableUserInfoEndpointPassthrough();

                    // Local-only E2E exception. Never relax the HTTPS requirement
                    // in Production, even if the CI flag is accidentally supplied.
                    if (environment.IsDevelopment() &&
                        configuration.GetValue<bool>("Identity:AllowInsecureHttpForE2E"))
                    {
                        aspNetCoreOptions.DisableTransportSecurityRequirement();
                    }

                    options.DisableAccessTokenEncryption(); // optional for JWT readability
                })

                .AddValidation(options =>
                {
                    options.UseLocalServer();
                    options.UseAspNetCore();
                });
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });
            services.AddScoped<ISeedContributor, DefaultIdentitySeedContributor>();
            services.AddAuthorization(options =>
            {
                // Privileged System API endpoints are not tenant endpoints.
                // A System account is necessary, but not sufficient: the user must
                // explicitly hold the platform administrator role.
                options.AddPolicy("SystemAdministrator", policy =>
                    policy.RequireAuthenticatedUser()
                          .AddRequirements(new ActiveSystemAdministratorRequirement()));
            });
            return services;
        }
        public static WebApplication UseIdentityModule(this WebApplication app,IConfiguration configuration)
        {
            var assembly = typeof(IdentityDbContext).Assembly;

            app.UseAuthentication();
            // Before authorization, also observes endpoint-level Forbid() responses.
            app.UseMiddleware<PlatformDeniedAccessAuditMiddleware>();
            app.UseAuthorization();
            DbSeeder.SeedAsync(app.Services).GetAwaiter().GetResult();
            // Stop BEFORE endpoint registration/serving any requests if a
            // Production operator turned granular enforcement ON without a
            // reviewed fixed-Master inventory, schema and DB target. No auto-
            // cutover, privilege backfill, or audit bypass is allowed here.
            using (var cutoverScope = app.Services.CreateScope())
            {
                var identityDb = cutoverScope.ServiceProvider
                    .GetRequiredService<IdentityDbContext>();
                PlatformGranularCutoverStartupGuard.ValidateAsync(identityDb,
                    configuration, app.Environment.IsProduction())
                    .GetAwaiter().GetResult();
            }
            //app.UseMiddleware<PermissionMiddleware>();
            app.UseMiddleware<TenantResolutionMiddleware>();
            app.UseRateLimiter();
            PublicSignup.Map(app, configuration["BackendPrefix"] ?? "api/");
            TenantWorkspace.Map(app, configuration["BackendPrefix"] ?? "api/");
            app.MapEndpoints($"{configuration["BackendPrefix"]}System");

            return app;
        }
    }
}
