#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Abyss;

public class GenshinAbyssCommandExecutor : BaseCommandExecutor<GenshinCommandModule>
{
    private readonly GenshinImageUpdaterService m_ImageUpdaterService;
    private readonly GenshinAbyssApiService m_ApiService;
    private readonly GenshinAbyssCardService m_CommandService;

    private uint m_PendingFloor = 12;
    private Regions m_PendingServer = Regions.America;

    public GenshinAbyssCommandExecutor(ICommandService<GenshinAbyssCommandExecutor> commandService,
        IApiService<GenshinAbyssCommandExecutor> apiService,
        GenshinImageUpdaterService imageUpdaterService,
        UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<GenshinCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = (GenshinAbyssApiService)apiService;
        m_CommandService = (GenshinAbyssCardService)commandService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException("Invalid number of parameters provided.");

        var floor = (uint)parameters[0]!;
        var server = (Regions?)parameters[1];
        var profile = (uint)(parameters[2] ?? 1);

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

            m_PendingFloor = floor;
            m_PendingServer = server.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await GetAbyssCardAsync(floor, server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Abyss command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync();
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        try
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
            await GetAbyssCardAsync(m_PendingFloor, m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during authentication completion for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync();
        }
    }

    private async ValueTask GetAbyssCardAsync(uint floor, Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var response = await GetAndUpdateGameUidAsync(user, GameName.Genshin, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var gameUid = response.Data;
            var abyssInfo = await m_ApiService.GetAbyssInformationAsync(gameUid, region, ltuid, ltoken);
            if (!abyssInfo.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch Abyss information for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    gameUid, region, abyssInfo.ErrorMessage);
                await SendErrorMessageAsync(abyssInfo.ErrorMessage);
                return;
            }

            var abyssData = abyssInfo.Data;
            var floorData = abyssData.Floors!.FirstOrDefault(x => x.Index == floor);

            if (floorData == null)
            {
                await SendErrorMessageAsync($"No clear record found for floor {floor}. Please try again later.");
                return;
            }

            var tasks = floorData.Levels!.SelectMany(x => x.Battles!.SelectMany(y => y.Avatars!)).DistinctBy(x => x.Id)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateAvatarAsync(x.Id.ToString()!, x.Icon!));

            await Task.WhenAll(tasks);

            var message = await GenerateAbyssCardAsync(floor, gameUid, abyssData);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error generating Abyss card for floor {Floor} and server {Server}",
                floor, server);
            await SendErrorMessageAsync("An error occurred while generating the Abyss card. Please try again later.");
        }
    }

    private async ValueTask<InteractionMessageProperties> GenerateAbyssCardAsync(uint floor, string gameUid,
        GenshinAbyssInformation abyssData)
    {
        InteractionMessageProperties abyssCard = new();
        ComponentContainerProperties container = [];
        abyssCard.AddComponents([container]);

        container.AddComponents(
            new TextDisplayProperties(
                $"### {Context.Interaction.User.Username}'s Abyss Information (Floor {floor})"),
            new TextDisplayProperties(
                $"Cycle start: <t:{abyssData.StartTime}:F>\nCycle end: <t:{abyssData.EndTime}:F>"),
            new MediaGalleryProperties().AddItems(
                new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://abyss_card.jpg"))),
            new TextDisplayProperties(
                $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
        );
        abyssCard.AddAttachments(new AttachmentProperties("abyss_card.jpg",
            await m_CommandService.GetAbyssCardAsync(gameUid, abyssData)));
        return abyssCard;
    }
}
