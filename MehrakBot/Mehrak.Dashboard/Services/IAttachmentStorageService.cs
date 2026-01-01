using Mehrak.Domain.Models;

namespace Mehrak.Dashboard.Services;

public record StoredAttachmentResult(string OriginalFileName, string StorageFileName);

public record AttachmentDownloadResult(Stream Content, string ContentType);

public interface IAttachmentStorageService
{
    Task<StoredAttachmentResult?> StoreAsync(CommandAttachment attachment, CancellationToken cancellationToken = default);

    Task<AttachmentDownloadResult?> DownloadAsync(string storageFileName, CancellationToken cancellationToken = default);
}
