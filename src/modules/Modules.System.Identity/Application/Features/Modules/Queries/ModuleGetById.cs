using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Modules.Queries
{
    public record ModuleGetByIdQuery(Guid Id) : IQuery<ModuleGetByIdDto>
    {
    }

    public record ModuleGetByIdDto : MapFrom<Module>
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

    }
    public class ModuleGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<ModuleGetByIdQuery, ModuleGetByIdDto>
    {
        public async Task<ModuleGetByIdDto> Handle(ModuleGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Modules.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<ModuleGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}
