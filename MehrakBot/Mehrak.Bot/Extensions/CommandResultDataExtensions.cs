#region

using System.Text;
using Mehrak.Domain.Models;
using NetCord;
using NetCord.Rest;
using static Mehrak.Domain.Models.CommandResult;
using static Mehrak.Domain.Models.CommandText;

#endregion

namespace Mehrak.Bot.Extensions;

internal static class CommandResultDataExtensions
{
    public static InteractionMessageProperties ToMessage(this CommandResultData data)
    {
        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);

        if (data.IsContainer)
        {
            var container = new ComponentContainerProperties();

            foreach (var component in data.Components)
                switch (component)
                {
                    case CommandSection section:
                        var sectionComponent = new ComponentSectionProperties(
                                new ComponentSectionThumbnailProperties(
                                    new ComponentMediaProperties($"attachment://{section.Attachment.FileName}")))
                            .AddComponents(section.Components.Select(x =>
                                new TextDisplayProperties(x.ToFormattedString())));
                        container.AddComponents([sectionComponent]);
                        break;

                    case CommandAttachment attachment:
                        if (container.Components.LastOrDefault() is not MediaGalleryProperties gallery)
                        {
#pragma warning disable IDE0028 // Simplify collection initialization
                            gallery = new MediaGalleryProperties();
#pragma warning restore IDE0028 // Simplify collection initialization
                            container.AddComponents(gallery);
                        }

                        gallery.AddItems(
                            new MediaGalleryItemProperties(
                                new ComponentMediaProperties($"attachment://{attachment.FileName}")));
                        break;

                    case CommandText text:
                        container.AddComponents(new TextDisplayProperties(text.ToFormattedString()));
                        break;

                    default:
                        break;
                }

            properties.AddComponents([container]);
        }
        else
        {
            foreach (var component in data.Components)
                switch (component)
                {
                    case CommandAttachment attachment:
                        if (properties.Components?.LastOrDefault() is not MediaGalleryProperties gallery)
                        {
#pragma warning disable IDE0028 // Simplify collection initialization
                            gallery = new MediaGalleryProperties();
#pragma warning restore IDE0028 // Simplify collection initialization
                            properties.AddComponents(gallery);
                        }

                        gallery.AddItems(
                            new MediaGalleryItemProperties(
                                new ComponentMediaProperties($"attachment://{attachment.FileName}")));
                        break;

                    case CommandText text:
                        properties.AddComponents(new TextDisplayProperties(text.ToFormattedString()));
                        break;

                    default:
                        break;
                }
        }

        properties.AddAttachments(data.Components.OfType<ICommandResultAttachment>()
            .Select(x => x.GetAttachment()).Select(x => new AttachmentProperties(x.Item1, x.Item2)));

        return properties;
    }

    public static string ToFormattedString(this CommandText text)
    {
        StringBuilder sb = new();
        if (text.Type.HasFlag(TextType.Header1))
            sb.Append("# ");
        else if (text.Type.HasFlag(TextType.Header2))
            sb.Append("## ");
        else if (text.Type.HasFlag(TextType.Header3))
            sb.Append("### ");
        else if (text.Type.HasFlag(TextType.Footer)) sb.Append("-# ");

        if (text.Type.HasFlag(TextType.Bold)) sb.Append("**");
        if (text.Type.HasFlag(TextType.Italic)) sb.Append('*');

        sb.Append(text.Content);

        if (text.Type.HasFlag(TextType.Italic)) sb.Append('*');
        if (text.Type.HasFlag(TextType.Bold)) sb.Append("**");

        return sb.ToString();
    }
}
