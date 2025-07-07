#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Stygian;

public class GenshinStygianCommandExecutor : BaseCommandExecutor<GenshinCommandModule>
{
    private readonly ImageUpdaterService<GenshinCharacterInformation> m_ImageUpdaterService;
    private readonly ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> m_CharacterApi;
    private readonly GenshinStygianApiService m_ApiService;

    private Regions m_PendingServer;

    public GenshinStygianCommandExecutor(ImageUpdaterService<GenshinCharacterInformation> imageUpdaterService,
        IApiService<GenshinStygianCommandExecutor> apiService,
        ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> characterApi,
        UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<GenshinCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterApi = characterApi;
        m_ApiService = (GenshinStygianApiService)apiService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        var server = (Regions?)parameters[0];
        var profile = (uint)(parameters[1] ?? 1);
        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, GameName.Genshin);
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
                await SendStygianCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
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
            await SendAuthenticationErrorAsync(result.ErrorMessage);
            return;
        }

        Context = result.Context;
        Logger.LogInformation("Authentication completed successfully for user {UserId}",
            Context.Interaction.User.Id);
        await SendStygianCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendStygianCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var region = server.GetRegion();

            var response = await GetAndUpdateGameDataAsync(user, GameName.Genshin, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var userData = response.Data;
            var gameUid = response.Data.GameUid!;

            var stygianInfo = await m_ApiService.GetStygianDataAsync(gameUid, region, ltuid, ltoken);
            if (!stygianInfo.IsSuccess)
            {
                await SendErrorMessageAsync(stygianInfo.ErrorMessage);
                return;
            }

            if (!stygianInfo.Data.IsUnlock)
            {
                Logger.LogWarning("Stygian Onslaught is not unlocked for user {UserId}", Context.Interaction.User.Id);
                await SendErrorMessageAsync("Stygian Onslaught is not unlocked");
                return;
            }

            if (!stygianInfo.Data.Data![0].Single.HasData)
            {
                Logger.LogWarning("No Stygian Onslaught data found for this cycle for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Stygian Onslaught data found for this cycle");
                return;
            }

            var stygianData = stygianInfo.Data.Data[0].Single;

            var avatarTasks = stygianData.Challenge!.SelectMany(x => x.Teams).Select(async x =>
                await m_ImageUpdaterService.UpdateAvatarAsync(x.AvatarId.ToString(), x.Image));
            var sideAvatarTasks = stygianData.Challenge!.SelectMany(x => x.BestAvatar).Select(async x =>
                await m_ImageUpdaterService.UpdateSideAvatarAsync(x.AvatarId.ToString(), x.SideIcon));
            var monsterImages = await stygianData.Challenge!.Select(x => x.Monster).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.MonsterId),
                    async x => await m_ApiService.GetMonsterImageAsync(x.Icon));

            var charList = (await m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region)).ToList();
            if (charList.Count == 0)
            {
                await SendErrorMessageAsync("An error occurred while fetching character data");
                return;
            }

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

            await Task.WhenAll(avatarTasks);
            await Task.WhenAll(sideAvatarTasks);

            var stygianCard = await GetStygianCardAsync(userData, region, ltuid, ltoken, constMap, monsterImages);

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(stygianCard);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin stygian", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Stygian Onslaught card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin stygian", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Stygian Onslaught card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Stygian Onslaught card");
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin stygian", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetStygianCardAsync(UserGameData gameData, string region,
        ulong ltuid, string ltoken, Dictionary<int, int> constMap, Dictionary<int, Stream> monsterImages)
    {
        var message = new InteractionMessageProperties()
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2);

        return message;
    }
}
