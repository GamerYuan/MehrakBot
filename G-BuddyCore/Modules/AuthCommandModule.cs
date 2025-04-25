#region

using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace G_BuddyCore.Modules;

public class AuthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
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
}
