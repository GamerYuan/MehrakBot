#region

using G_BuddyCore.Models;
using G_BuddyCore.Repositories;
using G_BuddyCore.Services;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

#endregion

namespace G_BuddyCore.Modules;

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    private readonly UserRepository m_UserRespository;
    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly CookieService m_CookieService;

    public AuthModalModule(UserRepository userRespository, ILogger<AuthModalModule> logger, CookieService cookieService)
    {
        m_UserRespository = userRespository;
        m_Logger = logger;
        m_CookieService = cookieService;
    }

    [ComponentInteraction("authmodal")]
    public async Task TestModal()
    {
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

            InteractionMessageProperties responseMessage = new()
            {
                Flags = MessageFlags.Ephemeral
            };

            if (!ulong.TryParse(inputs["ltuid"], out var ltuid))
            {
                m_Logger.LogWarning("User {UserId} provided invalid UID format", Context.User.Id);
                responseMessage.Content = "Invalid UID!";
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
                return;
            }

            user.LtUid = ltuid;
            m_Logger.LogDebug("Encrypting cookie for user {UserId}", Context.User.Id);
            user.LToken = await Task.Run(() =>
                m_CookieService.EncryptCookie(inputs["ltoken"], inputs["passphrase"]));

            await m_UserRespository.CreateOrUpdateUserAsync(user);
            m_Logger.LogInformation("User {UserId} successfully authenticated", Context.User.Id);

            responseMessage.Content = "Authenticated successfully";
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error processing auth modal for user {UserId}", Context.User.Id);

            InteractionMessageProperties responseMessage = new()
            {
                Content = "An error occurred",
                Flags = MessageFlags.Ephemeral
            };
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
        }
    }
}
