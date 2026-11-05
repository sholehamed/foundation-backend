using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Resources.Queries
{
    public record ResourceLookupQuery(string? text) : IQuery<List<LookupDto>>;
    public class ResourceLookupQueryHandler : IQueryHandler<ResourceLookupQuery, List<LookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public ResourceLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<LookupDto>> Handle(ResourceLookupQuery request, CancellationToken cancellationToken)
        {
            var res = _context.Resources.Where(x => x.IsActive);
            if (!string.IsNullOrEmpty(request.text))
                res = res.Where(x => x.Title.Contains(request.text));
            var list = await res.Select(x => new LookupDto(x.Id, x.Title)).ToListAsync();
            return list;
        }
    }
}
