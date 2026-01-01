using Mehrak.Dashboard.Services;
using Mehrak.Domain.Models;

namespace Mehrak.Dashboard.Models;

public sealed class DashboardCommandResponseDto
{
    public bool Success { get; init; }
    public bool IsContainer { get; init; }
    public bool IsEphemeral { get; init; }
    public IReadOnlyList<DashboardCommandComponentDto> Components { get; init; } = [];

    public static async Task<DashboardCommandResponseDto> FromCommandResultAsync(
        CommandResult result,
        IAttachmentStorageService attachmentStorage,
        CancellationToken cancellationToken = default)
    {
        if (!result.IsSuccess || result.Data is null)
            throw new InvalidOperationException("Command result does not contain any data.");

        List<DashboardCommandComponentDto> components = [];
        foreach (var component in result.Data.Components)
        {
            var dto = await CreateComponentAsync(component, attachmentStorage, cancellationToken).ConfigureAwait(false);
            if (dto is not null)
                components.Add(dto);
        }

        return new DashboardCommandResponseDto
        {
            Success = true,
            IsContainer = result.Data.IsContainer,
            IsEphemeral = result.Data.IsEphemeral,
            Components = components
        };
    }

    private static async Task<DashboardCommandComponentDto?> CreateComponentAsync(
        ICommandResultComponent component,
        IAttachmentStorageService attachmentStorage,
        CancellationToken cancellationToken)
    {
        switch (component)
        {
            case CommandText text:
                return DashboardCommandComponentDto.FromText(text);
            case CommandAttachment attachment:
                {
                    var stored = await attachmentStorage.StoreAsync(attachment, cancellationToken).ConfigureAwait(false);
                    if (stored is null)
                        return null;
                    return DashboardCommandComponentDto.FromAttachment(new DashboardCommandAttachmentDto(stored.OriginalFileName, stored.StorageFileName));
                }
            case CommandSection section:
                {
                    var stored = await attachmentStorage.StoreAsync(section.Attachment, cancellationToken).ConfigureAwait(false);
                    if (stored is null)
                        return null;
                    var texts = section.Components.Select(t => new DashboardCommandTextDto(t.Content, t.Type)).ToArray();
                    return DashboardCommandComponentDto.FromSection(texts, new DashboardCommandAttachmentDto(stored.OriginalFileName, stored.StorageFileName));
                }
            default:
                return null;
        }
    }
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

public sealed record DashboardCommandAttachmentDto(string FileName, string StorageFileName);
