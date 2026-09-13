using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Command.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Application.Tests.Shared;

[TestFixture]
public sealed class CommandDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WhenAlreadyCancelled_CompletesRequest()
    {
        using var fixture = new DispatcherFixture(maxConcurrency: 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var completion = NewCompletionSource();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fixture.Dispatcher.DispatchAsync(NewCommand(completion, cancellation.Token)));
        Assert.That(completion.Task.IsCanceled, Is.True);
    }

    [Test]
    public async Task QueuedCancellation_DoesNotExecuteTheCancelledService()
    {
        using var fixture = new DispatcherFixture(maxConcurrency: 1);
        await fixture.Dispatcher.StartAsync(CancellationToken.None);

        var firstCompletion = NewCompletionSource();
        var first = NewCommand(firstCompletion, CancellationToken.None);
        await fixture.Dispatcher.DispatchAsync(first);
        await fixture.Service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var secondCancellation = new CancellationTokenSource();
        var secondCompletion = NewCompletionSource();
        await fixture.Dispatcher.DispatchAsync(NewCommand(secondCompletion, secondCancellation.Token));
        secondCancellation.Cancel();

        fixture.Service.ReleaseFirst.TrySetResult();
        await secondCompletion.Task.ContinueWith(_ => { }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(secondCompletion.Task.IsCanceled, Is.True);
            Assert.That(fixture.Service.ExecutionCount, Is.EqualTo(1));
        });

        await fixture.Dispatcher.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Shutdown_CancelsActiveAndSettlesQueuedRequests()
    {
        using var fixture = new DispatcherFixture(maxConcurrency: 1);
        await fixture.Dispatcher.StartAsync(CancellationToken.None);

        var activeCompletion = NewCompletionSource();
        var queuedCompletion = NewCompletionSource();
        await fixture.Dispatcher.DispatchAsync(NewCommand(activeCompletion, CancellationToken.None));
        await fixture.Service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Dispatcher.DispatchAsync(NewCommand(queuedCompletion, CancellationToken.None));

        await fixture.Dispatcher.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(activeCompletion.Task.IsCanceled, Is.True);
            Assert.That(queuedCompletion.Task.IsCanceled, Is.True);
            Assert.That(fixture.Service.IsDisposed, Is.True);
        });
    }

    [Test]
    public async Task Saturation_EveryRequestCompletionIsSettled()
    {
        using var fixture = new DispatcherFixture(maxConcurrency: 1);
        await fixture.Dispatcher.StartAsync(CancellationToken.None);

        var completions = Enumerable.Range(0, 105).Select(_ => NewCompletionSource()).ToArray();
        foreach (var completion in completions)
            await fixture.Dispatcher.DispatchAsync(NewCommand(completion, CancellationToken.None));

        await fixture.Service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.ReleaseFirst.TrySetResult();

        var settled = completions.Select(x => x.Task.ContinueWith(_ => { })).ToArray();
        await Task.WhenAll(settled).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(completions.All(x => x.Task.IsCompleted), Is.True);
        await fixture.Dispatcher.StopAsync(CancellationToken.None);
    }

    private static TaskCompletionSource<CommandResult> NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static QueuedCommand NewCommand(
        TaskCompletionSource<CommandResult> completion,
        CancellationToken cancellationToken) =>
        new(new Proto.ExecuteRequest { CommandName = "test" }, completion, cancellationToken);

    private sealed class DispatcherFixture : IDisposable
    {
        private readonly ServiceProvider m_Provider;

        public DispatcherFixture(int maxConcurrency)
        {
            Service = new DispatcherState();
            var services = new ServiceCollection();
            services.AddKeyedTransient<IApplicationService>("test",
                (_, _) => new BlockingApplicationService(Service));
            m_Provider = services.BuildServiceProvider();

            var metrics = new Mock<IApplicationMetrics>();
            metrics.Setup(x => x.ObserveCommandDuration(It.IsAny<string>()))
                .Returns(NoopDisposable.Instance);
            Dispatcher = new CommandDispatcher(
                Options.Create(new CommandDispatcherConfig { MaxConcurrency = maxConcurrency }),
                m_Provider,
                metrics.Object,
                NullLogger<CommandDispatcher>.Instance);
        }

        public CommandDispatcher Dispatcher { get; }
        public DispatcherState Service { get; }

        public void Dispose()
        {
            Dispatcher.Dispose();
            m_Provider.Dispose();
        }
    }

    private sealed class DispatcherState
    {
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExecutionCount { get; set; }
        public bool IsDisposed { get; set; }
    }

    private sealed class BlockingApplicationService(DispatcherState state) : IApplicationService, IDisposable
    {
        public async Task<CommandResult> ExecuteAsync(
            IApplicationContext context,
            CancellationToken cancellationToken = default)
        {
            state.ExecutionCount++;
            if (state.ExecutionCount == 1)
            {
                state.FirstStarted.TrySetResult();
                await state.ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            return CommandResult.Success();
        }

        public void Dispose() => state.IsDisposed = true;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
