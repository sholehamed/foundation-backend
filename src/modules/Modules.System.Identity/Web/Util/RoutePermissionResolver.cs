using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using System.Security.Claims;
using System.Text;

namespace Modules.System.Identity.Web.Util
{
    public interface IPermissionResolver
    {
        string? Resolve(HttpContext context);
    }
    public class RoutePermissionResolver : IPermissionResolver
    {
        public string? Resolve(HttpContext context)
        {
            var path = context.Request.Path.Value;

            var endpoint = context.GetEndpoint() as RouteEndpoint;
            var routeTemplate = endpoint.RoutePattern.RawText?.ToLower();
            var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
            var httpMethod = methodMetadata?.HttpMethods.FirstOrDefault() ?? "GET";
            string[] s = routeTemplate.Replace("/api/", "").Replace("{", "").Replace("}", "").ToUpperInvariant().Split('/');
            StringBuilder sb = new StringBuilder();
            sb.AppendJoin('.', s);
            sb.Append($".{httpMethod}");
            string key = sb.ToString();
            return key;
        }
    }

public interface IPermissionChecker
    {
        Task<bool> HasPermissionAsync(
            ClaimsPrincipal user,
            string permission,
            CancellationToken cancellationToken = default);
    }

}
