#region

using System.Globalization;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinCharacterCommandService<TContext> where TContext : IInteractionContext
{
    private readonly GenshinCharacterApiService m_GenshinCharacterApiService;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly GenshinCharacterCardService m_GenshinCharacterCardService;
    private readonly GenshinImageUpdaterService m_GenshinImageUpdaterService;
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly CommandLocalizerService m_CommandLocalizerService;
    private readonly ILogger<GenshinCharacterCommandService<TContext>> m_Logger;

    public TContext Context { get; set; } = default!;

    public GenshinCharacterCommandService(GenshinCharacterApiService genshinCharacterApiService,
        GameRecordApiService gameRecordApiService, GenshinCharacterCardService genshinCharacterCardService,
        GenshinImageUpdaterService genshinImageUpdaterService, UserRepository userRepository,
        TokenCacheService tokenCacheService, CommandLocalizerService commandLocalizerService,
        ILogger<GenshinCharacterCommandService<TContext>> logger)
    {
        m_GenshinCharacterApiService = genshinCharacterApiService;
        m_GameRecordApiService = gameRecordApiService;
        m_GenshinCharacterCardService = genshinCharacterCardService;
        m_GenshinImageUpdaterService = genshinImageUpdaterService;
        m_UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
        m_CommandLocalizerService = commandLocalizerService;
        m_Logger = logger;
    }

    public async Task SendCharacterCardResponseAsync(Regions server, string characterName)
    {
        var userLocale = new CultureInfo(Context.Interaction.UserLocale);
        if (!m_TokenCacheService.TryGetToken(Context.Interaction.User.Id, out var ltoken) ||
            !m_TokenCacheService.TryGetLtUid(Context.Interaction.User.Id, out var ltuid))
        {
            m_Logger.LogInformation("User {UserId} authentication timed out", Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([
                        new TextDisplayProperties(
                            m_CommandLocalizerService.GetLocalizedString("AuthTimeoutErrorMessage", userLocale))
                    ]));
            return;
        }

        var region = GetRegion(server);
        var user = await m_UserRepository.GetUserAsync(Context.Interaction.User.Id);

        if (user?.GameUids == null || !user.GameUids.TryGetValue(GameName.Genshin, out var dict) ||
            !dict.TryGetValue(region, out var gameUid))
        {
            m_Logger.LogDebug("User {UserId} does not have a game UID for region {Region}",
                Context.Interaction.User.Id, region);
            gameUid = await m_GameRecordApiService.GetUserRegionUidAsync(ltuid, ltoken!, "hk4e_global", region);
        }

        if (gameUid == null)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                    new TextDisplayProperties(
                        m_CommandLocalizerService.GetLocalizedString("ServerNoGameInformationErrorMessage", userLocale))
                ]));
            return;
        }

        if (user!.GameUids == null) user.GameUids = new Dictionary<GameName, Dictionary<string, string>>();
        if (!user.GameUids.ContainsKey(GameName.Genshin))
            user.GameUids[GameName.Genshin] = new Dictionary<string, string>();
        if (!user.GameUids[GameName.Genshin].TryAdd(region, gameUid))
            user.GameUids[GameName.Genshin][region] = gameUid;

        m_Logger.LogDebug("Found game UID {GameUid} for User {UserId} in region {Region}", gameUid,
            Context.Interaction.User.Id, region);

        var updateUser = m_UserRepository.CreateOrUpdateUserAsync(user);

        var characters = (await m_GenshinCharacterApiService.GetAllCharactersAsync(ltuid, ltoken!, gameUid, region))
            .ToArray();

        var character =
            characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        if (character == null)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral).WithComponents([
                    new TextDisplayProperties(
                        m_CommandLocalizerService.GetLocalizedString("CharacterNotFoundErrorMessage", userLocale))
                ]));
            return;
        }

        var properties =
            await GenerateCharacterCardResponseAsync((uint)character.Id!.Value, ltuid, ltoken!, gameUid, region);
        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithFlags(MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties(
                m_CommandLocalizerService.GetLocalizedString("CommandExecutionCompletedMessage", userLocale))));
        var followup = Context.Interaction.SendFollowupMessageAsync(properties);

        await Task.WhenAll(followup, updateUser);
    }

    private async Task<InteractionMessageProperties> GenerateCharacterCardResponseAsync(uint characterId, ulong ltuid,
        string ltoken, string gameUid, string region)
    {
        var characterDetail =
            await m_GenshinCharacterApiService.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, region, characterId);

        if (characterDetail == null || characterDetail.List.Count == 0)
        {
            m_Logger.LogError("Failed to retrieve character data {CharacterId} for user {UserId}",
                region, Context.Interaction.User.Id);
            return new InteractionMessageProperties().WithComponents([
                new TextDisplayProperties(
                    m_CommandLocalizerService.GetLocalizedString("CharacterRetrieveUnsuccessfulMessage",
                        new CultureInfo(Context.Interaction.UserLocale)))
            ]);
        }

        var characterInfo = characterDetail.List[0];

        await m_GenshinImageUpdaterService.UpdateDataAsync(characterInfo, characterDetail.AvatarWiki);

        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);
        properties.WithAllowedMentions(new AllowedMentionsProperties().AddAllowedUsers(Context.Interaction.User.Id));
        properties.AddComponents(new TextDisplayProperties($"<@{Context.Interaction.User.Id}>"));
        properties.AddComponents(new MediaGalleryProperties().WithItems(
            [new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://character_card.jpg"))]));
        properties.AddAttachments(new AttachmentProperties("character_card.jpg",
            await m_GenshinCharacterCardService.GenerateCharacterCardAsync(characterInfo, gameUid)));

        return properties;
    }

    private static string GetRegion(Regions server)
    {
        return server switch
        {
            Regions.Asia => "os_asia",
            Regions.Europe => "os_euro",
            Regions.America => "os_usa",
            Regions.Sar => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
