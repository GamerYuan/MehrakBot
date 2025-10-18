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

        if (data.Title != null || data.Sections.Any())
        {
            var container = new ComponentContainerProperties();
            container.AddComponents(new TextDisplayProperties($"### {data.Title}"));
            if (data.Content != null) container.AddComponents(new TextDisplayProperties(data.Content));
            if (data.Sections.Any())
            {
                foreach (var section in data.Sections)
                {
                    properties.AddAttachments(new AttachmentProperties(section.Attachment.FileName, section.Attachment.Content));
                    var sectionComponent = new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(new ComponentMediaProperties($"attachment://{section.Attachment.FileName}"))
                    );
                    if (section.Title != null) sectionComponent.AddComponents(new TextDisplayProperties($"### {section.Title}"));
                    if (section.Content != null) sectionComponent.AddComponents(new TextDisplayProperties(section.Content));
                    if (section.Footer != null) sectionComponent.AddComponents(new TextDisplayProperties($"-# {section.Footer}"));
                    container.AddComponents([sectionComponent]);
                }
            }
            if (data.Attachments.Any())
            {
                var mediaGallery = new MediaGalleryProperties();
                properties.AddAttachments(data.Attachments.Select(x => new AttachmentProperties(x.FileName, x.Content)));
                mediaGallery.AddItems(data.Attachments.Select(x =>
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{x.FileName}"))));
                container.AddComponents(mediaGallery);
            }
            if (data.Footer != null) container.AddComponents(new TextDisplayProperties(data.Footer));
            properties.AddComponents([container]);
        }
        else
        {
            if (data.Content != null) properties.AddComponents(new TextDisplayProperties(data.Content));
            if (data.Attachments.Any())
            {
                var mediaGallery = new MediaGalleryProperties();
                properties.AddAttachments(data.Attachments.Select(x => new AttachmentProperties(x.FileName, x.Content)));
                mediaGallery.AddItems(data.Attachments.Select(x =>
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{x.FileName}"))));
                properties.AddComponents(mediaGallery);
            }
            if (data.Footer != null) properties.AddComponents(new TextDisplayProperties(data.Footer));
        }

        return properties;
    }
}
