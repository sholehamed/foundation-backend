using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.ApiResources.Queries
{
    public record ApiResourceLookupQuery(string? text) : IQuery<List<LookupDto>>;
    public class ApiResourceLookupQueryHandler : IQueryHandler<ApiResourceLookupQuery, List<LookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public ApiResourceLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<LookupDto>> Handle(ApiResourceLookupQuery request, CancellationToken cancellationToken)
        {
            var res = _context.ApiResources.Where(x => x.IsActive);
            if (!string.IsNullOrEmpty(request.text))
                res = res.Where(x => x.Title.Contains(request.text));
            var list = await res.Select(x => new LookupDto(x.Id, $"{x.Title}-{x.RouteTemplate}")).ToListAsync();
            return list;
        }
    }
}
