using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Dispatcher;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class FoundationCqrsTraceTests
{
    public sealed record ProbeCommand(string UserEnteredSecret) : ICommand<int>;
    public sealed record ProbeQuery(string UserEnteredSecret) : IQuery<int>;
    public sealed record FailedCommand(string UserEnteredSecret) : ICommand;

    public sealed class ProbeHandler : ICommandHandler<ProbeCommand, int>
    {
        public Task<int> Handle(ProbeCommand command, CancellationToken token)
            => Task.FromResult(42);
    }

    public sealed class ProbeQueryHandler : IQueryHandler<ProbeQuery, int>
    {
        public Task<int> Handle(ProbeQuery query, CancellationToken token)
            => Task.FromResult(7);
    }

    public sealed class FailingHandler : ICommandHandler<FailedCommand>
    {
        public Task Handle(FailedCommand command, CancellationToken token)
            => throw new InvalidOperationException("SensitiveExceptionMessage");
    }

    [Fact]
    public async Task Dispatcher_emits_nested_traces_without_command_values_result_or_exception_message()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Foundation.Cqrs" or "fr18d.test.root",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Source.Name == "Foundation.Cqrs") spans.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("fr18d.test.root");
        using var root = source.StartActivity("incoming.request", ActivityKind.Server);
        Assert.NotNull(root);

        var services = new ServiceCollection()
            .AddTransient<ICommandHandler<ProbeCommand, int>, ProbeHandler>()
            .AddTransient<IQueryHandler<ProbeQuery, int>, ProbeQueryHandler>()
            .AddTransient<ICommandHandler<FailedCommand>, FailingHandler>()
            .BuildServiceProvider();
        using (services)
        {
            var dispatcher = new Dispatcher(services);
            Assert.Equal(42, await dispatcher.Send(
                new ProbeCommand("DO_NOT_EXPORT_SECRET_COMMAND")));
            Assert.Equal(7, await dispatcher.Query(
                new ProbeQuery("DO_NOT_EXPORT_SECRET_QUERY")));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dispatcher.Send(new FailedCommand("DO_NOT_EXPORT_SECRET_FAILURE")));
        }

        Assert.Equal(3, spans.Count);
        Assert.All(spans, span =>
        {
            Assert.Equal(root.TraceId, span.TraceId);
            Assert.Equal(root.SpanId, span.ParentSpanId);
            var all = span.DisplayName + string.Join(";", span.TagObjects.Select(
                pair => pair.Key + ":" + pair.Value));
            Assert.DoesNotContain("DO_NOT_EXPORT", all);
            Assert.DoesNotContain("SensitiveExceptionMessage", all);
        });
        Assert.Contains(spans, span => span.GetTagItem("foundation.cqrs.request_type")?.ToString()
            == nameof(ProbeQuery));
        var failed = Assert.Single(spans,
            span => span.GetTagItem("foundation.cqrs.request_type")?.ToString()
                == nameof(FailedCommand));
        Assert.Equal(ActivityStatusCode.Error, failed.Status);
        Assert.Equal(nameof(InvalidOperationException),
            failed.GetTagItem("foundation.error.type"));
    }
}
