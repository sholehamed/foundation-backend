using Microsoft.AspNetCore.HttpOverrides;
using Modules.System.FileStorage;
using Modules.System.Identity;
using Scalar.AspNetCore;
using Scalar.ClientGeneration;

namespace Server
{
    public static class DependencyInjections
    {
        public static void RegisterServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddOpenApi();
            builder.Services.AddAntiforgery();
            builder.Services.AddMediaStorage(builder.Configuration);
            builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
            builder.Services.AddScalarClientGeneration();

            // Salon Modules


            var explicitOrigins = new HashSet<string>(
                (builder.Configuration["Cors"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            var workspaceDomain = (builder.Configuration["Tenant:WorkspaceBaseDomain"] ?? "shinera.ir")
                .Trim().Trim('.').ToLowerInvariant();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("Cors", policy =>
                {
                    policy
                        .SetIsOriginAllowed(origin =>
                        {
                            if (explicitOrigins.Contains(origin)) return true;
                            // Dev Angular subdomains (e.g. salon.shinera.localhost:4200)
                            // must reach the local backend HTTPS port. In production
                            // use same-origin /api proxy; never wildcard arbitrary origins.
                            if (!builder.Environment.IsDevelopment() ||
                                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                                return false;
                            var host = uri.Host.ToLowerInvariant();
                            var suffix = "." + workspaceDomain;
                            var slug = host.EndsWith(suffix, StringComparison.Ordinal)
                                ? host[..^suffix.Length]
                                : "";
                            return host == workspaceDomain ||
                                (slug.Length > 0 && !slug.Contains('.') &&
                                 slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'));
                        })
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
        }
        public static void RegisterApps(this WebApplication app, IConfiguration configuration)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseCors("Cors");
            app.UseMediaStorage(configuration);
            app.UseIdentityModule(configuration);

            // Salon Modules
          

            app.UseAntiforgery();
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarClientGenerationAssets();
                app.MapScalarWithClientGeneration("apis");
            }

        }
    }
}
