using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using System.Text;

namespace Modules.System.Identity.Application.Features.ApiResources.Services
{


    public class ApiResourceSyncService
    {
        private readonly EndpointDataSource _endpointDataSource;
        private readonly IIdentityDbContext _db;

        public ApiResourceSyncService(EndpointDataSource endpointDataSource, IIdentityDbContext db)
        {
            _endpointDataSource = endpointDataSource;
            _db = db;
        }

        public async Task SyncMinimalApiResourcesAsync(Guid resourceId)
        {
            var resource = await _db.Resources
                .Where(x => x.Id == resourceId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(resource))
                return;

            var normalizedResource = resource.Trim();

            var discoveredEndpoints = _endpointDataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint =>
                {
                    var tagsMetadata = endpoint.Metadata.GetMetadata<ITagsMetadata>();

                    return tagsMetadata?.Tags.Any(tag =>
                        string.Equals(tag, normalizedResource, StringComparison.OrdinalIgnoreCase)) == true;
                })
                .Select(endpoint =>
                {
                    var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
                    var httpMethod = methodMetadata?.HttpMethods.FirstOrDefault() ?? "GET";
                    
                    var routeTemplate = endpoint.RoutePattern.RawText?.ToLower();
                    var allowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;
                    var title = endpoint.Metadata.GetMetadata<EndpointTitleMetadata>()?.Title;

                    string[] s = routeTemplate.Replace("/api/","").Replace("{","").Replace("}","").ToUpperInvariant().Split('/');
                    s = s.Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    StringBuilder sb = new StringBuilder();
                    sb.AppendJoin('.', s);
                    sb.Append($".{httpMethod}");
                    string key = sb.ToString();
                    return new
                    {
                        Key = key,
                        Title = title,
                        RouteTemplate = routeTemplate,
                        Method = Enum.Parse<Domain.Entities.HttpMethod>(httpMethod.ToUpper()),
                        AllowAnonymous = allowAnonymous
                    };
                })
                .ToList();

            var existingResources = await _db.ApiResources
                .Where(x => x.ResourceId == resourceId)
                .ToListAsync();

            var newEndpoints = discoveredEndpoints
                .Where(d => !existingResources.Any(e => e.Key == d.Key))
                .Select(d => new ApiResource
                {
                    ResourceId = resourceId,
                    Key = d.Key,
                    Title = d.Title,
                    RouteTemplate = d.RouteTemplate,
                    HttpMethod = d.Method,
                    Source = ApiSource.Auto,
                    AllowAnonymous = d.AllowAnonymous,
                    IsActive = true,
                    IsDeprecated = false
                }).ToList();

            // FR-02A: An endpoint temporarily disappearing from an inventory
            // is NOT evidence that an operator intended to revoke its mapping.
            // Report drift through reviewed FeatureRegistry/preview. Never
            // auto-deprecate or disable existing API records here.

            var toUpdate = existingResources
                .Where(e => e.Source == ApiSource.Auto)
                .Join(discoveredEndpoints, e => e.Key, d => d.Key, (e, d) => new { Existing = e, Discovered = d });

            foreach (var item in toUpdate)
            {
                item.Existing.RouteTemplate = item.Discovered.RouteTemplate;
                item.Existing.AllowAnonymous = item.Discovered.AllowAnonymous;
                // Do not overwrite an operator's disabled/deprecated choices.
                // This legacy route-key sync is not a permission grant tool; see
                // FeatureRegistry/preview + apply for stable code-owned keys.
            }

            if (newEndpoints.Any())
                await _db.ApiResources.AddRangeAsync(newEndpoints);

            await _db.SaveChangesAsync();
        }
    }

}
