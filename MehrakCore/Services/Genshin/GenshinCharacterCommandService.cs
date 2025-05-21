#region

using MehrakCore.ApiResponseTypes.Genshin;
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
    private readonly ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> m_GenshinCharacterApiService;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly ICharacterCardService<GenshinCharacterInformation> m_GenshinCharacterCardService;
    private readonly GenshinImageUpdaterService m_GenshinImageUpdaterService;
    private readonly UserRepository m_UserRepository;
    private readonly ILogger<GenshinCharacterCommandService<TContext>> m_Logger;

    public TContext Context { get; set; } = default!;

    public GenshinCharacterCommandService(
        ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> genshinCharacterApiService,
        GameRecordApiService gameRecordApiService,
        ICharacterCardService<GenshinCharacterInformation> genshinCharacterCardService,
        GenshinImageUpdaterService genshinImageUpdaterService, UserRepository userRepository,
        ILogger<GenshinCharacterCommandService<TContext>> logger)
    {
        m_GenshinCharacterApiService = genshinCharacterApiService;
        m_GameRecordApiService = gameRecordApiService;
        m_GenshinCharacterCardService = genshinCharacterCardService;
        m_GenshinImageUpdaterService = genshinImageUpdaterService;
        m_UserRepository = userRepository;
        m_Logger = logger;
    }

    public async Task SendCharacterCardResponseAsync(ulong ltuid, string ltoken, string characterName, Regions server)
    {
        var region = GetRegion(server);
        var user = await m_UserRepository.GetUserAsync(Context.Interaction.User.Id);

        var selectedProfile = user?.Profiles?.FirstOrDefault(x => x.LtUid == ltuid);

        // edge case check that probably will never occur
        // but if user removes their profile while this command is running will result in null
        if (user?.Profiles == null || selectedProfile == null)
        {
            m_Logger.LogDebug("User {UserId} does not have a profile with ltuid {LtUid}",
                Context.Interaction.User.Id, ltuid);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                    new TextDisplayProperties("No profile found. Please select the correct profile")
                ]));
            return;
        }

        if (selectedProfile.GameUids == null ||
            !selectedProfile.GameUids.TryGetValue(GameName.Genshin, out var dict) ||
            !dict.TryGetValue(server.ToString(), out var gameUid))
        {
            m_Logger.LogDebug("User {UserId} does not have a game UID for region {Region}",
                Context.Interaction.User.Id, region);
            var result = await m_GameRecordApiService.GetUserRegionUidAsync(ltuid, ltoken, "hk4e_global", region);
            if (result.RetCode == -100)
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                        new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please authenticate again.")
                    ]));
                return;
            }

            gameUid = result.Data;
        }

        if (gameUid == null)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                    new TextDisplayProperties("No game information found. Please select the correct region")
                ]));
            return;
        }

        selectedProfile.GameUids ??= new Dictionary<GameName, Dictionary<string, string>>();

        if (!selectedProfile.GameUids.ContainsKey(GameName.Genshin))
            selectedProfile.GameUids[GameName.Genshin] = new Dictionary<string, string>();
        if (!selectedProfile.GameUids[GameName.Genshin].TryAdd(server.ToString(), gameUid))
            selectedProfile.GameUids[GameName.Genshin][server.ToString()] = gameUid;

        m_Logger.LogDebug("Found game UID {GameUid} for User {UserId} in region {Region}", gameUid,
            Context.Interaction.User.Id, region);

        selectedProfile.LastUsedRegions ??= new Dictionary<GameName, Regions>();

        if (!selectedProfile.LastUsedRegions.TryAdd(GameName.Genshin, server))
            selectedProfile.LastUsedRegions[GameName.Genshin] = server;

        var updateUser = m_UserRepository.CreateOrUpdateUserAsync(user);

        var characters = (await m_GenshinCharacterApiService.GetAllCharactersAsync(ltuid, ltoken, gameUid, region))
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
            await GenerateCharacterCardResponseAsync((uint)character.Id!.Value, ltuid, ltoken, gameUid, region);
        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithFlags(MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties("Command execution completed")));
        var followup = Context.Interaction.SendFollowupMessageAsync(properties);
        await Task.WhenAll(followup, updateUser);
    }

    private async Task<InteractionMessageProperties> GenerateCharacterCardResponseAsync(uint characterId, ulong ltuid,
        string ltoken, string gameUid, string region)
    {
        var result =
            await m_GenshinCharacterApiService.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, region, characterId);

        if (result.RetCode == 10001)
        {
            m_Logger.LogError("Failed to retrieve character data {CharacterId} for user {UserId}", region,
                Context.Interaction.User.Id);
            return new InteractionMessageProperties().WithComponents([
                new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please authenticate again.")
            ]);
        }

        var characterDetail = result.Data;

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
        properties.AddComponents(
            new ActionRowProperties().AddButtons(new ButtonProperties($"remove_card",
                "Remove",
                ButtonStyle.Danger)));

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
