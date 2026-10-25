using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Shared.Modules;

internal class CustomCommandResultHandler<TContext>(MessageFlags? messageFlags = null) : IApplicationCommandResultHandler<TContext>
    where TContext : IApplicationCommandContext
{
    public async ValueTask HandleResultAsync(IExecutionResult result, TContext context,
        GatewayClient? client, ILogger logger, IServiceProvider services)
    {
        if (result is not IFailResult failResult)
            return;

        var interaction = context.Interaction;
        var localizer = services.GetService<ICommandUiLocalizer>() ?? CommandUiLocalizer.Default;
        var culture = BotUiCulture.Resolve(interaction.UserLocale);

        string resultMessage;

        if (failResult is IExceptionResult exceptionResult)
        {
            // Framework exception text is never surfaced to users: only a safe translated category.
            logger.LogError(exceptionResult.Exception,
                "Execution of an application command of name '{Name}' failed with an exception", interaction.Data.Name);
            resultMessage = localizer.Get(culture, CommandUiKeys.FrameworkUnexpectedError);
        }
        else
        {
            logger.LogDebug("Execution of an application command of name '{Name}' failed with '{Message}'",
                interaction.Data.Name, failResult.Message);

            // Bot-authored precondition messages (rate limits) are already localised and safe;
            // raw framework parse/validation text is replaced by a safe translated category.
            var isSafeRateLimit = failResult is PreconditionFailResult &&
                                  (failResult.Message == localizer.Get(culture, CommandUiKeys.RateLimitTooFrequent) ||
                                   failResult.Message == localizer.Get(culture, CommandUiKeys.RateLimitUnavailable));
            resultMessage = isSafeRateLimit ? failResult.Message :
                localizer.Get(culture, CommandUiKeys.FrameworkInputInvalid);
        }

        InteractionMessageProperties message = new()
        {
            Content = resultMessage,
            Flags = (messageFlags ?? 0) | MessageFlags.Ephemeral,
        };

        try
        {
            await interaction.SendResponseAsync(InteractionCallback.Message(message)); // Throws error if already responded to
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Unable to send interaction response");
            await interaction.SendFollowupMessageAsync(message);
        }
    }
}
