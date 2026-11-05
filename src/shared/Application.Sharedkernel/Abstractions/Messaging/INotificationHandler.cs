using Domain.SharedKernel.Common.Events;

namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface INotificationHandler<in TNotification>
    where TNotification : INotification
    {
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }
}
