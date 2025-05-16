#region

using System.Globalization;
using System.Security.Cryptography;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Services.Genshin;

public class AuthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly CommandLocalizerService m_LocalizerService;
    private readonly ILogger<AuthCommandModule> m_Logger;

    public AuthCommandModule(UserRepository userRepository, TokenCacheService tokenCacheService,
        CommandLocalizerService localizerService,
        ILogger<AuthCommandModule> logger)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_TokenCacheService = tokenCacheService;
        m_LocalizerService = localizerService;
    }

    [UserCommand("Authenticate HoYoLAB Profile")]
    public async Task AuthCommand()
    {
        m_Logger.LogInformation("User {UserId} is authenticating HoYoLAB profile", Context.User.Id);
        var userLocale = new CultureInfo(Context.Interaction.UserLocale);
        await RespondAsync(
            InteractionCallback.Modal(
                AuthModalModule.AddAuthModal(m_LocalizerService.GetLocalizedString("AuthModalTitle", userLocale),
                    m_LocalizerService.GetLocalizedString("PassphraseLabel", userLocale),
                    m_LocalizerService.GetLocalizedString("AddAuthPassphrasePlaceholder", userLocale))));
    }

    [UserCommand("Delete Profile")]
    public async Task DeleteProfileCommand()
    {
        m_Logger.LogInformation("User {UserId} requested profile deletion", Context.User.Id);

        var result = await m_UserRepository.DeleteUserAsync(Context.User.Id);

        m_TokenCacheService.RemoveEntry(Context.User.Id);

        m_Logger.LogInformation("Profile deletion for user {UserId}: {Result}",
            Context.User.Id, result ? "Success" : "Not Found");

        InteractionMessageProperties responseMessage = new()
        {
            Content = result ? "Profile deleted!" : "No profile found!",
            Flags = MessageFlags.Ephemeral
        };

        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
    }
}

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    public static ModalProperties AddAuthModal(string authTitle, string passLabel, string passPlaceholder)
    {
        return new ModalProperties("add_auth_modal", authTitle)
            .WithComponents([
                new TextInputProperties("ltuid", TextInputStyle.Short, "HoYoLAB UID"),
                new TextInputProperties("ltoken", TextInputStyle.Paragraph, "HoYoLAB Cookies"),
                new TextInputProperties("passphrase", TextInputStyle.Paragraph, passLabel)
                    .WithPlaceholder(passPlaceholder)
                    .WithMaxLength(64)
            ]);
    }

    public static ModalProperties AuthModal(Regions server, string character, string authTitle, string passLabel)
    {
        return new ModalProperties($"character_auth_modal:{server}:{character}", authTitle)
            .AddComponents([
                new TextInputProperties("passphrase", TextInputStyle.Paragraph, passLabel).WithMaxLength(64)
            ]);
    }

    private readonly UserRepository m_UserRespository;
    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly CookieService m_CookieService;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterCommandService<ModalInteractionContext> m_Service;
    private readonly CommandLocalizerService m_LocalizerService;

    public AuthModalModule(UserRepository userRespository, ILogger<AuthModalModule> logger, CookieService cookieService,
        TokenCacheService tokenCacheService, GenshinCharacterCommandService<ModalInteractionContext> service,
        CommandLocalizerService localizerService)
    {
        m_UserRespository = userRespository;
        m_Logger = logger;
        m_CookieService = cookieService;
        m_TokenCacheService = tokenCacheService;
        m_Service = service;
        m_LocalizerService = localizerService;
    }

    [ComponentInteraction("add_auth_modal")]
    public async Task AddAuth()
    {
        var userLocale = new CultureInfo(Context.Interaction.UserLocale);
        try
        {
            m_Logger.LogInformation("Processing auth modal submission from user {UserId}", Context.User.Id);
            var user = await m_UserRespository.GetUserAsync(Context.User.Id);
            user ??= new UserModel
            {
                Id = Context.User.Id
            };

            var inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            if (!ulong.TryParse(inputs["ltuid"], out var ltuid))
            {
                m_Logger.LogWarning("User {UserId} provided invalid UID format", Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties()
                        .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .WithComponents([
                            new TextDisplayProperties(
                                m_LocalizerService.GetLocalizedString("AddAuthModalInvalidUIDMessage", userLocale))
                        ])));
                return;
            }

            user.LtUid = ltuid;
            m_Logger.LogDebug("Encrypting cookie for user {UserId}", Context.User.Id);
            user.LToken = await Task.Run(() =>
                m_CookieService.EncryptCookie(inputs["ltoken"], inputs["passphrase"]));
            user.GameUids = new Dictionary<GameName, Dictionary<string, string>>();

            await m_UserRespository.CreateOrUpdateUserAsync(user);
            m_Logger.LogInformation("User {UserId} successfully authenticated", Context.User.Id);

            m_TokenCacheService.RemoveEntry(Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        m_LocalizerService.GetLocalizedString("AddAuthModalSuccessfulMessage", userLocale))
                ])));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .WithComponents([
                        new TextDisplayProperties(
                            m_LocalizerService.GetLocalizedString("UnknownErrorMessage", userLocale))
                    ])));
        }
    }

    [ComponentInteraction("character_auth_modal")]
    public async Task CharacterAuth(
        [ComponentInteractionParameter(TypeReaderType = typeof(RegionsEnumTypeReader<ModalInteractionContext>))]
        Regions server,
        string characterName)
    {
        if (await AuthUser())
        {
            m_Logger.LogInformation("User {UserId} successfully authenticated", Context.User.Id);
            m_Service.Context = Context;
            await m_Service.SendCharacterCardResponseAsync(server, characterName);
        }
    }

    private async Task<bool> AuthUser()
    {
        var userLocale = new CultureInfo(Context.Interaction.UserLocale);
        try
        {
            m_Logger.LogInformation("Processing auth modal submission from user {UserId}", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var user = await m_UserRespository.GetUserAsync(Context.User.Id);
            if (user == null)
            {
                m_Logger.LogWarning("User {UserId} not found in database", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .WithComponents([
                        new TextDisplayProperties(
                            m_LocalizerService.GetLocalizedString("AuthUserNoProfileMessage", userLocale))
                    ]));
                return false;
            }

            var inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            var ltoken = m_CookieService.DecryptCookie(user.LToken, inputs["passphrase"]);

            m_TokenCacheService.AddCacheEntry(Context.User.Id, user.LtUid, ltoken);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            m_Logger.LogWarning("User {UserId} provided wrong passphrase", Context.User.Id);
            if (Context.Interaction.Message?.InteractionMetadata?.OriginalResponseMessageId != null)
                await Context.Interaction.DeleteFollowupMessageAsync(
                    Context.Interaction.Message.InteractionMetadata
                        .OriginalResponseMessageId!.Value);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        m_LocalizerService.GetLocalizedString("AuthUserInvalidPassphraseMessage", userLocale))
                ]));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .WithComponents([
                        new TextDisplayProperties(
                            m_LocalizerService.GetLocalizedString("UnknownErrorMessage", userLocale))
                    ]));
        }

        return false;
    }
}
