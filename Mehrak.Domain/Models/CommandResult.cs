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
        string? footer = null, IEnumerable<(string, Stream)>? attachments = null)
    {
        return new CommandResult
        {
            IsSuccess = true,
            Data = new CommandResultData(title, content, footer, attachments)
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
        public IEnumerable<(string, Stream)> Attachments { get; }

        public CommandResultData(string? title, string? content,
            string? footer, IEnumerable<(string, Stream)>? attachments)
        {
            Title = title;
            Content = content;
            Footer = footer;
            Attachments = attachments ?? [];
        }
    }
}
