#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Models;

public interface ICommandResultComponent;

public interface ICommandResultAttachment
{
    public (string, Stream) GetAttachment();
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

public class CommandSection : ICommandResultComponent, ICommandResultAttachment
{
    public IEnumerable<CommandText> Components { get; }
    public CommandAttachment Attachment { get; }

    public CommandSection(IEnumerable<CommandText> components, CommandAttachment attachment)
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
    public string FileName { get; }
    public Stream Content { get; }

    public CommandAttachment(string fileName, Stream content)
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