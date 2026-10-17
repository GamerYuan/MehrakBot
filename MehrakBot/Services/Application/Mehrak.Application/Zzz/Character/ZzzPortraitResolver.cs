using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Wiki;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Mehrak.Application.Zzz.Character;

internal sealed record ZzzPortraitUrls(string? Base, string? Outfit);

/// <summary>
/// Resolves full artwork against the game-record portrait crops, independently of
/// wiki labels, gallery ordering, and the locale in which an outfit was uploaded.
/// </summary>
internal sealed class ZzzPortraitResolver(
    IApiService<JsonNode, WikiApiContext> wikiApi,
    IImageFetcher imageFetcher,
    IPortraitMatcher portraitMatcher,
    ILogger logger)
{
    private static readonly TimeSpan MatchingTimeout = TimeSpan.FromMinutes(2);

    internal async Task<Result<ZzzPortraitUrls>> ResolveAsync(ulong userId, string entryPage,
        ZzzAvatarData avatar, bool needBase, bool needOutfit, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(MatchingTimeout);
        var token = timeoutCts.Token;
        var references = new Dictionary<string, byte[]?>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var imageHashes = new HashSet<string>(StringComparer.Ordinal);
        var bestStatus = StatusCode.ExternalServerError;
        string? baseUrl = null, outfitUrl = null;

        try
        {
            // The vertical painting follows the equipped outfit even when the square
            // avatar still shows the base outfit (as in the Yixuan fixture).
            var baseReferenceUrl = GetBaseReferenceUrl(avatar);
            var outfitReferenceUrl = avatar.GetPortraitSubId() is { } outfitId &&
                avatar.RoleVerticalPaintingUrl.Contains($"_{avatar.Id}_{outfitId}.", StringComparison.Ordinal)
                ? avatar.RoleVerticalPaintingUrl
                : avatar.RoleSquareUrl;
            var baseReference = needBase ? await FetchReferenceAsync(baseReferenceUrl) : null;
            var outfitReference = needOutfit ? await FetchReferenceAsync(outfitReferenceUrl) : null;

            foreach (var locale in Enum.GetValues<WikiLocales>())
            {
                token.ThrowIfCancellationRequested();
                var result = await wikiApi.GetAsync(new WikiApiContext(userId, Game.ZenlessZoneZero, entryPage, locale), token);
                if (!result.IsSuccess)
                {
                    if (result.StatusCode == StatusCode.Cancelled)
                        throw new OperationCanceledException(token);
                    if (result.StatusCode == StatusCode.Timeout)
                        bestStatus = StatusCode.Timeout;
                    continue;
                }

                var page = result.Data["data"]?["page"];
                // A wiki header is a fallback reference only for the base artwork.
                // It is never saved as a full portrait.
                if (needBase && baseReference == null)
                    baseReference = await FetchReferenceAsync(page?["header_img_url"]?.GetValue<string>());

                foreach (var url in GetGalleryUrls(page))
                {
                    if (!visited.Add(url))
                        continue;
                    var bytes = await imageFetcher.FetchBytesAsync(url, token);
                    if (bytes == null || !imageHashes.Add(Convert.ToHexString(SHA256.HashData(bytes))) ||
                        !IsPortraitCandidate(bytes))
                        continue;

                    if (needBase && baseUrl == null && baseReference != null &&
                        (await portraitMatcher.MatchAsync(baseReference, bytes, token)).IsMatch)
                        baseUrl = url;

                    if (needOutfit && outfitUrl == null && outfitReference != null &&
                        (await portraitMatcher.MatchAsync(outfitReference, bytes, token)).IsMatch)
                        outfitUrl = url;

                    if ((!needBase || baseUrl != null) && (!needOutfit || outfitUrl != null))
                        return Result<ZzzPortraitUrls>.Success(new(baseUrl, outfitUrl));
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            bestStatus = StatusCode.Timeout;
            logger.LogWarning("Portrait matching timed out for ZZZ character {CharacterId}", avatar.Id);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (baseUrl != null || outfitUrl != null)
            return Result<ZzzPortraitUrls>.Success(new(baseUrl, outfitUrl));

        return Result<ZzzPortraitUrls>.Failure(bestStatus, "No matching character portrait found");

        async Task<byte[]?> FetchReferenceAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            if (!references.TryGetValue(url, out var bytes))
            {
                bytes = await imageFetcher.FetchBytesAsync(url, token);
                references[url] = bytes;
            }
            return bytes;
        }
    }

    internal static string GetBaseReferenceUrl(ZzzAvatarData avatar)
    {
        // Game-record artwork URLs use _{avatarId}_{outfitId}.png for skins.
        return Regex.Replace(avatar.RoleVerticalPaintingUrl,
            $@"_{avatar.Id}_\d+(?=\.png(?:\?|$))", $"_{avatar.Id}", RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> GetGalleryUrls(JsonNode? page)
    {
        foreach (var component in page?["modules"]?.AsArray()
            .SelectMany(x => x?["components"]?.AsArray() ?? []) ?? [])
        {
            if (component?["component_id"]?.GetValue<string>() != "gallery_character")
                continue;
            JsonArray? entries;
            try
            {
                var data = component["data"]?.GetValue<string>();
                entries = string.IsNullOrWhiteSpace(data) ? null : JsonNode.Parse(data)?["list"]?.AsArray();
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            foreach (var entry in entries ?? [])
            {
                var url = entry?["img"]?.GetValue<string>();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    yield return url!;
            }
        }
    }

    internal static bool IsPortraitCandidate(byte[] bytes)
    {
        try
        {
            if (Image.DetectFormat(bytes) != PngFormat.Instance)
                return false;
            using var image = Image.Load<Rgba32>(bytes);
            long transparent = 0, visible = 0;
            var bottomVisible = 0;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var rowVisible = 0;
                    foreach (var pixel in accessor.GetRowSpan(y))
                    {
                        if (pixel.A < 16) transparent++;
                        if (pixel.A > 128) visible++;
                        if (pixel.A > 128) rowVisible++;
                    }
                    if (rowVisible > 0) bottomVisible = rowVisible;
                }
            });
            // Actual transparency, not merely an alpha channel. A broad solid
            // bottom edge of the visible content indicates a cropped header/thumbnail,
            // including headers with transparent padding below the crop.
            return transparent >= (long)image.Width * image.Height / 100 && visible > 0 &&
                bottomVisible <= image.Width * 0.2;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return false;
        }
    }
}
