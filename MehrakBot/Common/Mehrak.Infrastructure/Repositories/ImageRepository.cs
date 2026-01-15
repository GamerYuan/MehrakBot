#region

using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace Mehrak.Infrastructure.Repositories;

public class ImageRepository : IImageRepository
{
    private readonly IAmazonS3 m_S3;
    private readonly ILogger<ImageRepository> m_Logger;
    private readonly string m_Bucket;

    public ImageRepository(IAmazonS3 s3, IOptions<S3StorageConfig> options, ILogger<ImageRepository> logger)
    {
        m_S3 = s3;
        m_Bucket = options.Value.Bucket ?? throw new ArgumentNullException(nameof(options.Value.Bucket));
        m_Logger = logger;
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
        return (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;
    }

    public async Task<Stream> DownloadFileToStreamAsync(string fileName, CancellationToken cancellationToken = default)
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

        await response.ResponseStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return stream;
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
    }

    public async Task<bool> FileExistsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Checking if file exists in S3: {FileNameInDb}", fileName);

        try
        {
            var headReq = new GetObjectMetadataRequest
            {
                BucketName = m_Bucket,
                Key = fileName
            };

            var response = await m_S3.GetObjectMetadataAsync(headReq, cancellationToken).ConfigureAwait(false);
            return (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
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

        return keys;
    }
}
