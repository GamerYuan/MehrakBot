#region

using Mehrak.Domain.Interfaces;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Theater;

public class GenshinTheaterCommandExecutor : BaseCommandExecutor<GenshinCommandModule>
{
    private readonly GenshinTheaterCardService m_CommandService;
    private readonly GenshinTheaterApiService m_ApiService;
    private readonly GenshinImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> m_CharacterApi;
    private Regions m_PendingServer;

    public GenshinTheaterCommandExecutor(ICommandService<GenshinTheaterCommandExecutor> commandService,
        IApiService<GenshinTheaterCommandExecutor> apiService, GenshinImageUpdaterService imageUpdaterService,
        ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> characterApi,
        UserRepository userRepository, RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<GenshinCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_CommandService = (GenshinTheaterCardService)commandService;
        m_ApiService = (GenshinTheaterApiService)apiService;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterApi = characterApi;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        var server = (Regions?)parameters[0];
        var profile = (uint)(parameters[1] ?? 1);
        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, Game.Genshin);
            if (server == null)
            {
                await SendErrorMessageAsync("No cached server found! Please select a server first.");
                return;
            }

            m_PendingServer = server.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await GetTheaterCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Command execution failed for region: {Region}, profile: {Profile}", server, profile);
            await SendErrorMessageAsync(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e,
                "An unexpected error occurred while executing command for region: {Region}, profile: {Profile}",
                server, profile);
            await SendErrorMessageAsync();
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (!result.IsSuccess)
        {
            Logger.LogError("Authentication failed for user {UserId}: {ErrorMessage}", Context.Interaction.User.Id,
                result.ErrorMessage);
            return;
        }

        Context = result.Context;
        Logger.LogInformation("Authentication completed successfully for user {UserId}",
            Context.Interaction.User.Id);
        await GetTheaterCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask GetTheaterCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var region = server.GetRegion();

            var response = await GetAndUpdateGameDataAsync(user, Game.Genshin, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var userData = response.Data;
            var gameUid = response.Data.GameUid!;

            var theaterDataResult = await m_ApiService.GetTheaterDataAsync(gameUid, region, ltuid, ltoken);
            if (!theaterDataResult.IsSuccess)
            {
                await SendErrorMessageAsync(theaterDataResult.ErrorMessage);
                return;
            }

            var theaterData = theaterDataResult.Data;

            var updateImageTask = theaterData.Detail.RoundsData.SelectMany(x => x.Avatars).DistinctBy(x => x.AvatarId)
                .Select(async x => await m_ImageUpdaterService.UpdateAvatarAsync(x.AvatarId.ToString(), x.Image));
            var sideAvatarTask =
                ((ItRankAvatar[])
                [
                    theaterData.Detail.FightStatistic.MaxDamageAvatar,
                    theaterData.Detail.FightStatistic.MaxDefeatAvatar,
                    theaterData.Detail.FightStatistic.MaxTakeDamageAvatar
                ]).DistinctBy(x => x.AvatarId)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateSideAvatarAsync(x.AvatarId.ToString(), x.AvatarIcon!));

            var charList = (await m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region)).ToList();
            if (charList.Count == 0)
            {
                await SendErrorMessageAsync("An error occurred while fetching character data");
                return;
            }

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);
            var buffMap = await m_ApiService.GetBuffIconsAsync(theaterData.Detail.RoundsData.First().SplendourBuff!);
            if (!buffMap.IsSuccess)
            {
                await SendErrorMessageAsync(buffMap.ErrorMessage);
                return;
            }

            await Task.WhenAll(updateImageTask);
            await Task.WhenAll(sideAvatarTask);

            var theaterCard = await GetTheaterCardAsync(theaterData, userData, constMap, buffMap.Data);

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(theaterCard);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin theater", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Imaginarium Theater card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin theater", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Imaginarium Theater card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Imaginarium Theater card");
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin theater", false);
        }
    }

    private async Task<InteractionMessageProperties> GetTheaterCardAsync(GenshinTheaterInformation theaterData,
        UserGameData userData, Dictionary<int, int> constMap, Dictionary<string, Stream> buffMap)
    {
        try
        {
            InteractionMessageProperties response = new();
            response.WithFlags(MessageFlags.IsComponentsV2);
            ComponentContainerProperties container =
            [
                new TextDisplayProperties($"### <@{Context.Interaction.User.Id}>'s Imaginarium Theater Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{theaterData.Schedule.StartTime}:f>\nCycle end: <t:{theaterData.Schedule.EndTime}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://theater_card.jpg"))),
                new TextDisplayProperties(
                    "-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];

            response.AddAttachments(new AttachmentProperties("theater_card.jpg",
                await m_CommandService.GetTheaterCardAsync(theaterData, userData, constMap, buffMap)));

            response.AddComponents([container]);
            response.AddComponents(
                new ActionRowProperties().AddButtons(new ButtonProperties("remove_card",
                    "Remove", ButtonStyle.Danger)));
            return response;
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to generate Imaginarium Theater card for user {UserId}",
                Context.Interaction.User.Id);
            throw new CommandException("An error occurred while generating Imaginarium Theater card", e);
        }
    }
}
