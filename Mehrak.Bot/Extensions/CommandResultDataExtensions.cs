using NetCord;
using NetCord.Rest;
using static Mehrak.Domain.Models.CommandResult;

namespace Mehrak.Bot.Extensions;

internal static class CommandResultDataExtensions
{
    public static InteractionMessageProperties ToMessage(this CommandResultData data)
    {
        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);

        if (data.Title == null)
        {
            if (data.Content != null) properties.AddComponents(new TextDisplayProperties(data.Content));
            if (data.Attachments.Any())
            {
                var mediaGallery = new MediaGalleryProperties();
                properties.AddAttachments(data.Attachments.Select(x => new AttachmentProperties(x.Item1, x.Item2)));
                mediaGallery.AddItems(data.Attachments.Select(x =>
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{x.Item1}"))));
                properties.AddComponents(mediaGallery);
            }
            if (data.Footer != null) properties.AddComponents(new TextDisplayProperties(data.Footer));
        }
        else
        {
            var container = new ComponentContainerProperties();
            container.AddComponents(new TextDisplayProperties($"## {data.Title}"));
            if (data.Content != null) container.AddComponents(new TextDisplayProperties(data.Content));
            if (data.Attachments.Any())
            {
                var mediaGallery = new MediaGalleryProperties();
                properties.AddAttachments(data.Attachments.Select(x => new AttachmentProperties(x.Item1, x.Item2)));
                mediaGallery.AddItems(data.Attachments.Select(x =>
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{x.Item1}"))));
                container.AddComponents(mediaGallery);
            }
            if (data.Footer != null) container.AddComponents(new TextDisplayProperties(data.Footer));
            properties.AddComponents(container);
        }

        return properties;
    }
}
