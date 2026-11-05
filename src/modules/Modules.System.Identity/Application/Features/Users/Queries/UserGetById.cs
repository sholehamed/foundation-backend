using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Queries
{
    public record UserGetByIdQuery(Guid Id) : IQuery<UserGetByIdDto>
    {
    }

    public record UserGetByIdDto : MapFrom<User>
    {
        public Guid Id { get; set; }
        public required string UserName { get; set; }
        public required string Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public Guid? Avatar { get; set; }

        public bool IsActive { get; set; }
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<User, UserGetByIdDto>()
                 .ForMember(x => x.Avatar, opt => opt.MapFrom(x => x.ImageId));
        }
    }
    public class UserGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<UserGetByIdQuery, UserGetByIdDto>
    {
        public async Task<UserGetByIdDto> Handle(UserGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Users.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<UserGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}

