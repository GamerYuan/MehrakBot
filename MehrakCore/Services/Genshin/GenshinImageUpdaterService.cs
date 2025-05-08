#region

using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinImageUpdaterService : ImageUpdaterService<GenshinCharacterInformation>
{
    private const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/genshin/wapi/entry_page";
    private const string BaseString = "genshin_{0}";
    private const int StandardImageSize = 1280;

    public GenshinImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<GenshinImageUpdaterService> logger) : base(
        imageRepository,
        httpClientFactory,
        logger)
    {
    }

    public override async Task UpdateDataAsync(GenshinCharacterInformation characterInformation,
        IReadOnlyDictionary<string, string> avatarWiki)
    {
        Logger.LogInformation("Starting image update process for character {CharacterName}, ID: {CharacterId}",
            characterInformation.Base.Name, characterInformation.Base.Id);
        List<Task> tasks = [];

        var id = characterInformation.Base.Id;

        if (id != null)
        {
            if (Cache.Contains(id.Value))
            {
                Logger.LogDebug("Character {CharacterName}, ID: {CharacterId} found in cache. Skipping image checks.",
                    characterInformation.Base.Name, id.Value);
            }
            else
            {
                Logger.LogDebug("Character {CharacterName}, ID: {CharacterId} not in cache. Checking image existence.",
                    characterInformation.Base.Name, id.Value);
                var characterImageFilename = string.Format(BaseString, id.Value);
                if (!await ImageRepository.FileExistsAsync(characterImageFilename))
                {
                    Logger.LogInformation(
                        "Character image for {CharacterName}, ID: {CharacterId} not found. Scheduling download.",
                        characterInformation.Base.Name, id.Value);
                    tasks.Add(UpdateCharacterImageTask(characterInformation.Base,
                        int.Parse(avatarWiki.Values.First().Split('/')[^1])));
                    tasks.AddRange(UpdateConstellationIconTasks(characterInformation.Constellations));
                    tasks.AddRange(UpdateSkillIconTasks(characterInformation.Skills));
                }
                else
                {
                    Logger.LogDebug("Character image {CharacterName}, ID: {CharacterId} already exists.",
                        characterInformation.Base.Name, id.Value);
                }

                Cache.Add(id.Value);
            }
        }
        else
        {
            Logger.LogWarning("Character information provided without a base ID.");
        }


        var weaponId = characterInformation.Base.Weapon.Id;

        if (weaponId != null)
        {
            if (Cache.Contains(weaponId.Value))
            {
                Logger.LogDebug("Weapon ID {WeaponId} found in cache. Skipping image check.", weaponId.Value);
            }
            else
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

                Cache.Add(weaponId.Value);
            }
        }
        else
        {
            Logger.LogWarning("Character information provided without a weapon ID.");
        }


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

            // Step 1: Standardize image size to 1280x1280
            using var standardImage = ImageExtensions.StandardizeImageSize(image, 1280);

            // Step 2: Apply gradient fade
            standardImage.Mutate(ctx => ctx.ApplyGradientFade());

            Logger.LogDebug("Image processed to standard size {Size}x{Size} with gradient fade applied",
                StandardImageSize, StandardImageSize);

            // Save processed image to memory stream for upload
            using var processedImageStream = new MemoryStream();
            await standardImage.SaveAsync(processedImageStream,
                standardImage.Metadata.DecodedImageFormat ?? PngFormat.Instance);
            processedImageStream.Position = 0;

            Logger.LogDebug("Uploading processed character image {Filename} with content type {ContentType}", filename,
                contentType);
            await ImageRepository.UploadFileAsync(filename,
                processedImageStream,
                image.Metadata.DecodedImageFormat?.ToString() ?? "png");
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
            var contentType = response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
            Logger.LogDebug(
                "Uploading weapon icon for weapon {WeaponName}, ID: {WeaponId} with content type {ContentType}",
                weaponDetail.Name, weaponDetail.Id.Value, contentType);
            await ImageRepository.UploadFileAsync(filename,
                await response.Content.ReadAsStreamAsync(),
                contentType);
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

    private IEnumerable<Task> UpdateConstellationIconTasks(IEnumerable<Constellation> constellations)
    {
        return constellations.AsParallel()
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
                    var contentType = response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug(
                        "Uploading constellation icon for constellation {ConstellationName}, ID: {Filename} with content type {ContentType}",
                        constellation.Name, constellation.Id.Value, contentType);
                    return await ImageRepository.UploadFileAsync(filename,
                        await response.Content.ReadAsStreamAsync(),
                        contentType);
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
            });
    }


    private IEnumerable<Task> UpdateSkillIconTasks(IEnumerable<Skill> skills)
    {
        return skills.AsParallel()
            .Select(async skill =>
            {
                var filename = string.Format(BaseString, skill.SkillId!.Value);
                Logger.LogDebug("Updating skill icon for {SkillName}, ID {SkillId} URL: {IconUrl}", skill.Name,
                    skill.SkillId, skill.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient("Default").GetAsync(skill.Icon);
                    response.EnsureSuccessStatusCode();
                    var content = response.Content;
                    var contentType = content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug(
                        "Uploading skill icon for {SkillName}, ID {SkillId} with content type {ContentType}",
                        skill.Name, skill.SkillId, contentType);
                    await ImageRepository.UploadFileAsync(filename,
                        await content.ReadAsStreamAsync(),
                        contentType);
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
            });
    }

    private IEnumerable<Task> UpdateRelicIconTasks(IEnumerable<Relic> relics)
    {
        var tasks = new List<Task>();
        foreach (var relic in relics)
        {
            if (relic.Id == null)
            {
                Logger.LogWarning("Skipping relic with null ID: {RelicName}", relic.Name);
                continue;
            }

            if (!Cache.Add(relic.Id.Value))
            {
                Logger.LogDebug("Relic {RelicName}, ID: {RelicId} found in cache. Skipping.", relic.Name,
                    relic.Id.Value);
                continue;
            }

            Logger.LogDebug("Relic {RelicName}, ID: {RelicId} added to cache.", relic.Name, relic.Id.Value);

            tasks.Add(Task.Run(async () => // Use Task.Run to allow async check inside loop
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
                    var content = response.Content;
                    var contentType = content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug(
                        "Uploading relic {RelicName}, ID: {RelicId} with content type {ContentType} to database",
                        relic.Name, relic.Id.Value, contentType);
                    await ImageRepository.UploadFileAsync(filename,
                        await content.ReadAsStreamAsync(),
                        contentType);
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

        return tasks;
    }
}
