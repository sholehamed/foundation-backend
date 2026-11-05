using Domain.Sharedkernel.Util;
using Domain.SharedKernel.Common;
using Domain.SharedKernel.Common.Events;

namespace Domain.SharedKernel.Entities
{
    public class Entity : IEntity,IHasDomainEvents
    {
        public Guid Id { get; private set; }
        public byte[] RowVersion { get; set; }
        private readonly List<IDomainEvent> _domainEvents = new();

        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        protected void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }
        public Entity()
        {
            
        }
        public Entity(ushort code)
        {
            Id=StructuredGuidV7.Create(code);
        }
        public Entity(ulong id,ushort code)
        {
            Id = StructuredGuidV7.CreateDeterministic(code, id);
        }


        public Entity(Guid id)=>Id = id;
       
    }
   
}
