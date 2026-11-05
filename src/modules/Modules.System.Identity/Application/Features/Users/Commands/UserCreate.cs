using Microsoft.AspNetCore.Identity;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Commands
{
    public record UserCreateCommand : MapTo<User>, ICommand<Guid>
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public required string Firstname { get; set; }
        public required string Lastname { get; set; }
        public required string Email { get; set; }
        public Guid? Avatar { get; set; }
        public override void Mapping(Profile profile)
        {

            profile.CreateMap<UserCreateCommand, User>()
                .ForMember(x => x.ImageId, opt => opt.MapFrom(x => x.Avatar))
                .ForMember(x => x.NormalizedEmail, opt => opt.MapFrom(x => x.Email.ToUpper()))
                .ForMember(x => x.NormalizedUserName, opt => opt.MapFrom(x => x.Username.ToLower()));
        }
    }
    public class UserCreateCommandValidator : AbstractValidator<UserCreateCommand>
    {

    }
    public class UserCreateCommandHandler(IMapper mapper, IIdentityDbContext context, IPasswordHasher<User> passwordHasher) : ICommandHandler<UserCreateCommand, Guid>
    {
        public async Task<Guid> Handle(UserCreateCommand command, CancellationToken cancellationToken)
        {
            User entity = mapper.Map<User>(command);
            entity.TenantId = Guid.Parse("019EDD61-E3EA-722E-B5BE-0EE5C2BE51DC");
            entity.PasswordHash = passwordHasher.HashPassword(entity, command.Password);
            await context.Users.AddAsync(entity, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return entity.Id;

        }
    }
}
