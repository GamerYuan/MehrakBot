#region

using G_BuddyCore.Repositories;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace G_BuddyCore.Modules;

public class AuthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly ILogger<AuthCommandModule> m_Logger;

    public AuthCommandModule(UserRepository userRepository, ILogger<AuthCommandModule> logger)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
    }

    [UserCommand("Authenticate HoYoLAB Profile")]
    public async Task AuthCommand()
    {
        m_Logger.LogInformation("User {UserId} is authenticating HoYoLAB profile", Context.User.Id);

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
        m_Logger.LogInformation("User {UserId} requested profile deletion", Context.User.Id);

        var result = await m_UserRepository.DeleteUserAsync(Context.User.Id);

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
