#region

using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

#endregion

namespace Mehrak.Infrastructure.Repositories;

public class ImageRepository : IImageRepository
{
    private readonly GridFSBucket m_Bucket;
    private readonly ILogger<ImageRepository> m_Logger;

    public ImageRepository(MongoDbService database, ILogger<ImageRepository> logger)
    {
        m_Bucket = database.Bucket;
        m_Logger = logger;
    }

    public async Task<bool> UploadFileAsync(string fileNameInDb, Stream sourceStream, string? contentType = null)
    {
        GridFSUploadOptions options = new()
        {
            Metadata = contentType != null ? new BsonDocument("contentType", contentType) : null
        };
        m_Logger.LogInformation("Uploading file to GridFS {FileNameInDb}", fileNameInDb);
        var objectId = await m_Bucket.UploadFromStreamAsync(fileNameInDb, sourceStream, options);
        return objectId != ObjectId.Empty;
    }

    public async Task<Stream> DownloadFileToStreamAsync(string fileNameInDb)
    {
        m_Logger.LogInformation("Downloading file from GridFS {FileNameInDb}", fileNameInDb);
        MemoryStream stream = new();
        await m_Bucket.DownloadToStreamByNameAsync(fileNameInDb, stream);
        stream.Position = 0;
        return stream;
    }

    public async Task DeleteFileAsync(string fileNameInDb)
    {
        m_Logger.LogInformation("Deleting file from GridFS {FileNameInDb}", fileNameInDb);
        var filter = Builders<GridFSFileInfo>.Filter.Eq(x => x.Filename, fileNameInDb);
        var fileInfo = await (await m_Bucket.FindAsync(filter)).FirstOrDefaultAsync();

        if (fileInfo != null) await m_Bucket.DeleteAsync(fileInfo.Id);
    }

    public async Task<bool> FileExistsAsync(string fileNameInDb)
    {
        m_Logger.LogInformation("Checking if file {FileNameInDb} exists in GridFS", fileNameInDb);
        var filter = Builders<GridFSFileInfo>.Filter.Eq(x => x.Filename, fileNameInDb);
        var fileInfo = await (await m_Bucket.FindAsync(filter)).FirstOrDefaultAsync();
        return fileInfo != null;
    }

    public async Task<List<string>> ListFilesAsync(string prefix = "")
    {
        m_Logger.LogInformation("Listing files in GridFS with prefix {Prefix}", prefix);
        var filter = Builders<GridFSFileInfo>
            .Filter.Regex(x => x.Filename, new BsonRegularExpression($"^{prefix}"));
        var fileInfos = await (await m_Bucket.FindAsync(filter)).ToListAsync();
        return [.. fileInfos.Select(x => x.Filename)];
    }
}