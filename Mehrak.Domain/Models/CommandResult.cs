using System.Diagnostics.CodeAnalysis;

namespace Mehrak.Domain.Models;

public class CommandResult
{
    [MemberNotNullWhen(true, nameof(Data))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public CommandResultData? Data { get; init; }

    public static CommandResult Success(string? title = null, string? content = null,
        string? footer = null, IEnumerable<CommandAttachment>? attachments = null,
        IEnumerable<CommandSection>? sections = null)
    {
        return new CommandResult
        {
            IsSuccess = true,
            Data = new CommandResultData(title, content, footer, attachments, sections)
        };
    }

    public static CommandResult Failure(string? errorMessage = null)
    {
        return new CommandResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    public class CommandResultData
    {
        public string? Title { get; }
        public string? Content { get; }
        public string? Footer { get; }
        public IEnumerable<CommandAttachment> Attachments { get; }
        public IEnumerable<CommandSection> Sections { get; }

        public CommandResultData(string? title, string? content,
            string? footer, IEnumerable<CommandAttachment>? attachments, IEnumerable<CommandSection>? sections)
        {
            Title = title;
            Content = content;
            Footer = footer;
            Attachments = attachments ?? [];
            Sections = sections ?? [];
        }
    }
}

public class CommandSection
{
    public string? Title { get; }
    public string? Content { get; }
    public CommandAttachment Attachment { get; }

    public CommandSection(string? title, string? content, CommandAttachment attachment)
    {
        Title = title;
        Content = content;
        Attachment = attachment;
    }
}

public class CommandAttachment
{
    public string FileName { get; }
    public Stream Content { get; }

    public CommandAttachment(string fileName, Stream content)
    {
        FileName = fileName;
        Content = content;
    }
}
