#region

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Shared;

/// <summary>
/// Sanitizes request and response metadata before it reaches <see cref="ILogger"/>. Callers pass only the returned
/// scalar strings; raw URIs, headers, exceptions and response payloads are never forwarded to a log sink.
/// </summary>
internal static class GameApiLog
{
    public const string RedactedMarker = "<redacted>";
    public const string OmittedMarker = GameApiLogBodyRenderer.OmittedMarker;
    public const string TruncatedMarker = GameApiLogBodyRenderer.TruncatedMarker;
    public const string UnavailableMarker = "<unavailable>";
    public const string InvalidUriMarker = "<invalid-uri>";
    public const string NotReadMarker = "<not-read>";
    public const string AuthBodySuppressedMarker = "<auth>";
    public const string NoSafeQueryParameters = "<none>";

    private const int MaxEndpointLength = 512;
    private const int MaxTextLength = 256;
    private const int MaxExceptionTypeLength = 128;
    private const int MaxSafeQueryValueLength = 12;
    private const string SafeQueryName = "entry_page_id";

    /// <summary>
    /// Returns a scheme/host/path endpoint with user info, query values and fragment removed. Values that cannot be
    /// parsed as an absolute HTTP URI are replaced by a marker; the original string is never returned.
    /// </summary>
    public static string Endpoint(string? requestUri)
    {
        if (string.IsNullOrWhiteSpace(requestUri))
            return UnavailableMarker;

        try
        {
            if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) || !IsHttp(uri))
                return InvalidUriMarker;

            var endpoint = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.UriEscaped);
            return Text(endpoint, MaxEndpointLength);
        }
        catch
        {
            return InvalidUriMarker;
        }
    }

    /// <summary>
    /// Returns the only query value that is safe to retain, <c>entry_page_id</c>, and only when the raw value is
    /// unique and consists of 1 to 12 ASCII digits. Every other query name and value is unavailable.
    /// </summary>
    public static string SafeQueryParameters(string? requestUri)
    {
        if (string.IsNullOrWhiteSpace(requestUri) ||
            !Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !IsHttp(uri))
            return NoSafeQueryParameters;

        var queryStart = requestUri.IndexOf('?');
        if (queryStart < 0 || queryStart == requestUri.Length - 1)
            return NoSafeQueryParameters;

        var fragmentStart = requestUri.IndexOf('#', queryStart + 1);
        var rawQuery = fragmentStart < 0
            ? requestUri.AsSpan(queryStart + 1)
            : requestUri.AsSpan(queryStart + 1, fragmentStart - queryStart - 1);

        string? value = null;
        var occurrences = 0;
        var query = rawQuery;

        while (!query.IsEmpty)
        {
            var separator = query.IndexOf('&');
            var pair = separator < 0 ? query : query[..separator];
            query = separator < 0 ? ReadOnlySpan<char>.Empty : query[(separator + 1)..];

            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            var candidate = equals < 0 ? ReadOnlySpan<char>.Empty : pair[(equals + 1)..];

            if (!name.Equals(SafeQueryName, StringComparison.OrdinalIgnoreCase))
                continue;

            occurrences++;
            if (occurrences > 1)
                return NoSafeQueryParameters;

            value = candidate.ToString();
        }

        if (value is null || value.Length is < 1 or > MaxSafeQueryValueLength)
            return NoSafeQueryParameters;

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
                return NoSafeQueryParameters;
        }

        return $"{SafeQueryName}={value}";
    }

    /// <summary>
    /// Replaces control characters with spaces and caps the retained length, so a value cannot forge additional log
    /// lines. The returned string contains only characters from the input plus the truncation marker.
    /// </summary>
    public static string Text(string? value, int maxLength = MaxTextLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0)
            return string.Empty;

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        var index = 0;

        for (; index < value.Length && builder.Length < maxLength; index++)
        {
            var character = value[index];
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        if (index < value.Length)
            builder.Append(TruncatedMarker);

        return builder.ToString();
    }

    /// <summary>
    /// Erases a classified value with the compliance <see cref="ErasingRedactor"/>. An empty redaction is represented
    /// by a stable marker so redacted members stay visible as redacted in diagnostics.
    /// </summary>
    public static string Erase(string? value)
    {
        if (value is null)
            return RedactedMarker;

        try
        {
            var redacted = ErasingRedactor.Instance.Redact(value);
            return string.IsNullOrEmpty(redacted) ? RedactedMarker : redacted;
        }
        catch
        {
            return RedactedMarker;
        }
    }

    /// <summary>
    /// Erases a user/account context identifier with the same compliance redactor used for response members. Every
    /// identifier becomes the same constant marker: the field stays visible, but the value is gone and events from
    /// different users cannot be correlated through it.
    /// </summary>
    public static string ContextId(ulong value)
    {
        return Erase(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Returns only the exception type name; messages, stack traces and inner exceptions are never retained.
    /// </summary>
    public static string ExceptionType(Exception? exception)
    {
        return exception is null ? UnavailableMarker : Text(exception.GetType().Name, MaxExceptionTypeLength);
    }

    /// <summary>
    /// Renders a known response model to a bounded sanitized JSON string. Opaque or unmodeled bodies stay omitted.
    /// </summary>
    public static string RenderBody(object? model)
    {
        return model is null ? OmittedMarker : GameApiLogBodyRenderer.Render(model);
    }

    /// <summary>
    /// Builds a stable marker for a diagnostic body that could not be read or rendered.
    /// </summary>
    public static string Unavailable(string? reason)
    {
        return string.IsNullOrEmpty(reason)
            ? UnavailableMarker
            : $"<unavailable:{Text(reason, 64)}>";
    }

    /// <summary>
    /// Logs an endpoint failure with a scalar exception type instead of the exception object. The bot user id is
    /// erased with <see cref="ContextId"/> before it reaches <paramref name="logger"/>.
    /// </summary>
    public static void LogException(ILogger logger, string? requestUri, ulong userId, Exception exception)
    {
        logger.LogError(LogMessages.ExceptionOccurred, Endpoint(requestUri), ContextId(userId),
            ExceptionType(exception));
    }

    private static bool IsHttp(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
