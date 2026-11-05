namespace Domain.SharedKernel.Common.Events
{
    public interface INotification
    {
    }
    public interface IDomainEvent:INotification
    {
        DateTime OccurredOn { get; }
    }
}
