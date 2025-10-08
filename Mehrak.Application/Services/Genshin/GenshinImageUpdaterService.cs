#region

using Mehrak.Domain.Common;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.Application.Services.Genshin;

public class GenshinImageUpdaterService : ImageUpdaterService<GenshinCharacterInformation>
{
    private const int StandardImageSize = 1280;
    private const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/genshin/wapi/entry_page";

    protected override string AvatarString => FileNameFormat.GenshinAvatarName;
    protected override string SideAvatarString => FileNameFormat.GenshinSideAvatarName;

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

            int? id = characterInformation.Base.Id;
            Dictionary<string, string> avatarWiki = wiki.First();

            if (id != null)
            {
                string characterImageFilename = string.Format(FileNameFormat.GenshinFileName, id.Value);
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

            int? weaponId = characterInformation.Base.Weapon.Id;

            if (weaponId != null)
            {
                Logger.LogDebug("Weapon ID {WeaponId} not in cache. Checking image existence.", weaponId.Value);
                string weaponImageFilename = string.Format(FileNameFormat.GenshinFileName, weaponId.Value);
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
            tasks.AddRange(UpdateSkillIconTasks(characterInformation.Base.Id!.Value, characterInformation.Skills));

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
            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get
            };
            request.Headers.Add("X-Rpc-Language", "zh-cn");
            request.RequestUri = new Uri($"{WikiApi}?entry_page_id={avatarId}");
            HttpResponseMessage wikiResponse = await client.SendAsync(request);
            string? avatarUrl =
                (await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync()))?["data"]?["page"]?
                ["header_img_url"]?.ToString();

            if (string.IsNullOrEmpty(avatarUrl))
            {
                Logger.LogError(
                    "Failed to retrieve avatar URL for character {CharacterName}, ID: {CharacterId}, Wiki Entry: {WikiEntry}",
                    characterDetail.Name, characterDetail.Id, avatarId);
                return;
            }

            string filename = string.Format(FileNameFormat.GenshinFileName, characterDetail.Id!.Value);
            Logger.LogDebug("Updating character image for ID {CharacterId} from URL: {ImageUrl}", characterDetail.Id,
                avatarUrl);

            HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(avatarUrl);
            response.EnsureSuccessStatusCode();
            string contentType =
                response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png"; // Default to png if null

            // Process the image
            await using Stream imageStream = await response.Content.ReadAsStreamAsync();
            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(imageStream);

            int minX = image.Width;
            int minY = image.Height;
            int maxX = -1;
            int maxY = -1;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

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
                Size size = ctx.GetCurrentSize();
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

            Logger.LogDebug("Image processed to standard size {Width}x{Height} with gradient fade applied",
                StandardImageSize, StandardImageSize);

            // Save processed image to memory stream for upload
            using MemoryStream processedImageStream = new();
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

    public virtual async Task UpdateWeaponImageTask(Weapon weapon)
    {
        string filename = string.Format(FileNameFormat.GenshinFileName, weapon.Id!.Value);
        if (await ImageRepository.FileExistsAsync(filename))
        {
            Logger.LogDebug("Weapon image {Filename} already exists. Skipping update.", filename);
            return;
        }

        Logger.LogDebug("Updating weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
            weapon.Name, weapon.Id.Value, weapon.Icon);
        try
        {
            HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(weapon.Icon);
            response.EnsureSuccessStatusCode();
            using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(200, 0, KnownResamplers.Bicubic));
            using MemoryStream processedImageStream = new();
            await image.SaveAsPngAsync(processedImageStream, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit8,
                ColorType = PngColorType.RgbWithAlpha
            });
            processedImageStream.Position = 0;
            Logger.LogDebug(
                "Uploading weapon icon for weapon {WeaponName}, ID: {WeaponId}", weapon.Name,
                weapon.Id.Value);
            await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error downloading weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
                weapon.Name, weapon.Id.Value, weapon.Icon);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing weapon icon for weapon {WeaponName}, ID: {WeaponId}",
                weapon.Name, weapon.Id.Value);
            throw;
        }
    }

    private async Task UpdateWeaponImageTask(WeaponDetail weaponDetail)
    {
        string filename = string.Format(FileNameFormat.GenshinFileName, weaponDetail.Id!.Value);
        Logger.LogDebug("Updating weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
            weaponDetail.Name, weaponDetail.Id.Value, weaponDetail.Icon);
        try
        {
            HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(weaponDetail.Icon);
            response.EnsureSuccessStatusCode();
            using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(200, 0, KnownResamplers.Bicubic));
            using MemoryStream processedImageStream = new();
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
                !await ImageRepository.FileExistsAsync(string.Format(FileNameFormat.GenshinFileName, constellation.Id!.Value)))
            .Select(async constellation =>
            {
                string filename = string.Format(FileNameFormat.GenshinFileName, constellation.Id!.Value);
                Logger.LogDebug(
                    "Updating constellation icon for constellation {ConstellationName}, ID: {ConstellationId} URL: {IconUrl}",
                    constellation.Name, constellation.Id, constellation.Icon);
                try
                {
                    HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(constellation.Icon);
                    response.EnsureSuccessStatusCode();
                    using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
                    image.Mutate(x => x.Resize(90, 0, KnownResamplers.Bicubic));
                    using MemoryStream processedImageStream = new();
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

    private Task UpdateSkillIconTasks(int avatarId, IEnumerable<Skill> skills)
    {
        return Task.WhenAll(skills.AsParallel().ToAsyncEnumerable()
            .WhereAwait(async skill =>
                !await ImageRepository.FileExistsAsync(string.Format(FileNameFormat.GenshinSkillName, avatarId, skill.SkillId!.Value)))
            .Select(async skill =>
            {
                string filename = string.Format(FileNameFormat.GenshinSkillName, avatarId, skill.SkillId!.Value);
                Logger.LogDebug("Updating skill icon for {SkillName}, ID {SkillId} URL: {IconUrl}", skill.Name,
                    skill.SkillId, skill.Icon);
                try
                {
                    HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(skill.Icon);
                    response.EnsureSuccessStatusCode();
                    using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
                    image.Mutate(x => x.Resize(100, 0, KnownResamplers.Bicubic));
                    using MemoryStream processedImageStream = new();
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
        List<Task> tasks = [];
        foreach (Relic relic in relics)
        {
            if (relic.Id == null)
            {
                Logger.LogWarning("Skipping relic with null ID: {RelicName}", relic.Name);
                continue;
            }

            tasks.Add(Task.Run(async () =>
            {
                string filename = string.Format(FileNameFormat.GenshinFileName, relic.Id!.Value);
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
                    HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(relic.Icon);
                    response.EnsureSuccessStatusCode();
                    using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
                    image.Mutate(ctx =>
                    {
                        ctx.Resize(300, 0, KnownResamplers.Lanczos3);
                        ctx.Pad(300, 300);
                        ctx.ApplyGradientFade(0.5f);
                    });
                    using MemoryStream processedImageStream = new();
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
