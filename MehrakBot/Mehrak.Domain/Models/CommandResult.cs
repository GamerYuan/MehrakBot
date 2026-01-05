#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Models;

public interface ICommandResultComponent;

public interface ICommandResultAttachment
{
    string FileName { get; init; }
}

public interface ICommandResultEmbedAttachment
{
    (string, Stream) GetAttachment();
}

public enum CommandFailureReason
{
    Unknown,
    AuthError,
    ApiError,
    BotError
}

public class CommandResult
{
    [MemberNotNullWhen(true, nameof(Data))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    [MemberNotNullWhen(false, nameof(FailureReason))]
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public CommandFailureReason FailureReason { get; init; }

    public CommandResultData? Data { get; init; }

    public static CommandResult Success(IEnumerable<ICommandResultComponent>? components = null,
        bool isContainer = false, bool isEphemeral = false)
    {
        return new CommandResult
        {
            IsSuccess = true,
            Data = new CommandResultData(components, isContainer, isEphemeral)
        };
    }

    public static CommandResult Failure(CommandFailureReason failureReason, string errorMessage)
    {
        return new CommandResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            FailureReason = failureReason
        };
    }

    public class CommandResultData
    {
        public IEnumerable<ICommandResultComponent> Components { get; }
        public bool IsContainer { get; }
        public bool IsEphemeral { get; }

        public CommandResultData(IEnumerable<ICommandResultComponent>? components, bool isContainer, bool isEphemeral)
        {
            Components = components ?? [];
            IsContainer = isContainer;
            IsEphemeral = isEphemeral;
        }
    }
}

public class CommandSection : ICommandResultComponent, ICommandResultEmbedAttachment
{
    public IEnumerable<CommandText> Components { get; }
    public EmbeddedAttachment Attachment { get; }

    public CommandSection(IEnumerable<CommandText> components, EmbeddedAttachment attachment)
    {
        Components = components;
        Attachment = attachment;
    }

    public (string, Stream) GetAttachment()
    {
        return Attachment.GetAttachment();
    }
}

public class CommandAttachment : ICommandResultComponent, ICommandResultAttachment
{

    public CommandAttachment(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; init; }
}

public class EmbeddedAttachment : ICommandResultAttachment, ICommandResultEmbedAttachment
{
    public string FileName { get; init; }
    public Stream Content { get; init; }

    public EmbeddedAttachment(string fileName, Stream content)
    {
        FileName = fileName;
        Content = content;
    }

    public (string, Stream) GetAttachment()
    {
        return (FileName, Content);
    }
}


public class CommandText : ICommandResultComponent
{
    public string Content { get; }
    public TextType Type { get; }

    public CommandText(string content, TextType type = TextType.Plain)
    {
        Content = content;
        Type = type;
    }

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
