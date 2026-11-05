using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Menus.Commands
{
    public record MenuReorderCommand:ICommand
    {
        public Guid CategoryId { get; init; }
        public List<MenuOrderItemDto> Items { get; init; } = new();
    }
    public record MenuOrderItemDto
    {
        public Guid Id { get; init; }
        public Guid? ParentId { get; init; }
        public short Order { get; init; }
    }
    public class MenuReorderCommandHandler : ICommandHandler<MenuReorderCommand>
    {
        private readonly IIdentityDbContext _context;

        public MenuReorderCommandHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task Handle(MenuReorderCommand command, CancellationToken ct)
        {
            var menus = await _context.Menus
                   .Where(x => x.CategoryId == command.CategoryId)
                   .ToListAsync(ct);

            // ۲. اعمال تغییرات بر اساس لیست ارسالی از فرانت
            foreach (var itemUpdate in command.Items)
            {
                var menu = menus.FirstOrDefault(x => x.Id == itemUpdate.Id);
                if (menu != null)
                {
                    menu.ParentId = itemUpdate.ParentId;
                    menu.Order = itemUpdate.Order;
                }
            }

            // ۳. ذخیره تغییرات به صورت یکپارچه (Bulk Update توسط ردیابی EF انجام می‌شود)
            await _context.SaveChangesAsync(ct);
        }
    }
}
