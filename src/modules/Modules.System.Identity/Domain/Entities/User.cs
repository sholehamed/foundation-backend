using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public enum UserType:byte
    {
        System = 1,
        Tenant = 2
    }
    public class User : AuditableEntity
    {
        public User() : base(17) { }
        public User(ulong id) : base(id, 17) { }
        public User(Guid id) : base(id) { }
        public User(Guid? tenantId, string userName, string? email,  string? firstName, string? lastName,UserType userType, bool isActive=true) : base(17)
        {
            UserType = userType;
            TenantId = tenantId;
            UserName = userName;
            Email = email;
          
            FirstName = firstName;
            LastName = lastName;
            IsActive = isActive;
           
        }
        public User(Guid id,Guid? tenantId, string userName, string? email, string? firstName, string? lastName,UserType userType, bool isActive = true) : base(id)
        {
            UserType = userType;
            TenantId = tenantId;
            UserName = userName;
            Email = email;

            FirstName = firstName;
            LastName = lastName;
            IsActive = isActive;

        }
        public Guid? ImageId { get; set; }
        public Guid? TenantId { get; set; }
        public UserType UserType { get; set; }
        public string UserName { get; set; } = default!;
        public string NormalizedUserName { get; set; } = default!;

        public string? Email { get; set; }
        public string? NormalizedEmail { get; set; }
        public bool EmailConfirmed { get; set; }

        public string PasswordHash { get; set; } = default!;
        public string? SecurityStamp { get; set; }
        public string? ConcurrencyStamp { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsLockedOut { get; set; }
        public DateTime? LockoutEndUtc { get; set; }

        public int AccessFailedCount { get; set; }
        public virtual Tenant? Tenant { get; set; }
        public ICollection<UserRole> UserRoles { get; set; } = [];
        public ICollection<UserGroup> UserGroups { get; set; } = [];
        public ICollection<UserPermission> UserPermissions { get; set; } = [];
    }

}
