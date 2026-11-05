using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

public class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IDispatcher _dispatcher;

    public DispatchDomainEventsInterceptor(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ChangeTracker ct = eventData.Context!.ChangeTracker;
        var domainEvents = ct
           .Entries<IHasDomainEvents>()
           .Select(x => x.Entity)
           .SelectMany(x => x.DomainEvents)
           .ToList();


        foreach (var domainEvent in domainEvents)
        {
            await PublishDomainEvent(domainEvent, cancellationToken);
        }

        ClearDomainEvents(ct);

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private async Task PublishDomainEvent(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var method = typeof(IDispatcher)
            .GetMethods()
            .First(x => x.Name == nameof(IDispatcher.Publish) &&
                        x.IsGenericMethod &&
                        x.GetGenericArguments().Length == 1 &&
                        x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() != typeof(CancellationToken));

        var genericMethod = method.MakeGenericMethod(domainEvent.GetType());
        var task = (Task)genericMethod.Invoke(_dispatcher, new object[] { domainEvent, cancellationToken })!;
        await task;
    }

    private void ClearDomainEvents(ChangeTracker changeTracker)
    {
        var aggregates = changeTracker
            .Entries<IHasDomainEvents>()
            .Select(x => x.Entity)
            .ToList();

        aggregates.ForEach(a => a.ClearDomainEvents());
    }

}