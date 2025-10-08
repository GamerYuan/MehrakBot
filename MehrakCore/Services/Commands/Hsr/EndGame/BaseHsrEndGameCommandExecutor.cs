#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Hsr.EndGame;

public abstract class BaseHsrEndGameCommandExecutor : BaseCommandExecutor<HsrCommandModule>
{
    private readonly HsrEndGameCardService m_CommandService;

    protected abstract string GameModeName { get; }
    protected abstract string AttachmentName { get; }

    protected BaseHsrEndGameCommandExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<HsrCommandModule> logger, ICommandService<BaseHsrEndGameCommandExecutor> commandService) : base(
        userRepository,
        tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_CommandService = (HsrEndGameCardService)commandService;
    }

    private protected async ValueTask<InteractionMessageProperties> GetEndGameCardAsync(EndGameMode gameMode,
        UserGameData gameData, HsrEndInformation gameModeData, Regions region, Dictionary<int, Stream> buffMap)
    {
        try
        {
            var tz = region.GetTimeZoneInfo();
            var group = gameModeData.Groups.First();
            var startTime = new DateTimeOffset(group.BeginTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(group.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            InteractionMessageProperties message = new();
            ComponentContainerProperties container =
            [
                new TextDisplayProperties(
                    $"### <@{Context.Interaction.User.Id}>'s {GameModeName} Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{AttachmentName}.jpg"))),
                new TextDisplayProperties(
                    $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];

            message.WithComponents([container]);
            message.WithFlags(MessageFlags.IsComponentsV2);
            message.AddAttachments(new AttachmentProperties($"{AttachmentName}.jpg",
                await m_CommandService.GetEndGameCardImageAsync(gameMode, gameData, gameModeData, buffMap)));

            return message;
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error generating {GameModeName} card for user {UserId}",
                GameModeName, Context.Interaction.User.Id);
            throw new CommandException($"An error occurred while generating {GameModeName} card", e);
        }
    }
}

internal enum EndGameMode
{
    PureFiction,
    ApocalypticShadow
}

internal static class EndGameModeExtensions
{
    public static string GetString(this EndGameMode mode)
    {
        return mode switch
        {
            EndGameMode.PureFiction => "Pure Fiction",
            EndGameMode.ApocalypticShadow => "Apocalyptic Shadow",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
