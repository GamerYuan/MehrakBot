#region

using MehrakCore.Models;
using MehrakCore.Repositories;
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
    private readonly ILogger<GenshinCharacterCommandService<TContext>> m_Logger;

    public TContext Context { get; set; } = default!;

    public GenshinCharacterCommandService(GenshinCharacterApiService genshinCharacterApiService,
        GameRecordApiService gameRecordApiService, GenshinCharacterCardService genshinCharacterCardService,
        GenshinImageUpdaterService genshinImageUpdaterService, UserRepository userRepository,
        TokenCacheService tokenCacheService,
        ILogger<GenshinCharacterCommandService<TContext>> logger)
    {
        m_GenshinCharacterApiService = genshinCharacterApiService;
        m_GameRecordApiService = gameRecordApiService;
        m_GenshinCharacterCardService = genshinCharacterCardService;
        m_GenshinImageUpdaterService = genshinImageUpdaterService;
        m_UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
        m_Logger = logger;
    }

    public async Task SendCharacterCardResponseAsync(string server, string characterName)
    {
        if (!m_TokenCacheService.TryGetToken(Context.Interaction.User.Id, out var ltoken) ||
            !m_TokenCacheService.TryGetLtUid(Context.Interaction.User.Id, out var ltuid))
        {
            m_Logger.LogInformation("User {UserId} authentication timed out", Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([
                        new TextDisplayProperties("Authentication timed out, please try again.")
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
                    new TextDisplayProperties("No game information found. Please select the correct region")
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
                    new TextDisplayProperties("Character not found. Please try again.")
                ]));
            return;
        }

        var properties =
            await GenerateCharacterCardResponseAsync((uint)character.Id!.Value, ltuid, ltoken!, gameUid, region);
        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithFlags(MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties("Command execution completed")));
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
                new TextDisplayProperties("Failed to retrieve character data. Please try again.")
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

    private static string GetRegion(string server)
    {
        return server switch
        {
            _ when server.Equals("Asia", StringComparison.OrdinalIgnoreCase) => "os_asia",
            _ when server.Equals("Europe", StringComparison.OrdinalIgnoreCase) => "os_euro",
            _ when server.Equals("America", StringComparison.OrdinalIgnoreCase) => "os_usa",
            _ when server.Equals("SAR", StringComparison.OrdinalIgnoreCase) => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
