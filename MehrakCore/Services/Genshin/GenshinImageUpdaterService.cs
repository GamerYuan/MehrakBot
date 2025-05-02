#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinImageUpdaterService : ImageUpdaterService<GenshinCharacterInformation>
{
    private readonly string m_BaseString = "genshin_{0}";

    public GenshinImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<GenshinImageUpdaterService> logger) : base(
        imageRepository,
        httpClientFactory,
        logger)
    {
    }

    public override async Task UpdateDataAsync(GenshinCharacterInformation characterInformation)
    {
        Logger.LogInformation("Starting image update process for character ID: {CharacterId}",
            characterInformation.Base.Id);
        List<Task> tasks = new();

        var id = characterInformation.Base.Id;

        if (id != null)
        {
            if (Cache.Contains(id.Value))
            {
                Logger.LogDebug("Character ID {CharacterId} found in cache. Skipping image checks.", id.Value);
            }
            else
            {
                Logger.LogDebug("Character ID {CharacterId} not in cache. Checking image existence.", id.Value);
                var characterImageFilename = string.Format(m_BaseString, id.Value);
                if (!await ImageRepository.FileExistsAsync(characterImageFilename))
                {
                    Logger.LogInformation("Character image for ID {CharacterId} not found. Scheduling download.",
                        id.Value);
                    tasks.Add(UpdateCharacterImageTask(characterInformation.Base));
                    tasks.AddRange(UpdateConstellationIconTasks(characterInformation.Constellations));
                    tasks.AddRange(UpdateSkillIconTasks(characterInformation.Skills));
                }
                else
                {
                    Logger.LogDebug("Character image {Filename} already exists.", characterImageFilename);
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
                var weaponImageFilename = string.Format(m_BaseString, weaponId.Value);
                if (!await ImageRepository.FileExistsAsync(weaponImageFilename))
                {
                    Logger.LogInformation("Weapon image for ID {WeaponId} not found. Scheduling download.",
                        weaponId.Value);
                    tasks.Add(UpdateWeaponImageTask(characterInformation.Base.Weapon));
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

    private async Task<ObjectId> UpdateCharacterImageTask(BaseCharacterDetail characterDetail)
    {
        var filename = string.Format(m_BaseString, characterDetail.Id!.Value);
        Logger.LogDebug("Updating character image for ID {CharacterId} from URL: {ImageUrl}", characterDetail.Id,
            characterDetail.Image);
        try
        {
            var response = await HttpClientFactory.CreateClient().GetAsync(characterDetail.Image);
            response.EnsureSuccessStatusCode();
            var contentType =
                response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png"; // Default to png if null
            Logger.LogDebug("Uploading character image {Filename} with content type {ContentType}", filename,
                contentType);
            return await ImageRepository.UploadFileAsync(filename,
                await response.Content.ReadAsStreamAsync(),
                contentType);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error downloading character image for ID {CharacterId} from {ImageUrl}",
                characterDetail.Id, characterDetail.Image);
            return ObjectId.Empty; // Or rethrow, depending on desired error handling
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing character image for ID {CharacterId}", characterDetail.Id);
            return ObjectId.Empty;
        }
    }

    private async Task<ObjectId> UpdateWeaponImageTask(WeaponDetail weaponDetail)
    {
        var filename = string.Format(m_BaseString, weaponDetail.Id!.Value);
        Logger.LogDebug("Updating weapon icon for ID {WeaponId} from URL: {IconUrl}", weaponDetail.Id,
            weaponDetail.Icon);
        try
        {
            var response = await HttpClientFactory.CreateClient().GetAsync(weaponDetail.Icon);
            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
            Logger.LogDebug("Uploading weapon icon {Filename} with content type {ContentType}", filename, contentType);
            return await ImageRepository.UploadFileAsync(filename,
                await response.Content.ReadAsStreamAsync(),
                contentType);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error downloading weapon icon for ID {WeaponId} from {IconUrl}", weaponDetail.Id,
                weaponDetail.Icon);
            return ObjectId.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing weapon icon for ID {WeaponId}", weaponDetail.Id);
            return ObjectId.Empty;
        }
    }

    private IEnumerable<Task<ObjectId>> UpdateConstellationIconTasks(IEnumerable<Constellation> constellations)
    {
        return constellations.AsParallel()
            .Select(async constellation =>
            {
                var filename = string.Format(m_BaseString, constellation.Id!.Value);
                Logger.LogDebug("Updating constellation icon for ID {ConstellationId} from URL: {IconUrl}",
                    constellation.Id, constellation.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(constellation.Icon);
                    response.EnsureSuccessStatusCode();
                    var contentType = response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug("Uploading constellation icon {Filename} with content type {ContentType}", filename,
                        contentType);
                    return await ImageRepository.UploadFileAsync(filename,
                        await response.Content.ReadAsStreamAsync(),
                        contentType);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex, "Error downloading constellation icon for ID {ConstellationId} from {IconUrl}",
                        constellation.Id, constellation.Icon);
                    return ObjectId.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing constellation icon for ID {ConstellationId}",
                        constellation.Id);
                    return ObjectId.Empty;
                }
            });
    }


    private IEnumerable<Task<ObjectId>> UpdateSkillIconTasks(IEnumerable<Skill> skills)
    {
        return skills.AsParallel()
            .Select(async skill =>
            {
                var filename = string.Format(m_BaseString, skill.SkillId!.Value);
                Logger.LogDebug("Updating skill icon for ID {SkillId} from URL: {IconUrl}", skill.SkillId, skill.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(skill.Icon);
                    response.EnsureSuccessStatusCode();
                    var content = response.Content;
                    var contentType = content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug("Uploading skill icon {Filename} with content type {ContentType}", filename,
                        contentType);
                    return await ImageRepository.UploadFileAsync(filename,
                        await content.ReadAsStreamAsync(),
                        contentType);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex, "Error downloading skill icon for ID {SkillId} from {IconUrl}", skill.SkillId,
                        skill.Icon);
                    return ObjectId.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing skill icon for ID {SkillId}", skill.SkillId);
                    return ObjectId.Empty;
                }
            });
    }

    private IEnumerable<Task<ObjectId>> UpdateRelicIconTasks(IEnumerable<Relic> relics)
    {
        var tasks = new List<Task<ObjectId>>();
        foreach (var relic in relics)
        {
            if (relic.Id == null)
            {
                Logger.LogWarning("Skipping relic with null ID: {RelicName}", relic.Name);
                continue;
            }

            if (Cache.Contains(relic.Id.Value))
            {
                Logger.LogDebug("Relic ID {RelicId} found in cache. Skipping.", relic.Id.Value);
                continue;
            }

            Cache.Add(relic.Id.Value);
            Logger.LogDebug("Relic ID {RelicId} added to cache.", relic.Id.Value);

            tasks.Add(Task.Run(async () => // Use Task.Run to allow async check inside loop
            {
                var filename = string.Format(m_BaseString, relic.Id!.Value);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogDebug("Relic icon {Filename} already exists.", filename);
                    return ObjectId.Empty; // Indicate no upload needed
                }

                Logger.LogDebug("Updating relic icon for ID {RelicId} from URL: {IconUrl}", relic.Id, relic.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(relic.Icon);
                    response.EnsureSuccessStatusCode();
                    var content = response.Content;
                    var contentType = content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug("Uploading relic icon {Filename} with content type {ContentType}", filename,
                        contentType);
                    return await ImageRepository.UploadFileAsync(filename,
                        await content.ReadAsStreamAsync(),
                        contentType);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex, "Error downloading relic icon for ID {RelicId} from {IconUrl}", relic.Id,
                        relic.Icon);
                    return ObjectId.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing relic icon for ID {RelicId}", relic.Id);
                    return ObjectId.Empty;
                }
            }));
        }

        return tasks;
    }
}
