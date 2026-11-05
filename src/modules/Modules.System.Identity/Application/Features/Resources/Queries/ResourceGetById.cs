using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Resources.Queries
{
    public record ResourceGetByIdQuery(Guid Id) : IQuery<ResourceGetByIdDto>
    {
    }

    public record ResourceGetByIdDto : MapFrom<Resource>
    {
        public Guid Id { get; set; }
        public Guid ModuleId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

    }
    public class ResourceGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<ResourceGetByIdQuery, ResourceGetByIdDto>
    {
        public async Task<ResourceGetByIdDto> Handle(ResourceGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Resources.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<ResourceGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}
