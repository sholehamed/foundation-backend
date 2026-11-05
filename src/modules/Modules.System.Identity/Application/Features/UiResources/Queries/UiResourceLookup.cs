using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.UiResources.Queries
{
    public record UiResourceLookupQuery(string? text) : IQuery<List<LookupDto>>;
    public class UiResourceLookupQueryHandler : IQueryHandler<UiResourceLookupQuery, List<LookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public UiResourceLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<LookupDto>> Handle(UiResourceLookupQuery request, CancellationToken cancellationToken)
        {
            var res = _context.UiResources.Where(x => x.IsActive);
            if (!string.IsNullOrEmpty(request.text))
                res = res.Where(x => x.Title.Contains(request.text));
            var list = await res.Select(x => new LookupDto(x.Id, $"{x.Title}")).ToListAsync();
            return list;
        }
    }
}
