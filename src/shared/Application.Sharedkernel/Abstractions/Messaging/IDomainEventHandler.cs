using Domain.SharedKernel.Common.Events;

namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface IDomainEventHandler<in TDomainEvent>:INotificationHandler<TDomainEvent>
    where TDomainEvent : IDomainEvent
    {
    }
}
