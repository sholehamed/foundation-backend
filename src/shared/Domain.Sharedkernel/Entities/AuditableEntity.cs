using Domain.SharedKernel.Common;
using Domain.SharedKernel.Common.Events;

namespace Domain.SharedKernel.Entities
{
    public interface IAuditableEntity: IAuditable, IEntity, IHasDomainEvents
    {
        DateTime CreatedAt { get; set; }
        Guid CreatedBy { get; set; }
        string? CreatedByIp { get; set; }
        DateTime? LastModifiedAt { get; set; }
        Guid? LastModifiedBy { get; set; }
        string? LastModifiedByIp { get; set; }

        void Create(Guid userId, string ip);
        void Modify(Guid userId, string ip);
    }

    public abstract class AuditableEntity : Entity, IAuditable, IAuditableEntity
    {
        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByIp { get; set; }
        public Guid? LastModifiedBy { get; set; }
        public DateTime? LastModifiedAt { get; set; }
        public string? LastModifiedByIp { get; set; }
        protected AuditableEntity() : base(Guid.CreateVersion7())
        {

        }
        protected AuditableEntity(ushort tableCode) : base(tableCode)
        {

        }
        protected AuditableEntity(ulong id, ushort tableCode) : base(id, tableCode)
        {

        }
        protected AuditableEntity(Guid id) : base(id)
        {

        }
        public void Create(Guid userId, string ip)
        {
            CreatedBy = userId;
            CreatedByIp = ip;
            CreatedAt = DateTime.Now;
        }

        public void Modify(Guid userId, string ip)
        {
            LastModifiedBy = userId;
            LastModifiedByIp = ip;
            LastModifiedAt = DateTime.Now;
        }
    }
}
