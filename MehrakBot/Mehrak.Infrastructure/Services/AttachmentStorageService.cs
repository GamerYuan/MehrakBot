using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Services;

public sealed class AttachmentStorageService : IAttachmentStorageService
{
    private readonly IAmazonS3 m_S3;
    private readonly string m_Bucket;
    private readonly ILogger<AttachmentStorageService> m_Logger;

    public AttachmentStorageService(IAmazonS3 s3, IConfiguration config, ILogger<AttachmentStorageService> logger)
    {
        m_S3 = s3;
        m_Bucket = config["AttachmentStorage:Bucket"] ?? throw new ArgumentNullException("AttachmentStorage:Bucket");
        m_Logger = logger;
    }

    public async Task<bool> StoreAsync(string storageFileName, Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream == Stream.Null)
            return false;

        if (stream.CanSeek)
            stream.Position = 0;

        var extension = Path.GetExtension(storageFileName);
        var objectKey = storageFileName;

        var contentType = ResolveContentType(extension);

        var putReq = new PutObjectRequest
        {
            BucketName = m_Bucket,
            Key = objectKey,
            InputStream = stream,
            AutoCloseStream = false,
            ContentType = contentType ?? "application/octet-stream",
        };

        if (!string.IsNullOrEmpty(contentType))
        {
            putReq.Metadata["content-type"] = contentType;
        }

        var response = await m_S3.PutObjectAsync(putReq, cancellationToken).ConfigureAwait(false);
        var success = (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;

        if (!success)
        {
            m_Logger.LogError("Failed to upload attachment {FileName} to {ObjectKey}", storageFileName, objectKey);
            return false;
        }

        m_Logger.LogDebug("Stored attachment {FileName} as {ObjectKey}", storageFileName, objectKey);
        return true;
    }

    public async Task<AttachmentDownloadResult?> DownloadAsync(string storageFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageFileName) || !IsValidStorageFileName(storageFileName))
            return null;

        var objectKey = storageFileName;
        var getReq = new GetObjectRequest
        {
            BucketName = m_Bucket,
            Key = objectKey,
        };

        using var response = await m_S3.GetObjectAsync(getReq, cancellationToken).ConfigureAwait(false);
        MemoryStream stream = new();

        if ((int)response.HttpStatusCode >= 300) return null;

        await response.ResponseStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;

        if (stream == Stream.Null || stream.Length == 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        var contentType = ResolveContentType(Path.GetExtension(storageFileName));
        return new AttachmentDownloadResult(stream, contentType);
    }

    public async Task<bool> ExistsAsync(string storageFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageFileName) || !IsValidStorageFileName(storageFileName))
            return false;
        var objectKey = storageFileName;
        var metadataReq = new GetObjectMetadataRequest
        {
            BucketName = m_Bucket,
            Key = objectKey,
        };
        try
        {
            var response = await m_S3.GetObjectMetadataAsync(metadataReq, cancellationToken).ConfigureAwait(false);
            return (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public static bool IsValidStorageFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var safeName = Path.GetFileName(fileName);
        if (!ReferenceEquals(fileName, safeName))
            return false;

        var span = safeName.AsSpan();
        foreach (var ch in span)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
                continue;
            return false;
        }

        const int expectedHashLength = 64; // sha256 hash length in hex
        var hashPart = safeName.Split('.')[0];
        return hashPart.Length == expectedHashLength && hashPart.All(Uri.IsHexDigit);
    }

    private static string ResolveContentType(string? extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
