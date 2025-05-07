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
                var characterImageFilename = string.Format(m_BaseString, id.Value);
                if (!await ImageRepository.FileExistsAsync(characterImageFilename))
                {
                    Logger.LogInformation(
                        "Character image for {CharacterName}, ID: {CharacterId} not found. Scheduling download.",
                        characterInformation.Base.Name, id.Value);
                    tasks.Add(UpdateCharacterImageTask(characterInformation.Base));
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
                var weaponImageFilename = string.Format(m_BaseString, weaponId.Value);
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
        Logger.LogDebug("Updating weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
            weaponDetail.Name, weaponDetail.Id.Value, weaponDetail.Icon);
        try
        {
            var response = await HttpClientFactory.CreateClient().GetAsync(weaponDetail.Icon);
            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
            Logger.LogDebug(
                "Uploading weapon icon for weapon {WeaponName}, ID: {WeaponId} with content type {ContentType}",
                weaponDetail.Name, weaponDetail.Id.Value, contentType);
            return await ImageRepository.UploadFileAsync(filename,
                await response.Content.ReadAsStreamAsync(),
                contentType);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error downloading weapon icon for weapon {WeaponName}, ID: {WeaponId} URL: {IconUrl}",
                weaponDetail.Name, weaponDetail.Id.Value, weaponDetail.Icon);
            return ObjectId.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing weapon icon for weapon {WeaponName}, ID: {WeaponId}",
                weaponDetail.Name, weaponDetail.Id.Value);
            return ObjectId.Empty;
        }
    }

    private IEnumerable<Task<ObjectId>> UpdateConstellationIconTasks(IEnumerable<Constellation> constellations)
    {
        return constellations.AsParallel()
            .Select(async constellation =>
            {
                var filename = string.Format(m_BaseString, constellation.Id!.Value);
                Logger.LogDebug(
                    "Updating constellation icon for constellation {ConstellationName}, ID: {ConstellationId} URL: {IconUrl}",
                    constellation.Name, constellation.Id, constellation.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(constellation.Icon);
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


    private IEnumerable<Task<ObjectId>> UpdateSkillIconTasks(IEnumerable<Skill> skills)
    {
        return skills.AsParallel()
            .Select(async skill =>
            {
                var filename = string.Format(m_BaseString, skill.SkillId!.Value);
                Logger.LogDebug("Updating skill icon for {SkillName}, ID {SkillId} URL: {IconUrl}", skill.Name,
                    skill.SkillId, skill.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(skill.Icon);
                    response.EnsureSuccessStatusCode();
                    var content = response.Content;
                    var contentType = content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug(
                        "Uploading skill icon for {SkillName}, ID {SkillId} with content type {ContentType}",
                        skill.Name, skill.SkillId, contentType);
                    return await ImageRepository.UploadFileAsync(filename,
                        await content.ReadAsStreamAsync(),
                        contentType);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex,
                        "Error downloading skill icon for skill {SkillName}, ID {SkillId} URL: {IconUrl}", skill.Name,
                        skill.SkillId,
                        skill.Icon);
                    return ObjectId.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "Error processing skill icon for skill {SkillName}, ID {SkillId}", skill.Name,
                        skill.SkillId);
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

            if (!Cache.Add(relic.Id.Value))
            {
                Logger.LogDebug("Relic {RelicName}, ID: {RelicId} found in cache. Skipping.", relic.Name,
                    relic.Id.Value);
                continue;
            }

            Logger.LogDebug("Relic {RelicName}, ID: {RelicId} added to cache.", relic.Name, relic.Id.Value);

            tasks.Add(Task.Run(async () => // Use Task.Run to allow async check inside loop
            {
                var filename = string.Format(m_BaseString, relic.Id!.Value);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogDebug("Relic {RelicName}, ID: {RelicId} already exists in database. Skipping.",
                        relic.Name, relic.Id.Value);
                    return ObjectId.Empty; // Indicate no upload needed
                }

                Logger.LogDebug("Updating relic icon for relic {RelicName}, ID: {RelicId} URL: {IconUrl}", relic.Name,
                    relic.Id.Value, relic.Icon);
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(relic.Icon);
                    response.EnsureSuccessStatusCode();
                    var content = response.Content;
                    var contentType = content.Headers.ContentType?.MediaType?.Split('/')[1] ?? "png";
                    Logger.LogDebug(
                        "Uploading relic {RelicName}, ID: {RelicId} with content type {ContentType} to database",
                        relic.Name, relic.Id.Value, contentType);
                    return await ImageRepository.UploadFileAsync(filename,
                        await content.ReadAsStreamAsync(),
                        contentType);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex,
                        "Error downloading relic icon for relic {RelicName}, ID: {RelicId} URL: {IconUrl}", relic.Name,
                        relic.Id,
                        relic.Icon);
                    return ObjectId.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing relic icon for relic {RelicName}, ID: {RelicId}", relic.Name,
                        relic.Id);
                    return ObjectId.Empty;
                }
            }));
        }

        return tasks;
    }
}
