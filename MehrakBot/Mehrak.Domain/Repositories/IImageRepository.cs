
namespace Mehrak.Domain.Repositories;

public interface IImageRepository
{
    Task<bool> UploadFileAsync(string fileName, Stream sourceStream, string? contentType = null, CancellationToken cancellationToken = default);

    Task<Stream> DownloadFileToStreamAsync(string fileName, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string fileName, CancellationToken cancellationToken = default);

    Task<bool> FileExistsAsync(string fileName, CancellationToken cancellationToken = default);

    Task<List<string>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default);
}
