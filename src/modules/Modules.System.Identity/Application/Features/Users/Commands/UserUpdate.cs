using Microsoft.AspNetCore.Identity;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Commands
{
    public record UserUpdateCommand : MapTo<User>, ICommand<Guid>
    {
        public Guid Id { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
        public required string Firstname { get; set; }
        public required string Lastname { get; set; }
        public required string Email { get; set; }
        public Guid? Avatar { get; set; }

        public override void Mapping(Profile profile)
        {

            profile.CreateMap<UserUpdateCommand, User>()
                .ForMember(x=>x.ImageId,opt=>opt.MapFrom(x=>x.Avatar))
                .ForMember(x => x.NormalizedEmail, opt => opt.MapFrom(x => x.Email.ToUpper()))
                .ForMember(x => x.NormalizedUserName, opt => opt.MapFrom(x => x.Username.ToLower()));
        }
    }
    public class UserUpdateCommandValidator : AbstractValidator<UserUpdateCommand>
    {

    }
    public class UserUpdateCommandHandler(IMapper mapper, IIdentityDbContext context, IPasswordHasher<User> passwordHasher) : ICommandHandler<UserUpdateCommand, Guid>
    {
        public async Task<Guid> Handle(UserUpdateCommand command, CancellationToken cancellationToken)
        {
            User? entity = await context.Users
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            mapper.Map(command, entity);
            entity.PasswordHash = passwordHasher.HashPassword(entity, command.Password);
            await context.SaveChangesAsync(cancellationToken);
            return entity.Id;

        }
    }
}
