using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Models;
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

    public async Task<StoredAttachmentResult?> StoreAsync(CommandAttachment attachment, CancellationToken cancellationToken = default)
    {
        var (fileName, stream) = attachment.GetAttachment();
        if (stream == Stream.Null)
            return null;

        using MemoryStream buffer = new();
        if (stream.CanSeek)
            stream.Position = 0;

        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (buffer.Length == 0)
            return null;

        buffer.Position = 0;
        string hash;
        using (var sha256 = SHA256.Create())
        {
            hash = Convert.ToHexString(await sha256.ComputeHashAsync(buffer, cancellationToken)).ToLowerInvariant();
        }

        buffer.Position = 0;
        var extension = Path.GetExtension(fileName);
        var storageFileName = string.IsNullOrEmpty(extension)
            ? hash
            : $"{hash}{extension.ToLowerInvariant()}";
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
            m_Logger.LogError("Failed to upload attachment {FileName} to {ObjectKey}", fileName, objectKey);
            return null;
        }

        m_Logger.LogDebug("Stored attachment {FileName} as {ObjectKey}", fileName, objectKey);
        return new StoredAttachmentResult(fileName, storageFileName);
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
