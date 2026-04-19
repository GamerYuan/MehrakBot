using Mehrak.Dashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize(Policy = "RequireSuperAdmin")]
[Route("admin/seaweed-filer")]
public sealed class SeaweedFilerProxyController : ControllerBase
{
    private static readonly HashSet<string> ReservedApiPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "alias",
        "attachments",
        "auth",
        "characters",
        "codes",
        "genshin",
        "healthz",
        "hi3",
        "hsr",
        "profile-auth",
        "users",
        "zzz"
    };

    private static readonly HashSet<string> SkippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Authorization",
        "Content-Length",
        "Cookie",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
    };

    private static readonly HashSet<string> SkippedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Set-Cookie"
    };

    private const string ProxyBasePath = "/admin/seaweed-filer";
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly SeaweedFilerOptions m_Options;
    private readonly ILogger<SeaweedFilerProxyController> m_Logger;

    public SeaweedFilerProxyController(
        IHttpClientFactory httpClientFactory,
        IOptions<SeaweedFilerOptions> options,
        ILogger<SeaweedFilerProxyController> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Options = options.Value;
        m_Logger = logger;
    }

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    [Route("")]
    [Route("{**path}")]
    public Task<IActionResult> Proxy(string? path, CancellationToken cancellationToken)
    {
        return ProxyCurrentRequest(cancellationToken);
    }

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    [Route("~/{**path}")]
    public Task<IActionResult> ProxyFallback(string? path, CancellationToken cancellationToken)
    {
        if (IsReservedApiPath(Request.Path))
            return Task.FromResult<IActionResult>(NotFound());

        return ProxyCurrentRequest(cancellationToken);
    }

    private async Task<IActionResult> ProxyCurrentRequest(CancellationToken cancellationToken)
    {
        if (!TryCreateBaseUri(out var baseUri))
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Seaweed filer proxy is not configured.");

        var targetUri = BuildTargetUri(baseUri, Request.Path, Request.QueryString);
        if (targetUri is null)
            return BadRequest(new { error = "Invalid filer path." });

        using var proxyRequest = CreateProxyRequest(targetUri);
        AddForwardingHeaders(proxyRequest);

        var client = m_HttpClientFactory.CreateClient("SeaweedFilerProxy");

        try
        {
            using var proxyResponse = await client.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            Response.StatusCode = (int)proxyResponse.StatusCode;

            CopyResponseHeaders(proxyResponse);

            await proxyResponse.Content.CopyToAsync(Response.Body, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var sanitised = Request.Path.Value?.ReplaceLineEndings(" ") ?? string.Empty;
            m_Logger.LogError(ex, "Seaweed filer proxy request failed for {Method} {Path}", Request.Method, sanitised);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Unable to reach Seaweed filer." });
        }

        return new EmptyResult();
    }

    private HttpRequestMessage CreateProxyRequest(Uri targetUri)
    {
        var requestMessage = new HttpRequestMessage(new HttpMethod(Request.Method), targetUri);

        if (Request.ContentLength is > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
            requestMessage.Content = new StreamContent(Request.Body);

        foreach (var header in Request.Headers)
        {
            if (SkippedRequestHeaders.Contains(header.Key))
                continue;

            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable()))
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
        }

        return requestMessage;
    }

    private static Uri? BuildTargetUri(Uri baseUri, PathString requestPath, QueryString queryString)
    {
        var pathValue = requestPath.Value ?? string.Empty;
        var trimmed = pathValue.StartsWith(ProxyBasePath, StringComparison.OrdinalIgnoreCase)
            ? pathValue[ProxyBasePath.Length..]
            : pathValue;

        var relativePath = trimmed.TrimStart('/');
        var relative = relativePath;

        if (queryString.HasValue)
            relative += queryString.Value;

        if (string.IsNullOrEmpty(relative))
            return baseUri;

        if (!Uri.TryCreate(baseUri, relative, out var targetUri))
            return null;

        if (!Uri.Compare(baseUri, targetUri, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase)
                .Equals(0))
            return null;

        return targetUri;
    }

    private static bool IsReservedApiPath(PathString requestPath)
    {
        var pathValue = requestPath.Value?.Trim('/');
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        var separatorIndex = pathValue.IndexOf('/');
        var firstSegment = separatorIndex >= 0 ? pathValue[..separatorIndex] : pathValue;

        return ReservedApiPrefixes.Contains(firstSegment);
    }

    private bool TryCreateBaseUri(out Uri baseUri)
    {
        var configured = m_Options.BaseUrl?.Trim();

        if (string.IsNullOrWhiteSpace(configured))
        {
            m_Logger.LogError("SeaweedFiler:BaseUrl is missing.");
            baseUri = default!;
            return false;
        }

        if (!configured.EndsWith('/'))
            configured += "/";

        if (!Uri.TryCreate(configured, UriKind.Absolute, out baseUri!))
        {
            m_Logger.LogError("SeaweedFiler:BaseUrl is invalid: {BaseUrl}", configured);
            return false;
        }

        return true;
    }

    private void AddForwardingHeaders(HttpRequestMessage requestMessage)
    {
        var remoteAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteAddress))
            requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-For", remoteAddress);

        requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Proto", Request.Scheme);
        requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Host", Request.Host.Value);
    }

    private void CopyResponseHeaders(HttpResponseMessage proxyResponse)
    {
        foreach (var header in proxyResponse.Headers)
        {
            if (SkippedResponseHeaders.Contains(header.Key))
                continue;

            Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in proxyResponse.Content.Headers)
        {
            if (SkippedResponseHeaders.Contains(header.Key))
                continue;

            Response.Headers[header.Key] = header.Value.ToArray();
        }

        Response.Headers.Remove("transfer-encoding");
    }
}
