using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public enum HttpMethod
    {
        POST,
        PUT,
        PATCH,
        DELETE,
        GET
    }
    public enum ApiSource
    {
        /// <summary>
        /// call endpoints  auto discovery
        /// </summary>
        Auto,
        /// <summary>
        /// add manual
        /// </summary>
        Manual
    }
    public class ApiResource: AuditableEntity
    {
        public ApiResource() : base(1) { }
        public ApiResource(ulong id) : base(id, 1) { }
        public ApiResource(Guid id) : base(id) { }
        public ApiResource(Guid resourceId,string key, string title, string? description, HttpMethod httpMethod, string routeTemplate) : base(1)
        {
            ResourceId = resourceId;
            Key = key;
            Title = title;
            Description = description;
            HttpMethod = httpMethod;
            RouteTemplate = routeTemplate;
            Source = ApiSource.Auto;
            AllowAnonymous = false;
            IsActive = true;
            IsDeprecated = false;
        }
        public ApiResource(Guid id,Guid resourceId, string key, string title, string? description, HttpMethod httpMethod, string routeTemplate) : base(id)
        {
            ResourceId = resourceId;
            Key = key;
            Title = title;
            Description = description;
            HttpMethod = httpMethod;
            RouteTemplate = routeTemplate;
            Source = ApiSource.Auto;
            AllowAnonymous = false;
            IsActive = true;
            IsDeprecated = false;
        }

        public Guid ResourceId { get; set; }
        public virtual Resource? Resource { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public HttpMethod HttpMethod { get; set; }
        public string RouteTemplate { get; set; }
        public ApiSource Source { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeprecated { get; set; }
        public virtual ICollection<PermissionApiResource> PermissionApiResources { get; set; }
        = [];
    }
}