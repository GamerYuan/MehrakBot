#region

using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Modules;

public class RemoveCharacterCardModule : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("remove_card")]
    public async Task RemoveCharacterCard()
    {
        if (Context.Interaction.User.Id != Context.Message.InteractionMetadata?.User.Id) return;

        await Context.Channel.DeleteMessageAsync(Context.Message.Id);
    }
}
