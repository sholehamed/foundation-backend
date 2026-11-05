using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.ApiResources.Queries
{
    public record ApiResourceGetByIdQuery(Guid Id) : IQuery<ApiResourceGetByIdDto>
    {
    }

    public record ApiResourceGetByIdDto : MapFrom<ApiResource>
    {
        public Guid Id { get; set; }
        public Guid ResourceId { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public Domain.Entities.HttpMethod HttpMethod { get; set; }
        public string RouteTemplate { get; set; }
        public ApiSource Source { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeprecated { get; set; }


    }
    public class ApiResourceGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<ApiResourceGetByIdQuery, ApiResourceGetByIdDto>
    {
        public async Task<ApiResourceGetByIdDto> Handle(ApiResourceGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.ApiResources.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<ApiResourceGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}
