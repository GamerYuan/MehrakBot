#region

using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Common;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace Mehrak.Infrastructure.Repositories;

public class ImageRepository : IImageRepository
{
    private readonly IAmazonS3 m_S3;
    private readonly ILogger<ImageRepository> m_Logger;
    private readonly string m_Bucket;
    private readonly IMemoryCache m_ExistsCache;

    public ImageRepository(IAmazonS3 s3, IOptions<S3StorageConfig> options, ILogger<ImageRepository> logger, IMemoryCache existsCache)
    {
        m_S3 = s3;
        m_Bucket = options.Value.Bucket ?? throw new ArgumentNullException(nameof(options.Value.Bucket));
        m_Logger = logger;
        m_ExistsCache = existsCache;
    }

    public async Task<bool> UploadFileAsync(string fileName, Stream sourceStream,
        string? contentType = null, CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Uploading file to S3: {FileName}", fileName);

        if (sourceStream.CanSeek) sourceStream.Position = 0;

        var putReq = new PutObjectRequest
        {
            BucketName = m_Bucket,
            Key = fileName,
            InputStream = sourceStream,
            AutoCloseStream = false,
            ContentType = contentType ?? "application/octet-stream"
        };

        if (!string.IsNullOrEmpty(contentType))
        {
            putReq.Metadata["content-type"] = contentType;
        }

        var response = await m_S3.PutObjectAsync(putReq, cancellationToken).ConfigureAwait(false);

        var success = (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;
        if (success)
        {
            m_ExistsCache.Set(fileName, true, new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            });
        }
        else
        {
            m_Logger.LogError("Failed to upload file {FileName} to S3. Status code: {StatusCode}", fileName, response.HttpStatusCode);
        }

        return success;
    }

    public async Task<Stream> DownloadFileToStreamAsync(string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            m_Logger.LogDebug("Downloading file from S3: {FileName}", fileName);

            var getReq = new GetObjectRequest
            {
                BucketName = m_Bucket,
                Key = fileName,
            };

            using var response = await m_S3.GetObjectAsync(getReq, cancellationToken).ConfigureAwait(false);
            MemoryStream stream = new();

            if ((int)response.HttpStatusCode >= 300) return Stream.Null;
            m_ExistsCache.Set(fileName, true, new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            });

            await response.ResponseStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            return stream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            m_Logger.LogWarning("File {FileName} not found in S3 bucket {Bucket}", fileName, m_Bucket);
            m_ExistsCache.Remove(fileName);
            throw new ImageNotFoundException(fileName);
        }
    }

    public async Task DeleteFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Deleting file from S3: {FileName}", fileName);

        var delReq = new DeleteObjectRequest
        {
            BucketName = m_Bucket,
            Key = fileName
        };
        await m_S3.DeleteObjectAsync(delReq, cancellationToken).ConfigureAwait(false);
        m_ExistsCache.Remove(fileName);
    }

    public async Task<bool> FileExistsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (m_ExistsCache.TryGetValue(fileName, out bool exists))
        {
            m_Logger.LogDebug("File existence found in cache: {Exists}", exists);
            return exists;
        }

        m_Logger.LogDebug("Checking if file exists in S3: {FileNameInDb}", fileName);

        try
        {
            var headReq = new GetObjectMetadataRequest
            {
                BucketName = m_Bucket,
                Key = fileName
            };

            var response = await m_S3.GetObjectMetadataAsync(headReq, cancellationToken).ConfigureAwait(false);
            exists = (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;
            m_ExistsCache.Set(fileName, exists, new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            });
            return exists;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            exists = false;
            m_ExistsCache.Remove(fileName);
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            m_Logger.LogError(ex, "GetObjectMetadata failed for file {FileName} in bucket {Bucket}", fileName, m_Bucket);
            return false;
        }
    }

    public async Task<List<string>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Listing files in S3 bucket {Bucket} with prefix {Prefix}", m_Bucket, prefix);

        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var listReq = new ListObjectsV2Request
            {
                BucketName = m_Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000
            };

            var response = await m_S3.ListObjectsV2Async(listReq, cancellationToken).ConfigureAwait(false);

            keys.AddRange(response.S3Objects.Select(o => o.Key));
            continuationToken = response.IsTruncated ?? false ? response.NextContinuationToken : null;

        } while (continuationToken != null);

        foreach (var key in keys)
        {
            m_Logger.LogDebug("Found file in S3: {Key}", key);
            m_ExistsCache.Set(key, true, new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            });
        }

        return keys;
    }

    public void InvalidateCache(string fileName)
    {
        m_Logger.LogDebug("Invalidating cache for file: {FileName}", fileName);
        m_ExistsCache.Remove(fileName);
    }
}
