#region

using G_BuddyCore.Models;
using G_BuddyCore.Repositories;
using G_BuddyCore.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

#endregion

namespace G_BuddyCore.Modules;

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    private readonly UserRepository m_UserRespository;

    public AuthModalModule(UserRepository userRespository)
    {
        m_UserRespository = userRespository;
    }

    [ComponentInteraction("authmodal")]
    public async Task TestModal()
    {
        try
        {
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
                responseMessage.Content = "Invalid UID!";
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
                return;
            }

            user.LtUid = ltuid;
            user.LToken = await Task.Run(() =>
                CookieService.EncryptCookie(inputs["ltoken"], inputs["passphrase"]));

            await m_UserRespository.CreateOrUpdateUserAsync(user);

            responseMessage.Content = "Authenticated successfully";
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
        }
        catch
        {
            InteractionMessageProperties responseMessage = new()
            {
                Content = "An error occurred",
                Flags = MessageFlags.Ephemeral
            };
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
        }
    }
}
