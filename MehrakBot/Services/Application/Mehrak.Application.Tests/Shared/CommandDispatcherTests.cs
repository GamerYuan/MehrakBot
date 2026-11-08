using System.Diagnostics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
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

    [Test]
    public async Task MissingCommand_ReturnsSafeInvalidRequestError()
    {
        using var capture = new CommandActivityCapture("untrusted-request-text");
        using var fixture = new DispatcherFixture(maxConcurrency: 1);
        await fixture.Dispatcher.StartAsync(CancellationToken.None);
        var completion = NewCompletionSource();
        await fixture.Dispatcher.DispatchAsync(new QueuedCommand(
            new Proto.ExecuteRequest { CommandName = "untrusted-request-text" }, completion, CancellationToken.None));
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var span = await capture.Stopped.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(CommandErrorCode.InvalidRequest));
            Assert.That(result.Error.Message, Does.Not.Contain("untrusted-request-text"));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.FailureOutcome));
            Assert.That(span.Tags[ApplicationTelemetry.CommandFailureReasonTag],
                Is.EqualTo(nameof(CommandFailureReason.BotError)));
            Assert.That(span.Tags[ApplicationTelemetry.CommandErrorCodeTag],
                Is.EqualTo(nameof(CommandErrorCode.InvalidRequest)));
        }

        await fixture.Dispatcher.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task TerminalSuccess_SetsOkAndSuccessOutcome()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Success(DailyCheckInOutcome.Empty()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Ok));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.SuccessOutcome));
        }
    }

    [Test]
    public async Task TerminalPartialSuccess_RemainsOk()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Success(CharacterCardsOutcome.Create(Game.Genshin, "900",
                [CharacterCardItem.NotFound("missing")], OperationOutcome.Partial)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Ok));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.SuccessOutcome));
        }
    }

    [Test]
    public async Task TerminalApiError_SetsErrorAndBoundedFailureTags()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Failure(CommandFailureReason.ApiError, CommandErrorCode.ApiUnavailable,
                CommandErrorTarget.CharacterData));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.FailureOutcome));
            Assert.That(span.Tags[ApplicationTelemetry.CommandFailureReasonTag],
                Is.EqualTo(nameof(CommandFailureReason.ApiError)));
            Assert.That(span.Tags[ApplicationTelemetry.CommandErrorCodeTag],
                Is.EqualTo(nameof(CommandErrorCode.ApiUnavailable)));
        }
    }

    [Test]
    public async Task TerminalApiErrorWithExpectedCode_RemainsError()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Failure(CommandFailureReason.ApiError, CommandErrorCode.CharacterNotFound,
                CommandErrorTarget.CharacterData));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.FailureOutcome));
        }
    }

    [Test]
    public async Task TerminalTimeout_SetsError()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags[ApplicationTelemetry.CommandFailureReasonTag],
                Is.EqualTo(nameof(CommandFailureReason.Timeout)));
        }
    }

    [Test]
    public async Task TerminalAuthFailure_IsExpectedAndDoesNotSetError()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Failure(CommandFailureReason.AuthError,
                CommandErrorCode.AuthenticationRequired, CommandErrorTarget.GameProfile));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.ExpectedFailureOutcome));
            Assert.That(span.Tags[ApplicationTelemetry.CommandFailureReasonTag],
                Is.EqualTo(nameof(CommandFailureReason.AuthError)));
            Assert.That(span.Tags[ApplicationTelemetry.CommandErrorCodeTag],
                Is.EqualTo(nameof(CommandErrorCode.AuthenticationRequired)));
        }
    }

    [Test]
    public async Task TerminalInvalidRequest_SetsErrorAndFailureOutcome()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Failure(CommandFailureReason.BotError, CommandErrorCode.InvalidRequest));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.FailureOutcome));
            Assert.That(span.Tags[ApplicationTelemetry.CommandErrorCodeTag],
                Is.EqualTo(nameof(CommandErrorCode.InvalidRequest)));
        }
    }

    [Test]
    public async Task TerminalCancelledResult_IsExpectedAndDoesNotSetError()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Result = CommandResult.Failure(CommandFailureReason.Cancelled, CommandErrorCode.Cancelled));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.ExpectedFailureOutcome));
        }
    }

    [Test]
    public async Task EscapingException_SetsErrorAndFailureOutcome()
    {
        var span = await ExecuteAndCaptureAsync(state =>
            state.Failure = new InvalidOperationException("synthetic failure"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.FailureOutcome));
        }
    }

    [Test]
    public async Task RequestCancellation_SetsCancelledOutcomeWithoutError()
    {
        var span = await ExecuteAndCaptureAsync(static _ => { }, cancel: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
            Assert.That(span.Tags[ApplicationTelemetry.CommandOutcomeTag],
                Is.EqualTo(ApplicationTelemetry.CancelledOutcome));
            Assert.That(span.Tags[ApplicationTelemetry.CommandFailureReasonTag],
                Is.EqualTo(nameof(CommandFailureReason.Cancelled)));
        }
    }

    private static async Task<CommandSpan> ExecuteAndCaptureAsync(Action<DispatcherState> configure,
        bool cancel = false)
    {
        using var capture = new CommandActivityCapture();
        using var fixture = new DispatcherFixture(maxConcurrency: 1, blockFirst: cancel);
        configure(fixture.Service);

        using var cancellation = new CancellationTokenSource();
        await fixture.Dispatcher.StartAsync(CancellationToken.None);
        var completion = NewCompletionSource();
        await fixture.Dispatcher.DispatchAsync(NewCommand(completion, cancellation.Token));

        if (cancel)
        {
            await fixture.Service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }

        await completion.Task.ContinueWith(_ => { }).WaitAsync(TimeSpan.FromSeconds(5));
        var span = await capture.Stopped.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Dispatcher.StopAsync(CancellationToken.None);
        return span;
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

        public DispatcherFixture(int maxConcurrency, bool blockFirst = true)
        {
            Service = new DispatcherState { BlockFirst = blockFirst };
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
        public bool BlockFirst { get; init; } = true;
        public CommandResult? Result { get; set; }
        public Exception? Failure { get; set; }
    }

    private sealed class BlockingApplicationService(DispatcherState state) : IApplicationService, IDisposable
    {
        public async Task<CommandResult> ExecuteAsync(
            IApplicationContext context,
            CancellationToken cancellationToken = default)
        {
            state.ExecutionCount++;
            if (state.BlockFirst && state.ExecutionCount == 1)
            {
                state.FirstStarted.TrySetResult();
                await state.ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            if (state.Failure is not null)
                throw state.Failure;

            return state.Result ?? CommandResult.Success(DailyCheckInOutcome.Empty());
        }

        public void Dispose() => state.IsDisposed = true;
    }

    private sealed class CommandActivityCapture : IDisposable
    {
        private readonly ActivityListener m_Listener;
        private readonly TaskCompletionSource<CommandSpan> m_Stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CommandActivityCapture(string expectedOperationName = "test")
        {
            m_Listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ApplicationTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName != expectedOperationName) return;
                    var tags = activity.TagObjects.ToDictionary(pair => pair.Key, pair => pair.Value);
                    m_Stopped.TrySetResult(new CommandSpan(activity.Status, tags));
                }
            };
            ActivitySource.AddActivityListener(m_Listener);
        }

        public Task<CommandSpan> Stopped => m_Stopped.Task;

        public void Dispose() => m_Listener.Dispose();
    }

    private sealed record CommandSpan(ActivityStatusCode Status, IReadOnlyDictionary<string, object?> Tags);

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
