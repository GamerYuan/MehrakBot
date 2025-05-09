#region

using MehrakCore.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

#endregion

namespace MehrakCore.Repositories;

public class ImageRepository
{
    private readonly GridFSBucket m_Bucket;
    private readonly ILogger<ImageRepository> m_Logger;

    public ImageRepository(MongoDbService database, ILogger<ImageRepository> logger)
    {
        m_Bucket = database.Bucket;
    }

    public async Task<ObjectId> UploadFileAsync(string fileNameInDb, Stream sourceStream, string? contentType = null)
    {
        var options = new GridFSUploadOptions
        {
            Metadata = contentType != null ? new BsonDocument("contentType", contentType) : null
        };
        return await m_Bucket.UploadFromStreamAsync(fileNameInDb, sourceStream, options);
    }

    public async Task<Stream> DownloadFileToStreamAsync(string fileNameInDb)
    {
        MemoryStream stream = new();
        await m_Bucket.DownloadToStreamByNameAsync(fileNameInDb, stream);
        stream.Position = 0;
        return stream;
    }

    public async Task DeleteFileAsync(string fileNameInDb)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(x => x.Filename, fileNameInDb);
        var fileInfo = await (await m_Bucket.FindAsync(filter)).FirstOrDefaultAsync();

        if (fileInfo != null) await m_Bucket.DeleteAsync(fileInfo.Id);
    }

    public async Task<bool> FileExistsAsync(string fileNameInDb)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(x => x.Filename, fileNameInDb);
        var fileInfo = await (await m_Bucket.FindAsync(filter)).FirstOrDefaultAsync();
        return fileInfo != null;
    }
}
