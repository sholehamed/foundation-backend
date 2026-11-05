using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Permissions.Commands
{
    public record PermissionResource
    {
        public string Type { get; set; }
        public Guid Id { get; set; }
    }
    public record PermissionUpdateCommand : MapTo<Permission>, ICommand
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public Guid ResourceId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public ICollection<PermissionResource>? PermissionResources { get; set; }
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<PermissionUpdateCommand, Permission>()
                .ForMember(x => x.Id, opt => opt.Ignore())
                .ForMember(x => x.ApiResources, opt => opt.Ignore())
                .ForMember(x => x.UiResources, opt => opt.Ignore());
        }
        public class PermissionUpdateCommandValidator : AbstractValidator<PermissionUpdateCommand>
        {
            private readonly IIdentityDbContext _context;

            public PermissionUpdateCommandValidator(IIdentityDbContext context)
            {
                this._context = context;
                RuleFor(x => x.Id)
             .NotEmpty();
                RuleFor(x => x.Code)
             .NotEmpty()
             .MaximumLength(100)
             .MustAsync(BeUniqueCode).WithMessage("کد پرمیشن وارد شده تکراری است.");

                RuleFor(x => x.Name)
                    .NotEmpty()
                    .MaximumLength(200);

                RuleFor(x => x.ResourceId)
                    .NotEmpty();
            }
            private async Task<bool> BeUniqueCode(PermissionUpdateCommand command, string code, CancellationToken cancellationToken)
            {
                // بررسی اینکه آیا کدی مشابه با شناسه متفاوت وجود دارد یا خیر
                return !await _context.Permissions
                    .AnyAsync(x => x.Code == code && x.Id != command.Id, cancellationToken);
            }
        }
        public class PermissionUpdateCommandHandler : ICommandHandler<PermissionUpdateCommand>
        {
            private readonly IIdentityDbContext _context;
            private readonly IMapper _mapper;

            public PermissionUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
            {
                _context = identityDbContext;
                _mapper = mapper;
            }

            public async Task Handle(PermissionUpdateCommand command, CancellationToken cancellationToken)
            {
                // The resource collection is optional for this command.
                var resources = command.PermissionResources ?? [];
                var apiResources = resources
                    .Where(x => x.Type == "API")
                    .Select(x => x.Id)
                    .ToList();

                var uiResources = resources
                    .Where(x => x.Type == "UI")
                    .Select(x => x.Id)
                    .ToList();

                var entity = await _context.Permissions
                    .Include(x => x.ApiResources)
                    .Include(x => x.UiResources)
                    .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

                Guard.Against.NotFound(command.Id, entity);

                _mapper.Map(command, entity);

                var newApiResourceIds = apiResources.Distinct().ToHashSet();
                var currentApiResourceIds = entity.ApiResources.Select(x => x.ApiResourceId).ToHashSet();

                var apiResourcesToRemove = entity.ApiResources
                    .Where(x => !newApiResourceIds.Contains(x.ApiResourceId))
                    .ToList();

                foreach (var item in apiResourcesToRemove)
                {
                    _context.PermissionApiResources.Remove(item);
                }

                var apiResourcesToAdd = newApiResourceIds
                    .Where(x => !currentApiResourceIds.Contains(x))
                    .Select(x => new PermissionApiResource
                    {
                        PermissionId = entity.Id,
                        ApiResourceId = x
                    });

                foreach (var item in apiResourcesToAdd)
                {
                    _context.PermissionApiResources.Add(item);
                }

                var newUiResourceIds = uiResources.Distinct().ToHashSet();
                var currentUiResourceIds = entity.UiResources.Select(x => x.UiResourceId).ToHashSet();

                var uiResourcesToRemove = entity.UiResources
                    .Where(x => !newUiResourceIds.Contains(x.UiResourceId))
                    .ToList();

                foreach (var item in uiResourcesToRemove)
                {
                    _context.PermissionUiResources.Remove(item);
                }

                var uiResourcesToAdd = newUiResourceIds
                    .Where(x => !currentUiResourceIds.Contains(x))
                    .Select(x => new PermissionUiResource
                    {
                        PermissionId = entity.Id,
                        UiResourceId = x
                    });

                foreach (var item in uiResourcesToAdd)
                {
                    _context.PermissionUiResources.Add(item);
                }

                await _context.SaveChangesAsync(cancellationToken);
            }

        }
    }
}