using Mehrak.Domain.Command.Models;

namespace Mehrak.Bot.Shared.Models;

// Discord-only representation. Never serialized into the Application RPC contract.
internal interface IPresentationComponent;

internal interface IPresentationAttachment
{
    string FileName { get; }
    AttachmentSourceType SourceType { get; }
    string? StorageFileName { get; }
}

internal sealed class CommandPresentation
{
    public string? EphemeralMessage { get; }
    public IReadOnlyList<IPresentationComponent> Components { get; }
    public bool IsContainer { get; }
    public bool IsEphemeral { get; }

    public CommandPresentation(IEnumerable<IPresentationComponent>? components, bool isContainer, bool isEphemeral,
        string? ephemeralMessage = null)
    {
        Components = components?.ToArray() ?? [];
        IsContainer = isContainer;
        IsEphemeral = isEphemeral;
        EphemeralMessage = ephemeralMessage;
    }

    public IEnumerable<IPresentationAttachment> Attachments => Components.OfType<IPresentationAttachment>()
        .Concat(Components.OfType<CommandSection>().Select(x => x.Attachment));
}

internal sealed class CommandSection(IEnumerable<CommandText> components, IPresentationAttachment attachment) : IPresentationComponent
{
    public IReadOnlyList<CommandText> Components { get; } = components.ToArray();
    public IPresentationAttachment Attachment { get; } = attachment;
}

internal sealed class CommandAttachment(string fileName,
    AttachmentSourceType sourceType = AttachmentSourceType.AttachmentStorage,
    string? storageFileName = null) : IPresentationComponent, IPresentationAttachment
{
    public string FileName { get; } = fileName;
    public AttachmentSourceType SourceType { get; } = sourceType;
    public string? StorageFileName { get; } = storageFileName;
}

internal sealed class CommandText(string content, CommandText.TextType type = CommandText.TextType.Plain) : IPresentationComponent
{
    public string Content { get; } = content;
    public TextType Type { get; } = type;

    [Flags]
    public enum TextType
    {
        Plain = 1 << 0,
        Header1 = 1 << 1,
        Header2 = 1 << 2,
        Header3 = 1 << 3,
        Bold = 1 << 4,
        Italic = 1 << 5,
        Footer = 1 << 6
    }
}
