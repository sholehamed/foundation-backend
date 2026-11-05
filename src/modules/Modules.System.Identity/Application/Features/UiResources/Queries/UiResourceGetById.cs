using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.UiResources.Queries
{
    public record UiResourceGetByIdQuery(Guid Id) : IQuery<UiResourceGetByIdDto>
    {
    }

    public record UiResourceGetByIdDto : MapFrom<UiResource>
    {
        public Guid Id { get; set; }
        public Guid ResourceId { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public UiResourceType Type { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }

    }
    public class UiResourceGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<UiResourceGetByIdQuery, UiResourceGetByIdDto>
    {
        public async Task<UiResourceGetByIdDto> Handle(UiResourceGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.UiResources.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<UiResourceGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}
