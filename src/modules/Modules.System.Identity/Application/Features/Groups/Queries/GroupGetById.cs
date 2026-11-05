using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Groups.System.Identity.Application.Features.Groups.Queries
{
    public record GroupGetByIdQuery(Guid Id) : IQuery<GroupGetByIdDto>
    {
    }

    public record GroupGetByIdDto : MapFrom<Group>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

    }
    public class GroupGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<GroupGetByIdQuery, GroupGetByIdDto>
    {
        public async Task<GroupGetByIdDto> Handle(GroupGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Groups.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<GroupGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}
