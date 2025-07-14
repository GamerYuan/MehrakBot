#region

using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

[SlashCommand("zzz", "Zenless Zone Zero Toolbox")]
public class ZzzCommandModule : ApplicationCommandModule<ApplicationCommandContext>, ICommandModule
{
    private readonly ICodeRedeemExecutor<ZzzCommandModule> m_CodeRedeemExecutor;
    private readonly CommandRateLimitService m_CommandRateLimitService;
    private readonly ILogger<ZzzCommandModule> m_Logger;

    public ZzzCommandModule(ICodeRedeemExecutor<ZzzCommandModule> codeRedeemExecutor,
        CommandRateLimitService commandRateLimitService, ILogger<ZzzCommandModule> logger)
    {
        m_CodeRedeemExecutor = codeRedeemExecutor;
        m_CommandRateLimitService = commandRateLimitService;
        m_Logger = logger;
    }

    [SubSlashCommand("codes", "Redeem Zenless Zone Zero codes")]
    public async Task CodesCommand(
        [SlashCommandParameter(Name = "code", Description = "Redemption Codes (Comma-separated, Case-insensitive)")]
        string code = "",
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the codes command with code {Code}, server {Server}, profile {ProfileId}",
            Context.User.Id, code, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_CodeRedeemExecutor.Context = Context;
        await m_CodeRedeemExecutor.ExecuteAsync(code, server, profile).ConfigureAwait(false);
    }

    private async Task<bool> ValidateRateLimitAsync()
    {
        if (await m_CommandRateLimitService.IsRateLimitedAsync(Context.Interaction.User.Id))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Used command too frequent! Please try again later")
                    .WithFlags(MessageFlags.Ephemeral)));
            return false;
        }

        await m_CommandRateLimitService.SetRateLimitAsync(Context.Interaction.User.Id);
        return true;
    }
}
