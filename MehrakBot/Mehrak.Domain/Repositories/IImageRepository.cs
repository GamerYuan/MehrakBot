namespace Mehrak.Domain.Repositories;

public interface IImageRepository
{
    Task<bool> UploadFileAsync(string fileNameInDb, Stream sourceStream, string? contentType = null);

    Task<Stream> DownloadFileToStreamAsync(string fileNameInDb);

    Task DeleteFileAsync(string fileNameInDb);

    Task<bool> FileExistsAsync(string fileNameInDb);

    Task<List<string>> ListFilesAsync(string prefix = "");
}