#region

using System.Text;

#endregion

namespace Mehrak.GameApi.Shared;

/// <summary>
/// One bounded diagnostic read of a failed HTTP response body. <see cref="UnavailableReason"/> is an exception type
/// name or a stable reason marker, never an exception message.
/// </summary>
internal readonly record struct GameApiLogBodyCapture(string? Body, string? UnavailableReason, string ContentType,
    long? ContentLength)
{
    public static GameApiLogBodyCapture NotRead(string contentType, long? contentLength)
    {
        return new GameApiLogBodyCapture(null, null, contentType, contentLength);
    }

    public static GameApiLogBodyCapture Unavailable(string reason, string contentType, long? contentLength)
    {
        return new GameApiLogBodyCapture(null, reason, contentType, contentLength);
    }
}

/// <summary>
/// Reads a failed response body for diagnostics with a hard size cap and charset-aware decoding. Ordinary read
/// failures are reported as stable markers. Cancellation requested on the supplied token is control flow, not a
/// diagnostic failure, so it is rethrown for the request pipeline to report as <c>Cancelled</c> or <c>Timeout</c>.
/// </summary>
internal static class GameApiLogBodyReader
{
    public const int MaxBodyBytes = 64 * 1024;
    public const string SizeLimitReason = "SizeLimit";
    public const string CancelledReason = "Cancelled";

    public static async Task<GameApiLogBodyCapture> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var contentType = GameApiLog.Text(content.Headers.ContentType?.MediaType, 128);
        var contentLength = content.Headers.ContentLength;

        if (contentLength > MaxBodyBytes)
            return GameApiLogBodyCapture.Unavailable(SizeLimitReason, contentType, contentLength);

        try
        {
            var bytes = await ReadBoundedAsync(content, cancellationToken);
            if (bytes is null)
                return GameApiLogBodyCapture.Unavailable(SizeLimitReason, contentType, contentLength);

            var body = Decode(bytes, content.Headers.ContentType?.CharSet);
            return new GameApiLogBodyCapture(body, null, contentType, contentLength);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return GameApiLogBodyCapture.Unavailable(CancelledReason, contentType, contentLength);
        }
        catch (Exception exception)
        {
            return GameApiLogBodyCapture.Unavailable(exception.GetType().Name, contentType, contentLength);
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            // Request at most the remaining budget plus one sentinel byte so an unknown-length body is never
            // pulled past the cap into a throwaway chunk.
            var remaining = MaxBodyBytes + 1 - (int)buffer.Length;
            if (remaining <= 0)
                return null;

            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)),
                cancellationToken);
            if (read == 0)
                break;

            buffer.Write(chunk, 0, read);

            if (buffer.Length > MaxBodyBytes)
                return null;
        }

        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        var text = ResolveEncoding(bytes, charset).GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static Encoding ResolveEncoding(byte[] bytes, string? charset)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim().Trim('"'));
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
            catch (NotSupportedException)
            {
                return Encoding.UTF8;
            }
        }

        return Encoding.UTF8;
    }
}
