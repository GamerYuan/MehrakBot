using Grpc.Core;
using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Application.Services.Genshin.RealTimeNotes;
using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Application.Services.Hsr.Anomaly;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Application.Services.Hsr.RealTimeNotes;
using Mehrak.Application.Services.Zzz.Assault;
using Mehrak.Application.Services.Zzz.Character;
using Mehrak.Application.Services.Zzz.Defense;
using Mehrak.Application.Services.Zzz.RealTimeNotes;
using Mehrak.Domain.Common;
using Mehrak.Domain.Extensions;
using Mehrak.Domain.Services.Abstractions;
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
    private readonly Dictionary<string, Func<Proto.ExecuteRequest, CancellationToken, Task<Domain.Models.CommandResult>>> m_HandlerMap;

    public CommandDispatcher(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
        m_HandlerMap = new(StringComparer.OrdinalIgnoreCase);

        Register<CheckInApplicationContext>(CommandName.Common.CheckIn);

        Register<GenshinAbyssApplicationContext>(CommandName.Genshin.Abyss);
        Register<GenshinCharacterApplicationContext>(CommandName.Genshin.Character);
        Register<GenshinCharListApplicationContext>(CommandName.Genshin.CharList);
        Register<CodeRedeemApplicationContext>(CommandName.Genshin.Codes);
        Register<GenshinRealTimeNotesApplicationContext>(CommandName.Genshin.RealTimeNotes);
        Register<GenshinStygianApplicationContext>(CommandName.Genshin.Stygian);
        Register<GenshinTheaterApplicationContext>(CommandName.Genshin.Theater);

        Register<HsrAnomalyApplicationContext>(CommandName.Hsr.Anomaly);
        Register<HsrEndGameApplicationContext>(CommandName.Hsr.ApocalypticShadow);
        Register<HsrCharacterApplicationContext>(CommandName.Hsr.Character);
        Register<HsrCharListApplicationContext>(CommandName.Hsr.CharList);
        Register<HsrMemoryApplicationContext>(CommandName.Hsr.Memory);
        Register<HsrRealTimeNotesApplicationContext>(CommandName.Hsr.RealTimeNotes);
        Register<HsrEndGameApplicationContext>(CommandName.Hsr.PureFiction);

        Register<ZzzCharacterApplicationContext>(CommandName.Zzz.Character);
        Register<ZzzAssaultApplicationContext>(CommandName.Zzz.Assault);
        Register<ZzzDefenseApplicationContext>(CommandName.Zzz.Defense);
        Register<ZzzRealTimeNotesApplicationContext>(CommandName.Zzz.RealTimeNotes);

        Register<Hi3CharacterApplicationContext>(CommandName.Hi3.Character);
    }

    public async Task<Domain.Models.CommandResult> DispatchAsync(Proto.ExecuteRequest request, CancellationToken cancellationToken)
    {
        if (!m_HandlerMap.TryGetValue(request.CommandName, out var handler))
        {
            return Domain.Models.CommandResult.Failure(Domain.Models.CommandFailureReason.BotError,
                $"Unknown command: {request.CommandName}");
        }

        return await handler(request, cancellationToken);
    }

    private void Register<TContext>(string commandName)
        where TContext : class, IApplicationContext
    {
        m_HandlerMap[commandName] = (request, cancellationToken) => ExecuteAsync<TContext>(request);
    }

    private async Task<Domain.Models.CommandResult> ExecuteAsync<TContext>(Proto.ExecuteRequest request)
        where TContext : class, IApplicationContext
    {
        using var scope = m_ServiceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        TContext appContext;
        try
        {
            appContext = ActivatorUtilities.CreateInstance<TContext>(scopedProvider, request.DiscordUserId,
                request.Parameters.Select(x => (x.Key, x.Value)));
        }
        catch
        {
            return Domain.Models.CommandResult.Failure(Domain.Models.CommandFailureReason.BotError,
                $"Could not instantiate context {typeof(TContext).Name}");
        }

        appContext.LtUid = request.LtUid;
        appContext.LToken = request.LToken;

        var service = scopedProvider.GetService<IApplicationService<TContext>>();

        if (service == null)
        {
            return Domain.Models.CommandResult.Failure(Domain.Models.CommandFailureReason.BotError,
                $"No service registered for context {typeof(TContext).Name}");
        }

        return await service.ExecuteAsync(appContext);
    }
}
