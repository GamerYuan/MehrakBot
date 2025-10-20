using System.Diagnostics.CodeAnalysis;
using System.Text;

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
        IEnumerable<CommandSection>? sections = null, IEnumerable<CommandText>? texts = null)
    {
        return new CommandResult
        {
            IsSuccess = true,
            Data = new CommandResultData(title, content, footer, attachments, sections, texts)
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
        public IEnumerable<CommandText> Texts { get; }

        public CommandResultData(string? title, string? content,
            string? footer, IEnumerable<CommandAttachment>? attachments, IEnumerable<CommandSection>? sections,
            IEnumerable<CommandText>? texts)
        {
            Title = title;
            Content = content;
            Footer = footer;
            Attachments = attachments ?? [];
            Sections = sections ?? [];
            Texts = texts ?? [];
        }
    }
}

public class CommandSection
{
    public string? Title { get; }
    public string? Content { get; }
    public string? Footer { get; }
    public CommandAttachment Attachment { get; }

    public CommandSection(string? title, string? content, string? footer, CommandAttachment attachment)
    {
        Title = title;
        Content = content;
        Footer = footer;
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

public class CommandText
{
    public string Content { get; }
    public TextType Type { get; }

    public CommandText(string content, TextType type = TextType.Plain)
    {
        Content = content;
        Type = type;
    }

    public string ToFormattedString()
    {
        StringBuilder sb = new();
        if (Type.HasFlag(TextType.Header1))
        {
            sb.Append("# ");
        }
        else if (Type.HasFlag(TextType.Header2))
        {
            sb.Append("## ");
        }
        else if (Type.HasFlag(TextType.Header3))
        {
            sb.Append("### ");
        }

        if (Type.HasFlag(TextType.Bold))
        {
            sb.Append("**");
        }
        if (Type.HasFlag(TextType.Italic))
        {
            sb.Append('*');
        }

        sb.Append(Content);

        if (Type.HasFlag(TextType.Italic))
        {
            sb.Append('*');
        }
        if (Type.HasFlag(TextType.Bold))
        {
            sb.Append("**");
        }

        return sb.ToString();
    }

    [Flags]
    public enum TextType
    {
        Plain = 1 << 0,
        Header1 = 1 << 1,
        Header2 = 1 << 2,
        Header3 = 1 << 3,
        Bold = 1 << 4,
        Italic = 1 << 5
    }
}
