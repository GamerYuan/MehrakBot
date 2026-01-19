using Grpc.Core;
using Mehrak.Domain.Extensions;
using Mehrak.Domain.Models;
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
            var tcs = new TaskCompletionSource<CommandResult>();

            var queuedItem = new QueuedCommand(request, tcs, context.CancellationToken);

            await dispatcher.DispatchAsync(queuedItem);

            var result = await tcs.Task;

            return result.ToProto();
        }
        catch (TaskCanceledException)
        {
            throw new RpcException(new Status(Grpc.Core.StatusCode.Cancelled, "Request cancelled by client"));
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
