#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Modules;

public class CharacterCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ILogger<CharacterCommandModule> m_Logger;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly CommandRateLimitService m_RateLimitService;

    public CharacterCommandModule(ILogger<CharacterCommandModule> logger, TokenCacheService tokenCacheService,
        CommandRateLimitService rateLimitService)
    {
        m_Logger = logger;
        m_TokenCacheService = tokenCacheService;
        m_RateLimitService = rateLimitService;
    }

    [SlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character name for the card (Case-insensitive)")]
        string characterName = "")
    {
        try
        {
            m_Logger.LogInformation("User {UserId} used the character command", Context.User.Id);
            if (m_RateLimitService.IsRateLimited(Context.User.Id))
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("Used command too frequent! Please try again later")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            m_RateLimitService.SetRateLimit(Context.User.Id);

            if (!m_TokenCacheService.TryGetToken(Context.User.Id, out _) ||
                !m_TokenCacheService.TryGetLtUid(Context.User.Id, out _))
            {
                m_Logger.LogInformation("User {UserId} is not authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.Modal(AuthModalModule.AuthModal(characterName)));
            }
            else
            {
                m_Logger.LogInformation("User {UserId} is already authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await Context.Interaction.SendFollowupMessageAsync(
                    CharacterSelectionModule.ServerSelection(characterName));
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing character command for user {UserId}", Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
        }
    }
}

public class CharacterSelectionModule : ComponentInteractionModule<StringMenuInteractionContext>
{
    private static readonly StringMenuSelectOptionProperties[] Options =
    [
        new("Asia", "os_asia"),
        new("Europe", "os_euro"),
        new("America", "os_usa"),
        new("TW/HK/MO", "os_cht")
    ];

    public static InteractionMessageProperties ServerSelection(string s)
    {
        return new InteractionMessageProperties()
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties("Select your server!"))
            .AddComponents(new StringMenuProperties($"server_select:{s}", Options)
                .WithPlaceholder("Select your server"));
    }

    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterApiService m_GenshinApiService;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly GenshinCharacterCardService m_GenshinCharacterCardService;
    private readonly PaginationCacheService<GenshinBasicCharacterData> m_PaginationCacheService;
    private readonly GenshinImageUpdaterService m_GenshinImageUpdaterService;
    private readonly ILogger<CharacterSelectionModule> m_Logger;

    public CharacterSelectionModule(TokenCacheService tokenCacheService,
        GenshinCharacterApiService genshinApiService,
        GameRecordApiService gameRecordApi,
        GenshinCharacterCardService genshinCharacterCardService,
        PaginationCacheService<GenshinBasicCharacterData> paginationCacheService,
        GenshinImageUpdaterService genshinImageUpdaterService,
        ILogger<CharacterSelectionModule> logger)
    {
        m_TokenCacheService = tokenCacheService;
        m_GenshinApiService = genshinApiService;
        m_GameRecordApiService = gameRecordApi;
        m_GenshinCharacterCardService = genshinCharacterCardService;
        m_GenshinImageUpdaterService = genshinImageUpdaterService;
        m_PaginationCacheService = paginationCacheService;
        m_Logger = logger;
    }

    [ComponentInteraction("server_select")]
    public async Task ServerSelect(string characterName = "")
    {
        try
        {
            m_Logger.LogDebug("Processing server selection for user {UserId}", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

            if (!m_TokenCacheService.TryGetToken(Context.User.Id, out var ltoken) ||
                !m_TokenCacheService.TryGetLtUid(Context.User.Id, out var ltuid))
            {
                await ModifyResponseAsync(m => m.WithComponents([
                    new TextDisplayProperties("Authentication timed out, please try again.")
                ]));
                return;
            }

            var region = Context.SelectedValues[0];

            var gameUid = await m_GameRecordApiService.GetUserRegionUidAsync(ltuid, ltoken,
                "hk4e_global", region);

            if (gameUid == null)
            {
                await ModifyResponseAsync(m => m.WithComponents([
                    new TextDisplayProperties("No game information found. Please select the correct region")
                ]));
                return;
            }

            var characters = (await m_GenshinApiService.GetAllCharactersAsync(ltuid, ltoken, gameUid, region))
                .ToArray();
            m_PaginationCacheService.StoreItems(Context.User.Id, characters, gameUid, region);
            var totalPages = (int)Math.Ceiling((double)characters.Length / 25) - 1;

            if (characterName != string.Empty)
            {
                var character =
                    characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
                if (character == null)
                {
                    await ModifyResponseAsync(m => m.WithComponents([
                        new TextDisplayProperties("Character not found. Please try again.")
                    ]));
                    return;
                }

                var properties =
                    await GenerateCharacterCardResponseAsync((uint)character.Id!.Value, ltuid, ltoken, gameUid, region);
                var followup = Context.Interaction.SendFollowupMessageAsync(properties);
                var delete = Context.Interaction.DeleteResponseAsync();

                await Task.WhenAll(followup, delete);
            }
            else
            {
                await ModifyResponseAsync(m => m.WithFlags(MessageFlags.IsComponentsV2).WithComponents(
                    CharacterSelectPagination.CreateComponents(0, totalPages
                        , m_PaginationCacheService, Context.User.Id)));
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An exception occurred");
            await ModifyResponseAsync(m => m.WithComponents([
                new TextDisplayProperties("An unknown error occurred, please try again.")
            ]));
        }
    }

    [ComponentInteraction("character_select")]
    public async Task CharacterSelect()
    {
        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

            if (!m_TokenCacheService.TryGetToken(Context.User.Id, out var ltoken) ||
                !m_TokenCacheService.TryGetLtUid(Context.User.Id, out var ltuid))
            {
                var followup = Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithComponents([
                        new TextDisplayProperties("Authentication timed out, please try again.")
                    ]));
                var delete = Context.Interaction.DeleteFollowupMessageAsync(Context.Interaction.Message.Id);

                await Task.WhenAll(followup, delete);
                return;
            }

            var gameUid = m_PaginationCacheService.GetGameUid(Context.User.Id);
            var region = m_PaginationCacheService.GetRegion(Context.User.Id);

            m_PaginationCacheService.RemoveEntry(Context.User.Id);

            var properties = await GenerateCharacterCardResponseAsync(uint.Parse(Context.SelectedValues[0]), ltuid,
                ltoken, gameUid, region);

            var deleteTask = Context.Interaction.DeleteFollowupMessageAsync(Context.Interaction.Message.Id);
            var followupTask = Context.Interaction.SendFollowupMessageAsync(properties);

            await Task.WhenAll(deleteTask, followupTask);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An exception occurred");
            var delete = Context.Interaction.DeleteFollowupMessageAsync(Context.Interaction.Message.Id);
            var followup = Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties("An unknown error occurred, please try again.")
                ]));
            await Task.WhenAll(delete, followup);
        }
    }

    private async Task<InteractionMessageProperties> GenerateCharacterCardResponseAsync(uint characterId, ulong ltuid,
        string ltoken, string gameUid, string region)
    {
        var characterDetail =
            await m_GenshinApiService.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, region, characterId);

        if (characterDetail == null || characterDetail.List.Count == 0)
        {
            m_Logger.LogError("Failed to retrieve character data {CharacterId} for user {UserId}",
                Context.SelectedValues[0], Context.User.Id);
            return new InteractionMessageProperties().WithComponents([
                new TextDisplayProperties("Failed to retrieve character data. Please try again.")
            ]);
        }

        var characterInfo = characterDetail.List[0];

        await m_GenshinImageUpdaterService.UpdateDataAsync(characterInfo, characterDetail.AvatarWiki);

        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);
        properties.WithAllowedMentions(new AllowedMentionsProperties().AddAllowedUsers(Context.User.Id));
        properties.AddComponents(new TextDisplayProperties($"<@{Context.User.Id}>"));
        properties.AddComponents(new MediaGalleryProperties().WithItems(
            [new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://character_card.jpg"))]));
        properties.AddAttachments(new AttachmentProperties("character_card.jpg",
            await m_GenshinCharacterCardService.GenerateCharacterCardAsync(characterInfo, gameUid)));

        return properties;
    }
}

public class CharacterSelectPagination : ComponentInteractionModule<ButtonInteractionContext>

{
    private readonly PaginationCacheService<GenshinBasicCharacterData> m_PaginationCacheService;
    private readonly ILogger<CharacterSelectPagination> m_Logger;

    public CharacterSelectPagination(PaginationCacheService<GenshinBasicCharacterData> paginationCacheService,
        ILogger<CharacterSelectPagination> logger)
    {
        m_PaginationCacheService = paginationCacheService;
        m_Logger = logger;
    }

    [ComponentInteraction("character_select")]
    public InteractionCallback CharacterPagination(int page, int totalPages)
    {
        m_Logger.LogInformation("User {UserId} navigating to character page {Page}/{TotalPages}",
            Context.User.Id, page + 1, totalPages + 1);

        return InteractionCallback.ModifyMessage(m => m
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .WithComponents(CreateComponents(page, totalPages, m_PaginationCacheService, Context.User.Id)));
    }

    public static IComponentProperties[] CreateComponents(int page, int totalPages,
        PaginationCacheService<GenshinBasicCharacterData> paginationCacheService, ulong userId)
    {
        var components = new List<IComponentProperties>
            { new TextDisplayProperties($"Select your character! (Page {page + 1}/{totalPages + 1})") };

        var items = paginationCacheService.GetPageItems(userId, page).ToArray();

        if (items.Length == 0)
        {
            components.Add(new TextDisplayProperties("No characters available. Please try again."));
            return components.ToArray();
        }

        var menuOptions = items.Select(x =>
            new StringMenuSelectOptionProperties(x.Name, x.Id.ToString()!)).ToArray();

        components.Add(new StringMenuProperties($"character_select", menuOptions));

        var actionRow = new ActionRowProperties();

        if (page > 0)
            actionRow.Add(new ButtonProperties($"character_select:{page - 1}:{totalPages}",
                "Previous Page",
                ButtonStyle.Primary));

        if (page < totalPages)
            actionRow.Add(new ButtonProperties($"character_select:{page + 1}:{totalPages}",
                "Next Page",
                ButtonStyle.Primary));

        if (actionRow.Any()) components.Add(actionRow);
        return components.ToArray();
    }
}
