#region

using G_BuddyCore.Repositories;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace G_BuddyCore.Modules;

public class AuthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;

    public AuthCommandModule(UserRepository userRepository)
    {
        m_UserRepository = userRepository;
    }

    [UserCommand("Authenticate HoYoLAB Profile")]
    public async Task AuthCommand()
    {
        ModalProperties properties = new("authmodal", "Authenticate")
        {
            new TextInputProperties("ltuid", TextInputStyle.Short, "HoYoLAB UID")
            {
                Required = true
            },
            new TextInputProperties("ltoken", TextInputStyle.Paragraph, "HoYoLAB Cookies")
            {
                Required = true
            },
            new TextInputProperties("passphrase", TextInputStyle.Paragraph, "Passphrase")
            {
                Placeholder = "Do not use the same password as your Discord or HoYoLAB account!",
                Required = true,
                MaxLength = 64
            }
        };

        await RespondAsync(InteractionCallback.Modal(properties));
    }

    [UserCommand("Delete Profile")]
    public async Task DeleteProfileCommand()
    {
        var result = await m_UserRepository.DeleteUserAsync(Context.User.Id);

        InteractionMessageProperties responseMessage = new()
        {
            Content = result ? "Profile deleted!" : "No profile found!",
            Flags = MessageFlags.Ephemeral
        };

        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
    }
}
