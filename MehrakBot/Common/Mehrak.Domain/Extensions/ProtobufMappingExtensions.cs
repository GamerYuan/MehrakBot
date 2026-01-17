using Mehrak.Domain.Models;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Domain.Extensions;

public static class ProtobufMappingExtensions
{
    // Domain -> Proto

    public static Proto.CommandResult ToProto(this CommandResult domain)
    {
        var proto = new Proto.CommandResult
        {
            IsSuccess = domain.IsSuccess,
            ErrorMessage = domain.ErrorMessage ?? string.Empty,
            FailureReason = (Proto.CommandFailureReason)domain.FailureReason
        };

        if (domain.Data != null)
        {
            proto.Data = domain.Data.ToProto();
        }

        return proto;
    }

    public static Proto.CommandResultData ToProto(this CommandResult.CommandResultData domain)
    {
        var proto = new Proto.CommandResultData
        {
            IsContainer = domain.IsContainer,
            IsEphemeral = domain.IsEphemeral
        };

        foreach (var component in domain.Components)
        {
            proto.Components.Add(component.ToProto());
        }

        return proto;
    }

    public static Proto.CommandComponent ToProto(this ICommandResultComponent component)
    {
        var proto = new Proto.CommandComponent();

        switch (component)
        {
            case CommandText text:
                proto.Text = new Proto.CommandText
                {
                    Content = text.Content,
                    TypeFlags = (int)text.Type
                };
                break;

            case CommandSection section:
                proto.Section = new Proto.CommandSection
                {
                    Attachment = section.Attachment.ToProto()
                };
                foreach (var textItem in section.Components)
                {
                    proto.Section.Components.Add(new Proto.CommandText
                    {
                        Content = textItem.Content,
                        TypeFlags = (int)textItem.Type
                    });
                }
                break;

            case ICommandResultAttachment attachmentComponent:
                // Matches CommandAttachment (which is both Component and Attachment)
                proto.Attachment = attachmentComponent.ToProto();
                break;

            default:
                // Unknown types are ignored or could throw
                break;
        }

        return proto;
    }

    public static Proto.AttachmentReference ToProto(this ICommandResultAttachment attachment)
    {
        return new Proto.AttachmentReference
        {
            FileName = attachment.FileName,
            SourceType = (Proto.AttachmentSourceType)attachment.SourceType
        };
    }

    public static Proto.ExecuteRequest ToExecuteRequest(string commandName, ulong userId, ulong ltUid, string lToken, IEnumerable<(string Key, object Value)> commandParams)
    {
        var request = new Proto.ExecuteRequest
        {
            CommandName = commandName,
            DiscordUserId = userId,
            LtUid = ltUid,
            LToken = lToken ?? string.Empty
        };

        foreach (var (key, value) in commandParams)
        {
            request.Parameters.Add(key, value?.ToString() ?? string.Empty);
        }
        return request;
    }

    // Proto -> Domain

    public static CommandResult ToDomain(this Proto.CommandResult proto)
    {
        if (!proto.IsSuccess)
        {
            return CommandResult.Failure((CommandFailureReason)proto.FailureReason, proto.ErrorMessage);
        }

        var components = proto.Data.Components
            .Select(c => c.ToDomain())
            .Where(c => c != null)
            .Cast<ICommandResultComponent>();

        return CommandResult.Success(components, proto.Data.IsContainer, proto.Data.IsEphemeral);
    }

    public static ICommandResultComponent? ToDomain(this Proto.CommandComponent proto)
    {
        switch (proto.ComponentCase)
        {
            case Proto.CommandComponent.ComponentOneofCase.Text:
                return new CommandText(proto.Text.Content, (CommandText.TextType)proto.Text.TypeFlags);

            case Proto.CommandComponent.ComponentOneofCase.Section:
                var sectionTexts = proto.Section.Components.Select(t => new CommandText(t.Content, (CommandText.TextType)t.TypeFlags));
                // Use StoredAttachment for the attachment inside a section
                var sectionAttachment = new StoredAttachment(
                    proto.Section.Attachment.FileName,
                    (AttachmentSourceType)proto.Section.Attachment.SourceType);

                return new CommandSection(sectionTexts, sectionAttachment);

            case Proto.CommandComponent.ComponentOneofCase.Attachment:
                // Standalone attachment component must be CommandAttachment
                return new CommandAttachment(
                    proto.Attachment.FileName,
                    (AttachmentSourceType)proto.Attachment.SourceType);

            default:
                return null;
        }
    }
}
