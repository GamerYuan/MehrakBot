using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Modules;

internal class CustomCommandResultHandler<TContext>(MessageFlags? messageFlags = null) : IApplicationCommandResultHandler<TContext>
    where TContext : IApplicationCommandContext
{
    public async ValueTask HandleResultAsync(IExecutionResult result, TContext context,
        GatewayClient? client, ILogger logger, IServiceProvider services)
    {
        if (result is not IFailResult failResult)
            return;

        var resultMessage = failResult.Message;

        var interaction = context.Interaction;

        if (failResult is IExceptionResult exceptionResult)
            logger.LogError(exceptionResult.Exception, "Execution of an application command of name '{Name}' failed with an exception", interaction.Data.Name);
        else
            logger.LogDebug("Execution of an application command of name '{Name}' failed with '{Message}'", interaction.Data.Name, resultMessage);

        InteractionMessageProperties message = new()
        {
            Content = resultMessage,
            Flags = (messageFlags ?? 0) | MessageFlags.Ephemeral,
        };

        try
        {
            await interaction.SendResponseAsync(InteractionCallback.Message(message)); // Throws error if already responded to
        }
        catch (Exception)
        {
            await interaction.SendFollowupMessageAsync(message);
        }
    }
}
