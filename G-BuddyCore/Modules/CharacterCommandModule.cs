#region

using G_BuddyCore.Repositories;
using G_BuddyCore.Services;
using G_BuddyCore.Services.Genshin;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

#endregion

namespace G_BuddyCore.Modules;

public class CharacterCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly ILogger<CharacterCommandModule> m_Logger;
    private readonly CookieService m_CookieService;
    private readonly TokenCacheService m_TokenCacheService;

    public CharacterCommandModule(UserRepository userRepository, ILogger<CharacterCommandModule> logger,
        CookieService cookieService, TokenCacheService tokenCacheService)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_CookieService = cookieService;
        m_TokenCacheService = tokenCacheService;
    }

    [SlashCommand("character", "Get character card")]
    public async Task CharacterCommand()
    {
        try
        {
            m_Logger.LogInformation("User {UserId} used the character command", Context.User.Id);

            if (!m_TokenCacheService.TryGetToken(Context.User.Id, out var _) ||
                !m_TokenCacheService.TryGetLtUid(Context.User.Id, out var _))
            {
                m_Logger.LogInformation("User {UserId} is not authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Modal(AuthModalModule.AuthModal));
            }
            else
            {
                m_Logger.LogInformation("User {UserId} is already authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await Context.Interaction.SendFollowupMessageAsync(
                    CharacterSelectionModule.ServerSelection);
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

    public static InteractionMessageProperties ServerSelection =>
        new InteractionMessageProperties()
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties("Select your server!"))
            .AddComponents(new StringMenuProperties("server_select", Options)
                .WithPlaceholder("Select your server"));

    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterApiService m_GenshinApiService;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly PaginationCacheService m_PaginationCacheService;
    private readonly ILogger<CharacterSelectionModule> m_Logger;

    public CharacterSelectionModule(TokenCacheService tokenCacheService,
        GenshinCharacterApiService genshinApiService,
        GameRecordApiService gameRecordApi,
        PaginationCacheService paginationCacheService,
        ILogger<CharacterSelectionModule> logger)
    {
        m_TokenCacheService = tokenCacheService;
        m_GenshinApiService = genshinApiService;
        m_GameRecordApiService = gameRecordApi;
        m_PaginationCacheService = paginationCacheService;
        m_Logger = logger;
    }

    [ComponentInteraction("server_select")]
    public async Task ServerSelect()
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

        var characters = (await m_GenshinApiService.GetAllCharactersAsync(ltuid, ltoken, gameUid, region)).ToArray();
        m_PaginationCacheService.StoreItems(Context.User.Id, characters, gameUid, region);
        var totalPages = (int)Math.Ceiling((double)characters.Length / 25) - 1;

        await ModifyResponseAsync(m => m.WithFlags(MessageFlags.IsComponentsV2).WithComponents(
            CharacterSelectPagination.CreateComponents(0, totalPages
                , m_PaginationCacheService, Context.User.Id)));
    }

    [ComponentInteraction("character_select")]
    public async Task CharacterSelect()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

        if (!m_TokenCacheService.TryGetToken(Context.User.Id, out var ltoken) ||
            !m_TokenCacheService.TryGetLtUid(Context.User.Id, out var ltuid))
        {
            await ModifyResponseAsync(m => m.WithComponents([
                new TextDisplayProperties("Authentication timed out, please try again.")
            ]));
            return;
        }

        var gameUid = m_PaginationCacheService.GetGameUid(Context.User.Id);
        var region = m_PaginationCacheService.GetRegion(Context.User.Id);

        var fetchTask =
            m_GenshinApiService.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, region,
                uint.Parse(Context.SelectedValues[0]));

        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);
        properties.WithAllowedMentions(new AllowedMentionsProperties().AddAllowedUsers(Context.User.Id));
        properties.AddComponents(new TextDisplayProperties(await fetchTask));

        m_PaginationCacheService.RemoveEntry(Context.User.Id);

        var deleteTask = Context.Interaction.DeleteFollowupMessageAsync(Context.Interaction.Message.Id);
        var followupTask = Context.Interaction.SendFollowupMessageAsync(properties);

        await Task.WhenAll(deleteTask, followupTask);
    }
}

public class CharacterSelectPagination : ComponentInteractionModule<ButtonInteractionContext>
{
    private readonly PaginationCacheService m_PaginationCacheService;
    private readonly ILogger<CharacterSelectPagination> m_Logger;

    public CharacterSelectPagination(PaginationCacheService paginationCacheService,
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
        PaginationCacheService paginationCacheService, ulong userId)
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
