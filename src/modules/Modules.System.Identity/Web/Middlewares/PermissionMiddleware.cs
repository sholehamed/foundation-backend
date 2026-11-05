namespace Modules.System.Identity.Web.Middlewares
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Modules.System.Identity.Web.Util;

    public class PermissionMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(
            HttpContext context,
            IPermissionResolver permissionResolver,
            IPermissionChecker permissionChecker)
        {
            var endpoint = context.GetEndpoint();

     

            if (endpoint is null)
            {
                await next(context);
                return;
            }

            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                await next(context);
                return;
            }
            if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null)
            {
                await next(context);
                return;
            }


            var permission = permissionResolver.Resolve(context);

            if (string.IsNullOrWhiteSpace(permission))
            {
                await next(context);
                return;
            }

            if (context.User?.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "unauthorized",
                    error_description = "Authentication is required."
                });

                return;
            }

            var hasPermission = await permissionChecker.HasPermissionAsync(
                context.User,
                permission,
                context.RequestAborted);

            if (!hasPermission)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "forbidden",
                    error_description = $"Missing permission: {permission}"
                });

                return;
            }

            await next(context);
        }
    }

}
