using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Commands
{
    public record MenuUpdateCommand : MapTo<Menu>, ICommand
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public Guid? PMenuId { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public required string Title { get; set; }
        public string? Icon { get; set; }
        public short Order { get; set; }
        public string? Url { get; set; }
        public Guid? PermissionId { get; set; }
    }
    public class MenuUpdateCommandValidator : AbstractValidator<MenuUpdateCommand>
    {

    }
    public class MenuUpdateCommandHandler(IIdentityDbContext context, IMapper mapper) : ICommandHandler<MenuUpdateCommand>
    {
        public async Task Handle(MenuUpdateCommand command, CancellationToken cancellationToken)
        {
            User? entity = await context.Users
      .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            mapper.Map(command, entity);

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
