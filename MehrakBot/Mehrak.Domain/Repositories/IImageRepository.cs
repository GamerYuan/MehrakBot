namespace Mehrak.Domain.Repositories;

public interface IImageRepository
{
    public Task<bool> UploadFileAsync(string fileNameInDb, Stream sourceStream, string? contentType = null);

    public Task<Stream> DownloadFileToStreamAsync(string fileNameInDb);

    public Task DeleteFileAsync(string fileNameInDb);

    public Task<bool> FileExistsAsync(string fileNameInDb);

    public Task<List<string>> ListFilesAsync(string prefix = "");
}
