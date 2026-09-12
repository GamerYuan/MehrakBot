using System.Diagnostics;
using System.Threading.Channels;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Models;
using Mehrak.Domain.Command.Models;
using Microsoft.Extensions.Options;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Application.Shared.Services;

public class CommandDispatcher : BackgroundService
{
    private readonly Channel<QueuedCommand> m_Channel;
    private readonly IServiceProvider m_ServiceProvider;
    private readonly IApplicationMetrics m_Metrics;
    private readonly ILogger<CommandDispatcher> m_Logger;
    private readonly int m_MaxConcurrency;

    public CommandDispatcher(
        IOptions<CommandDispatcherConfig> config,
        IServiceProvider serviceProvider,
        IApplicationMetrics metrics,
        ILogger<CommandDispatcher> logger)
    {
        if (config.Value.MaxConcurrency <= 0)
            throw new ArgumentException("MaxConcurrency must be greater than zero", nameof(config));

        m_MaxConcurrency = config.Value.MaxConcurrency;
        m_ServiceProvider = serviceProvider;
        m_Metrics = metrics;
        m_Logger = logger;
        m_Channel = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(100)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        }, item => item.CompletionSource
            .TrySetResult(CommandResult.Failure(CommandFailureReason.BotError, "Server under high load")));
    }

    public async Task DispatchAsync(QueuedCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested)
        {
            command.CompletionSource.TrySetCanceled(command.CancellationToken);
            throw new OperationCanceledException(command.CancellationToken);
        }

        try
        {
            await m_Channel.Writer.WriteAsync(command, command.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            command.CompletionSource.TrySetCanceled(command.CancellationToken);
            throw;
        }
        catch (ChannelClosedException)
        {
            command.CompletionSource.TrySetCanceled();
            throw new OperationCanceledException("Command dispatcher is stopping");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, m_MaxConcurrency)
            .Select(_ => RunWorkerAsync(stoppingToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        finally
        {
            m_Channel.Writer.TryComplete();
            while (m_Channel.Reader.TryRead(out var queuedCommand))
                queuedCommand.CompletionSource.TrySetCanceled();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Closing the writer makes new submissions fail immediately. BackgroundService then
        // cancels the worker token and awaits all workers before returning from StopAsync.
        m_Channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);

        while (m_Channel.Reader.TryRead(out var queuedCommand))
            queuedCommand.CompletionSource.TrySetCanceled();
    }

    public override void Dispose()
    {
        m_Channel.Writer.TryComplete();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var queuedCommand in m_Channel.Reader.ReadAllAsync(stoppingToken))
                await ProcessCommandAsync(queuedCommand, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // ExecuteAsync drains commands that were not observed by a worker.
        }
    }

    private async Task ProcessCommandAsync(QueuedCommand command, CancellationToken stoppingToken)
    {
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity(command.Request.CommandName);
        activity?.SetTag("command.name", command.Request.CommandName);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            command.CancellationToken, stoppingToken);
        var cancellationToken = linkedCancellation.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = m_ServiceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            ApplicationContextBase appContext = new(
                command.Request.DiscordUserId,
                command.Request.Parameters.Select(x => (x.Key, x.Value)))
            {
                LtUid = command.Request.LtUid,
                LToken = command.Request.LToken
            };

            var service = scopedProvider.GetKeyedService<IApplicationService>(command.Request.CommandName);

            if (service == null)
            {
                m_Logger.LogWarning("No service registered for command {CommandName}", command.Request.CommandName);
                var failure = CommandResult.Failure(CommandFailureReason.BotError,
                    $"No service registered for command {command.Request.CommandName}");
                command.CompletionSource.TrySetResult(failure);
                return;
            }

            using var time = m_Metrics.ObserveCommandDuration(command.Request.CommandName);
            var result = await service.ExecuteAsync(appContext, cancellationToken);
            command.CompletionSource.TrySetResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var token = command.CancellationToken.IsCancellationRequested
                ? command.CancellationToken
                : cancellationToken;
            command.CompletionSource.TrySetCanceled(token);
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            m_Logger.LogError(e, "An error occurred while dispatching command {CommandName} for user {UserId}",
                command.Request.CommandName, command.Request.DiscordUserId);
            command.CompletionSource.TrySetException(e);
        }
    }
}

public record QueuedCommand(
    Proto.ExecuteRequest Request,
    TaskCompletionSource<CommandResult> CompletionSource,
    CancellationToken CancellationToken
);
