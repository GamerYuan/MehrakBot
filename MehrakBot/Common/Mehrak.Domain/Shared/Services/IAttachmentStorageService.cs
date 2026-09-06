namespace Mehrak.Domain.Shared.Services;

public record AttachmentDownloadResult(Stream Content, string ContentType);

public interface IAttachmentStorageService
{
    Task<bool> StoreAsync(string storageFileName, Stream stream, CancellationToken cancellationToken = default);

    Task<AttachmentDownloadResult?> DownloadAsync(string storageFileName, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storageFileName, CancellationToken cancellationToken = default);

    bool IsValidStorageFileName(string storageFileName);
}
