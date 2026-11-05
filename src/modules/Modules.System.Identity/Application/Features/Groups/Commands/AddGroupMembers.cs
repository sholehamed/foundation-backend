using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Groups.Commands
{
    public record AddGroupMembersCommand : ICommand
    {
        [global::System.Text.Json.Serialization.JsonIgnore]
        public Guid StorageTenantId { get; set; }

        public Guid? GroupId { get; set; }
        public List<Guid> UserIds { get; set; } = [];
    }

    public class AddGroupMembersCommandHandler(IIdentityDbContext context)
        : ICommandHandler<AddGroupMembersCommand>
    {
        public async Task Handle(AddGroupMembersCommand command, CancellationToken cancellationToken)
        {
            if (command.StorageTenantId == Guid.Empty || command.GroupId is null ||
                command.GroupId == Guid.Empty)
                throw new UnauthorizedAccessException("An authorized storage scope and group are required.");

            var ids = (command.UserIds ?? []).Distinct().ToArray();
            if (ids.Length == 0 || ids.Any(id => id == Guid.Empty))
                throw new UnauthorizedAccessException("Valid member IDs are required.");

            var groupId = command.GroupId.Value;
            var groupIsValid = await context.Groups.AsNoTracking().AnyAsync(
                x => x.Id == groupId && x.TenantId == command.StorageTenantId && x.IsActive,
                cancellationToken);
            var validUsers = await context.Users.AsNoTracking().CountAsync(
                x => ids.Contains(x.Id) && x.UserType == UserType.System &&
                     x.TenantId == null && x.IsActive, cancellationToken);
            if (!groupIsValid || validUsers != ids.Length)
                throw new UnauthorizedAccessException("Cross-scope group membership is forbidden.");

            // Never use an untrusted TenantId in the request body.
            var existing = await context.UserGroups.AsNoTracking()
                .Where(x => x.GroupId == groupId &&
                            x.TenantId == command.StorageTenantId && ids.Contains(x.UserId))
                .Select(x => x.UserId).ToListAsync(cancellationToken);
            var present = existing.ToHashSet();

            foreach (var id in ids.Where(id => !present.Contains(id)))
                context.UserGroups.Add(new UserGroup
                {
                    TenantId = command.StorageTenantId,
                    UserId = id,
                    GroupId = groupId
                });

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
