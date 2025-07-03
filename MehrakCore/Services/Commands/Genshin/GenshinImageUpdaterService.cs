#region

using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Commands.Genshin;

public class GenshinImageUpdaterService : ImageUpdaterService<GenshinCharacterInformation>
{
    private const string BaseString = "genshin_{0}";
    private const int StandardImageSize = 1280;
    private const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/genshin/wapi/entry_page";

    protected override string AvatarString => "genshin_avatar_{0}";
    protected override string SideAvatarString => "genshin_side_avatar_{0}";

    public GenshinImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<GenshinImageUpdaterService> logger) : base(
        imageRepository,
        httpClientFactory,
        logger)
    {
    }

    public override async Task UpdateDataAsync(GenshinCharacterInformation characterInformation,
        IEnumerable<Dictionary<string, string>> wiki)
    {
        try
        {
            Logger.LogInformation("Starting image update process for character {CharacterName}, ID: {CharacterId}",
                characterInformation.Base.Name, characterInformation.Base.Id);
            List<Task> tasks = [];

            var id = characterInformation.Base.Id;
            var avatarWiki = wiki.First();

            if (id != null)
            {
                var characterImageFilename = string.Format(BaseString, id.Value);
                if (!await ImageRepository.FileExistsAsync(characterImageFilename))
                {
                    Logger.LogInformation(
                        "Character image for {CharacterName}, ID: {CharacterId} not found. Scheduling download.",
                        characterInformation.Base.Name, id.Value);
                    tasks.Add(UpdateCharacterImageTask(characterInformation.Base,
                        int.Parse(avatarWiki.Values.First().Split('/')[^1])));
                }
                else
                {
                    Logger.LogDebug("Character image {CharacterName}, ID: {CharacterId} already exists.",
                        characterInformation.Base.Name, id.Value);
                }
            }
            else
            {
                Logger.LogWarning("Character information provided without a base ID.");
            }


            var weaponId = characterInformation.Base.Weapon.Id;

            if (weaponId != null)
            {
                Logger.LogDebug("Weapon ID {WeaponId} not in cache. Checking image existence.", weaponId.Value);
                var weaponImageFilename = string.Format(BaseString, weaponId.Value);
                if (!await ImageRepository.FileExistsAsync(weaponImageFilename))
                {
                    Logger.LogInformation("Weapon image for ID {WeaponId} not found. Scheduling download.",
                        weaponId.Value);
                    tasks.Add(UpdateWeaponImageTask(characterInformation.Weapon));
                }
                else
                {
                    Logger.LogDebug("Weapon image {Filename} already exists.", weaponImageFilename);
                }
            }
            else
            {
                Logger.LogWarning("Character information provided without a weapon ID.");
            }

            tasks.AddRange(UpdateConstellationIconTasks(characterInformation.Constellations));
            tasks.AddRange(UpdateSkillIconTasks(characterInformation.Skills));

            tasks.AddRange(UpdateRelicIconTasks(characterInformation.Relics));

            if (tasks.Count > 0)
            {
                Logger.LogInformation("Waiting for {TaskCount} image update tasks to complete.", tasks.Count);
                await Task.WhenAll(tasks);
                Logger.LogInformation("All image update tasks completed for character ID: {CharacterId}",
                    characterInformation.Base.Id);
            }
            else
            {
                Logger.LogInformation("No image updates required for character ID: {CharacterId}",
                    characterInformation.Base.Id);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating images for character {Character}", characterInformation);
            throw new CommandException("An error occurred while updating images for the character.", e);
        }
    }

    private async Task UpdateCharacterImageTask(BaseCharacterDetail characterDetail, int avatarId)
    {
        Logger.LogDebug("Updating character image for character {CharacterName}, ID {CharacterId}",
            characterDetail.Name, characterDetail.Id);

        try
        {
            var client = HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new();
            request.Method = HttpMethod.Get;
            request.Headers.Add("X-Rpc-Language", "zh-cn");
            request.RequestUri = new Uri($"{WikiApi}?entry_page_id={avatarId}");
            var wikiResponse = await client.SendAsync(request);
            var avatarUrl =
                (await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync()))?["data"]?["page"]?
                ["header_img_url"]?.ToString();

            if (string.IsNullOrEmpty(avatarUrl))
            {
                Logger.LogError(
                    "Failed to retrieve avatar URL for character {CharacterName}, ID: {CharacterId}, Wiki Entry: {WikiEntry}",
                    characterDetail.Name, characterDetail.Id, avatarId);
                return;
            }

            var filename = string.Format(BaseString, characterDetail.Id!.Value);
            Logger.LogDebug("Updating character image for ID {CharacterId} from URL: {ImageUrl}", characterDetail.Id,
                avatarUrl);

            var response = await HttpClientFactory.CreateClient("Default").GetAsync(avatarUrl);
            response.EnsureSuccessStatusCode();
            var contentType =
                response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png"; // Default to png if null

            // Process the image
            await using var imageStream = await response.Content.ReadAsStreamAsync();
            using var image = await Image.LoadAsync<Rgba32>(imageStream);

            int minX = image.Width;
            int minY = image.Height;
            int maxX = -1;
            int maxY = -1;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);

                    for (int x = 0; x < pixelRow.Length; x++)
                        if (pixelRow[x].A > 0)
                        {
                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                        }
                }
            });

            image.Mutate(ctx =>
            {
                ctx.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
                var size = ctx.GetCurrentSize();
                if (size.Width >= size.Height)
                    ctx.Resize(0,
                        (int)Math.Round(1280 * Math.Min(1.2 * size.Height / size.Width, 1f)),
                        KnownResamplers.Lanczos3);
                else
                    ctx.Resize(1400, 0, KnownResamplers.Lanczos3);

                size = ctx.GetCurrentSize();

                if (size.Width > StandardImageSize)
                    ctx.Crop(new Rectangle((size.Width - StandardImageSize) / 2, 0, StandardImageSize, size.Height));

                ctx.ApplyGradientFade();
            });

            Logger.LogDebug("Image processed to standard size {Size}x{Size} with gradient fade applied",
                StandardImageSize, StandardImageSize);

            // Save processed image to memory stream for upload
            using var processedImageStream = new
                MemoryStream();
            await image.SaveAsPngAsync(processedImageStream, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit8,
                ColorType = PngColorType.RgbWithAlpha
            });
            processedImageStream.Position = 0;

            Logger.LogDebug("Uploading processed character image {Filename} with content type {ContentType}", filename,
                contentType);
            await ImageRepository.UploadFileAsync(filename,
                processedImageStream, "png");
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error downloading character image for ID {CharacterId} from {ImageUrl}",
                characterDetail.Id, characterDetail.Image);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing character image for ID {CharacterId}", characterDetail.Id);
            throw;
        }
    }

    private async Task UpdateWeaponImageTask(WeaponDetail weaponDetail)
    {
        var filename = string.Format(BaseString, weaponDetail.Id!.Value);
        Logger.LogDebug("Updating weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
            weaponDetail.Name, weaponDetail.Id.Value, weaponDetail.Icon);
        try
        {
            var response = await HttpClientFactory.CreateClient("Default").GetAsync(weaponDetail.Icon);
            response.EnsureSuccessStatusCode();
            using var image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(200, 0, KnownResamplers.Bicubic));
            using var processedImageStream = new MemoryStream();
            await image.SaveAsPngAsync(processedImageStream, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit8,
                ColorType = PngColorType.RgbWithAlpha
            });
            processedImageStream.Position = 0;
            Logger.LogDebug(
                "Uploading weapon icon for weapon {WeaponName}, ID: {WeaponId}", weaponDetail.Name,
                weaponDetail.Id.Value);
            await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error downloading weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
                weaponDetail.Name, weaponDetail.Id.Value, weaponDetail.Icon);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing weapon icon for weapon {WeaponName}, ID: {WeaponId}",
                weaponDetail.Name, weaponDetail.Id.Value);
            throw;
        }
    }

    private Task UpdateConstellationIconTasks(IEnumerable<Constellation> constellations)
    {
        return Task.WhenAll(constellations.AsParallel().ToAsyncEnumerable()
            .WhereAwait(async constellation =>
                !await ImageRepository.FileExistsAsync(string.Format(BaseString, constellation.Id!.Value)))
            .Select(async constellation =>
            {
                var filename = string.Format(BaseString, constellation.Id!.Value);
                Logger.LogDebug(
                    "Updating constellation icon for constellation {ConstellationName}, ID: {ConstellationId} URL: {IconUrl}",
                    constellation.Name, constellation.Id, constellation.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient("Default").GetAsync(constellation.Icon);
                    response.EnsureSuccessStatusCode();
                    using var image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
                    image.Mutate(x => x.Resize(90, 0, KnownResamplers.Bicubic));
                    using var processedImageStream = new MemoryStream();
                    await image.SaveAsPngAsync(processedImageStream, new PngEncoder
                    {
                        BitDepth = PngBitDepth.Bit8,
                        ColorType = PngColorType.RgbWithAlpha
                    });
                    processedImageStream.Position = 0;
                    Logger.LogDebug(
                        "Uploading constellation icon for constellation {ConstellationName}, ID: {Filename}",
                        constellation.Name, constellation.Id.Value);
                    return await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex,
                        "Error downloading constellation icon for constellation {ConstellationName}, ID: {ConstellationId} URL: {IconUrl}",
                        constellation.Name, constellation.Id.Value, constellation.Icon);
                    return ObjectId.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "Error processing constellation icon for constellation {ConstellationName}, ID: {ConstellationId}",
                        constellation.Name, constellation.Id.Value);
                    return ObjectId.Empty;
                }
            }).ToEnumerable());
    }


    private Task UpdateSkillIconTasks(IEnumerable<Skill> skills)
    {
        return Task.WhenAll(skills.AsParallel().ToAsyncEnumerable()
            .WhereAwait(async skill =>
                !await ImageRepository.FileExistsAsync(string.Format(BaseString, skill.SkillId!.Value)))
            .Select(async skill =>
            {
                var filename = string.Format(BaseString, skill.SkillId!.Value);
                Logger.LogDebug("Updating skill icon for {SkillName}, ID {SkillId} URL: {IconUrl}", skill.Name,
                    skill.SkillId, skill.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient("Default").GetAsync(skill.Icon);
                    response.EnsureSuccessStatusCode();
                    using var image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
                    image.Mutate(x => x.Resize(100, 0, KnownResamplers.Bicubic));
                    using var processedImageStream = new MemoryStream();
                    await image.SaveAsPngAsync(processedImageStream, new PngEncoder
                    {
                        BitDepth = PngBitDepth.Bit8,
                        ColorType = PngColorType.RgbWithAlpha
                    });
                    processedImageStream.Position = 0;
                    Logger.LogDebug("Uploading skill icon for {SkillName}, ID {SkillId}", skill.Name, skill.SkillId);
                    await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex,
                        "Error downloading skill icon for skill {SkillName}, ID {SkillId} URL: {IconUrl}", skill.Name,
                        skill.SkillId,
                        skill.Icon);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "Error processing skill icon for skill {SkillName}, ID {SkillId}", skill.Name,
                        skill.SkillId);
                }
            }).ToEnumerable());
    }

    private Task UpdateRelicIconTasks(IEnumerable<Relic> relics)
    {
        var tasks = new List<Task>();
        foreach (var relic in relics)
        {
            if (relic.Id == null)
            {
                Logger.LogWarning("Skipping relic with null ID: {RelicName}", relic.Name);
                continue;
            }

            tasks.Add(Task.Run(async () =>
            {
                var filename = string.Format(BaseString, relic.Id!.Value);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogDebug("Relic {RelicName}, ID: {RelicId} already exists in database. Skipping.",
                        relic.Name, relic.Id.Value);
                    return;
                }

                Logger.LogDebug("Updating relic icon for relic {RelicName}, ID: {RelicId} URL: {IconUrl}", relic.Name,
                    relic.Id.Value, relic.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient("Default").GetAsync(relic.Icon);
                    response.EnsureSuccessStatusCode();
                    using var image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
                    image.Mutate(ctx =>
                    {
                        ctx.Resize(300, 0, KnownResamplers.Lanczos3);
                        ctx.Pad(300, 300);
                        ctx.ApplyGradientFade(0.5f);
                    });
                    using var processedImageStream = new MemoryStream();
                    await image.SaveAsPngAsync(processedImageStream, new PngEncoder
                    {
                        BitDepth = PngBitDepth.Bit8,
                        ColorType = PngColorType.RgbWithAlpha
                    });
                    processedImageStream.Position = 0;
                    Logger.LogDebug("Uploading relic icon for relic {RelicName}, ID: {RelicId}",
                        relic.Name, relic.Id.Value);
                    await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex,
                        "Error downloading relic icon for relic {RelicName}, ID: {RelicId} URL: {IconUrl}", relic.Name,
                        relic.Id,
                        relic.Icon);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing relic icon for relic {RelicName}, ID: {RelicId}", relic.Name,
                        relic.Id);
                }
            }));
        }

        return Task.WhenAll(tasks);
    }
}
