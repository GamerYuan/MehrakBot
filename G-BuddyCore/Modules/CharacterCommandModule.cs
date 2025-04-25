#region

using System.Security.Cryptography;
using System.Text.Json;
using G_BuddyCore.Repositories;
using G_BuddyCore.Services;
using G_BuddyCore.Services.Genshin;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace G_BuddyCore.Modules;

public class CharacterCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly ILogger<CharacterCommandModule> m_Logger;
    private readonly GenshinCharacterApiService m_GenshinApi;

    public CharacterCommandModule(UserRepository userRepository, ILogger<CharacterCommandModule> logger,
        GenshinCharacterApiService genshinApi)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_GenshinApi = genshinApi;
    }

    [SlashCommand("character", "Get character card")]
    public async Task CharacterCommand([SlashCommandParameter(Name = "passphrase")] string passphrase)
    {
        try
        {
            var user = await m_UserRepository.GetUserAsync(Context.User.Id);
            InteractionMessageProperties message = new()
            {
                Flags = MessageFlags.Ephemeral
            };
            if (user == null)
            {
                message.Content = "No profile found. Please authenticate your account first.";
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(message));
                return;
            }

            var ltoken = CookieService.DecryptCookie(user.LToken, passphrase);
            var characterList = await m_GenshinApi.GetAllCharactersAsync(user.LtUid, ltoken);
            if (string.IsNullOrEmpty(characterList))
            {
                message.Content = "No characters found. Please check your account.";
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(message));
                return;
            }

            var characterListMessage = $"Characters: {characterList}";
            message.Content = characterListMessage;
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(message));
        }
        catch (AuthenticationTagMismatchException)
        {
            InteractionMessageProperties message = new()
            {
                Content = "Invalid passphrase. Please try again.",
                Flags = MessageFlags.Ephemeral
            };

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(message));
        }
        catch (JsonException)
        {
            InteractionMessageProperties message = new()
            {
                Content = "Invalid token. Please setup your token again.",
                Flags = MessageFlags.Ephemeral
            };

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(message));
        }
        catch (Exception e)
        {
            InteractionMessageProperties message = new()
            {
                Content = "An error occurred while processing your request. Please try again later.",
                Flags = MessageFlags.Ephemeral
            };

            m_Logger.LogError(e.ToString());
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(message));
        }
    }
}
