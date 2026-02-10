#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Models;

public interface ICommandResultComponent;

public enum AttachmentSourceType
{
    ImageStorage,
    AttachmentStorage
}

public interface ICommandResultAttachment
{
    string FileName { get; init; }
    AttachmentSourceType SourceType { get; }
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
        bool isContainer = false, bool isEphemeral = false, string? ephemeralMessage = null)
    {
        return new CommandResult
        {
            IsSuccess = true,
            Data = new CommandResultData(components, isContainer, isEphemeral, ephemeralMessage)
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
        public string? EphemeralMessage { get; }
        public IEnumerable<ICommandResultComponent> Components { get; }
        public bool IsContainer { get; }
        public bool IsEphemeral { get; }

        public CommandResultData(IEnumerable<ICommandResultComponent>? components, bool isContainer, bool isEphemeral,
            string? ephemeralMessage = null)
        {
            Components = components ?? [];
            IsContainer = isContainer;
            IsEphemeral = isEphemeral;
            EphemeralMessage = ephemeralMessage;
        }
    }
}

public class CommandSection : ICommandResultComponent
{
    public IEnumerable<CommandText> Components { get; }
    public ICommandResultAttachment Attachment { get; }

    public CommandSection(IEnumerable<CommandText> components, ICommandResultAttachment attachment)
    {
        Components = components;
        Attachment = attachment;
    }
}

public class StoredAttachment : ICommandResultAttachment
{
    public string FileName { get; init; }
    public AttachmentSourceType SourceType { get; init; }

    public StoredAttachment(string fileName, AttachmentSourceType sourceType)
    {
        FileName = fileName;
        SourceType = sourceType;
    }
}

public class CommandAttachment : ICommandResultComponent, ICommandResultAttachment
{

    public CommandAttachment(string fileName,
        AttachmentSourceType sourceType = AttachmentSourceType.AttachmentStorage)
    {
        FileName = fileName;
        SourceType = sourceType;
    }

    public string FileName { get; init; }
    public AttachmentSourceType SourceType { get; init; }
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
