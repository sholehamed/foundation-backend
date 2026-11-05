using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Resources.Queries
{
    public record GroupedLookupDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
    }
    public record ResourceGetApiUiResourcesLookupQuery(Guid resourceId, string? text) : IQuery<List<GroupedLookupDto>>;
    public class ResourceGetApiUiResourcesLookupQueryHandler : IQueryHandler<ResourceGetApiUiResourcesLookupQuery, List<GroupedLookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public ResourceGetApiUiResourcesLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<GroupedLookupDto>> Handle(ResourceGetApiUiResourcesLookupQuery request, CancellationToken cancellationToken)
        {
            var list1 = _context.ApiResources.AsNoTracking().Where(x => x.ResourceId == request.resourceId).Select(x => new GroupedLookupDto { Id = x.Id, Type = "API", Name = x.Title });
            var list2 = _context.UiResources.AsNoTracking().Where(x => x.ResourceId == request.resourceId).Select(x => new GroupedLookupDto { Id = x.Id, Type = "UI", Name = x.Title });
            var list = await list1.Union(list2).OrderBy(x => x.Type).ThenBy(x => x.Name).ToListAsync();
            return list;
        }
    }
}
