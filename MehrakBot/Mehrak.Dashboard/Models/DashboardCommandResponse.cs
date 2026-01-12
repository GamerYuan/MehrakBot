using Mehrak.Domain.Models;

namespace Mehrak.Dashboard.Models;

public sealed class DashboardCommandResponseDto
{
    public bool Success { get; init; }
    public bool IsContainer { get; init; }
    public bool IsEphemeral { get; init; }
    public IReadOnlyList<DashboardCommandComponentDto> Components { get; init; } = [];
}

public sealed class DashboardCommandComponentDto
{
    private DashboardCommandComponentDto() { }

    public DashboardCommandComponentType Type { get; private init; }
    public DashboardCommandTextDto? Text { get; private init; }
    public DashboardCommandAttachmentDto? Attachment { get; private init; }
    public DashboardCommandSectionDto? Section { get; private init; }

    public static DashboardCommandComponentDto FromText(CommandText text)
    {
        return new DashboardCommandComponentDto
        {
            Type = DashboardCommandComponentType.Text,
            Text = new DashboardCommandTextDto(text.Content, text.Type)
        };
    }

    public static DashboardCommandComponentDto FromAttachment(DashboardCommandAttachmentDto attachment)
    {
        return new DashboardCommandComponentDto
        {
            Type = DashboardCommandComponentType.Attachment,
            Attachment = attachment
        };
    }

    public static DashboardCommandComponentDto FromSection(
        IReadOnlyList<DashboardCommandTextDto> texts,
        DashboardCommandAttachmentDto attachment)
    {
        return new DashboardCommandComponentDto
        {
            Type = DashboardCommandComponentType.Section,
            Section = new DashboardCommandSectionDto(texts, attachment)
        };
    }
}

public enum DashboardCommandComponentType
{
    Text,
    Attachment,
    Section
}

public sealed record DashboardCommandTextDto(string Content, CommandText.TextType TextType);

public sealed record DashboardCommandSectionDto(IReadOnlyList<DashboardCommandTextDto> Components, DashboardCommandAttachmentDto Attachment);

public sealed record DashboardCommandAttachmentDto(string StorageFileName);
