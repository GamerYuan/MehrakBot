using System.Threading.Channels;
using Mehrak.Application.Models;
using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Models;
using Microsoft.Extensions.Options;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Application.Services;

public class CommandDispatcher : BackgroundService
{
    private readonly Channel<QueuedCommand> m_Channel;

    private readonly IServiceProvider m_ServiceProvider;
    private readonly IApplicationMetrics m_Metrics;
    private readonly ILogger<CommandDispatcher> m_Logger;

    private readonly SemaphoreSlim m_Semaphore;

    public CommandDispatcher(IOptions<CommandDispatcherConfig> config, IServiceProvider serviceProvider,
        IApplicationMetrics metrics, ILogger<CommandDispatcher> logger)
    {
        if (config.Value.MaxConcurrency <= 0)
            throw new ArgumentException("MaxConcurrency must be greater than zero", nameof(config));
        m_Semaphore = new(config.Value.MaxConcurrency);

        m_ServiceProvider = serviceProvider;
        m_Metrics = metrics;
        m_Logger = logger;
        m_Channel = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(100)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        }, item => item.CompletionSource
            .TrySetResult(CommandResult.Failure(CommandFailureReason.BotError, "Server under high load")));
    }

    public async Task DispatchAsync(QueuedCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested) throw new TaskCanceledException();

        await m_Channel.Writer.WriteAsync(command, command.CancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var queuedCommand in m_Channel.Reader.ReadAllAsync(stoppingToken))
        {
            await m_Semaphore.WaitAsync(stoppingToken);

            _ = ProcessCommandAsync(queuedCommand).ContinueWith(t =>
            {
                m_Semaphore.Release();
            }, TaskContinuationOptions.None);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        m_Semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ProcessCommandAsync(QueuedCommand command)
    {
        try
        {
            if (command.CancellationToken.IsCancellationRequested)
            {
                command.CompletionSource.TrySetCanceled(command.CancellationToken);
                return;
            }

            using var scope = m_ServiceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            ApplicationContextBase appContext = new(command.Request.DiscordUserId, command.Request.Parameters.Select(x => (x.Key, x.Value)))
            {
                LtUid = command.Request.LtUid,
                LToken = command.Request.LToken
            };

            var service = scopedProvider.GetKeyedService<IApplicationService>(command.Request.CommandName);

            if (service == null)
            {
                m_Logger.LogWarning("No service registered for command {CommandName}", command.Request.CommandName);
                var failure = Domain.Models.CommandResult.Failure(Domain.Models.CommandFailureReason.BotError,
                    $"No service registered for command {command.Request.CommandName}");
                command.CompletionSource.TrySetResult(failure);
                return;
            }

            using var time = m_Metrics.ObserveCommandDuration(command.Request.CommandName);
            var result = await service.ExecuteAsync(appContext);
            command.CompletionSource.TrySetResult(result);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while dispatching command {CommandName} for user {UserId}",
                command.Request.CommandName, command.Request.DiscordUserId);
            command.CompletionSource.TrySetException(e);
        }
    }

}

public record QueuedCommand(
    Proto.ExecuteRequest Request,
    TaskCompletionSource<Domain.Models.CommandResult> CompletionSource,
    CancellationToken CancellationToken
);
