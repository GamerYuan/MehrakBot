#region

using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

#endregion

namespace Mehrak.Bot.Modules.Common;

public class RemoveCharacterCardModule : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("remove_card")]
    public async Task RemoveCharacterCard()
    {
        if (Context.Interaction.User.Id != Context.Message.InteractionMetadata?.User.Id) return;

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);
        await Context.Interaction.DeleteResponseAsync();
    }
}
