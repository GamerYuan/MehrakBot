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
                await Context.Interaction.SendResponseAsync(InteractionCallback.Modal(AuthModalModule.AuthModal));
            else
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.Message(CharacterServerSelectionModule.ServerSelection));
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

public class CharacterServerSelectionModule : ComponentInteractionModule<StringMenuInteractionContext>
{
    private static readonly StringMenuSelectOptionProperties[] Options =
    [
        new("Asia", "os_asia"),
        new("Europe", "os_euro"),
        new("America", "os_usa"),
        new("TW/HK/MO", "os_cht")
    ];

    public static InteractionMessageProperties ServerSelection =>
        new InteractionMessageProperties().WithFlags(MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties("Select your server!"))
            .AddComponents(new StringMenuProperties("server_select", Options)
                .WithPlaceholder("Select your server"));

    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterApiService m_GenshinApiService;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly ILogger<CharacterServerSelectionModule> m_Logger;

    public CharacterServerSelectionModule(TokenCacheService tokenCacheService,
        GenshinCharacterApiService genshinApiService,
        GameRecordApiService gameRecordApi,
        ILogger<CharacterServerSelectionModule> logger)
    {
        m_TokenCacheService = tokenCacheService;
        m_GenshinApiService = genshinApiService;
        m_GameRecordApiService = gameRecordApi;
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

        var characters = await m_GenshinApiService.GetAllCharactersAsync(ltuid, ltoken, gameUid, region);
        var deleteTask = Context.Interaction.DeleteFollowupMessageAsync(Context.Interaction.Message.Id);
        var followuptask = Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithFlags(MessageFlags.IsComponentsV2)
            .WithComponents([new TextDisplayProperties($"Characters: {characters}")]));

        await Task.WhenAll(deleteTask, followuptask);
    }
}
