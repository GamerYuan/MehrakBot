using Grpc.Core;
using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Extensions;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Application.Services;

public class GrpcApplicationService(
    CommandDispatcher dispatcher,
    ILogger<GrpcApplicationService> logger) : Proto.ApplicationService.ApplicationServiceBase
{
    public override async Task<Proto.CommandResult> ExecuteCommand(Proto.ExecuteRequest request, ServerCallContext context)
    {
        try
        {
            var result = await dispatcher.DispatchAsync(request, context.CancellationToken);
            return result.ToProto();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing command {CommandName}", request.CommandName);
            return new Proto.CommandResult
            {
                IsSuccess = false,
                ErrorMessage = $"Internal Server Error: {ex.Message}",
                FailureReason = Proto.CommandFailureReason.BotError
            };
        }
    }
}

public class CommandDispatcher
{
    private readonly IServiceProvider m_ServiceProvider;

    public CommandDispatcher(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public async Task<Domain.Models.CommandResult> DispatchAsync(Proto.ExecuteRequest request, CancellationToken cancellationToken)
    {
        using var scope = m_ServiceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        ApplicationContextBase appContext = new(request.DiscordUserId, request.Parameters.Select(x => (x.Key, x.Value)))
        {
            LtUid = request.LtUid,
            LToken = request.LToken
        };

        var service = scopedProvider.GetKeyedService<IApplicationService>(request.CommandName);

        if (service == null)
        {
            return Domain.Models.CommandResult.Failure(Domain.Models.CommandFailureReason.BotError,
                $"No service registered for command {request.CommandName}");
        }

        return await service.ExecuteAsync(appContext);
    }
}
