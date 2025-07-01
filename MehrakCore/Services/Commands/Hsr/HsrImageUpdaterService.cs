#region

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

public partial class HsrImageUpdaterService : ImageUpdaterService<HsrCharacterInformation>
{
    private const string BaseString = "hsr_{0}";
    private const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/hsr/wapi/entry_page";

    private readonly ConcurrentDictionary<int, string> m_SetMapping = new();

    protected override string AvatarString => "hsr_avatar_{0}";

    public HsrImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<ImageUpdaterService<HsrCharacterInformation>> logger) : base(imageRepository, httpClientFactory, logger)
    {
    }

    /// <summary>
    /// Updates the character data for HSR using the provided information and wiki data
    /// </summary>
    /// <param name="characterInformation">The character information</param>
    /// <param name="wiki">The list of wiki. For Hsr, the first should be equipWiki and the second is relicWiki</param>
    /// <returns></returns>
    public override async Task UpdateDataAsync(HsrCharacterInformation characterInformation,
        IEnumerable<Dictionary<string, string>> wiki)
    {
        try
        {
            var wikiArr = wiki.ToArray();
            var equipWiki = wikiArr[0];
            var relicWiki = wikiArr[1];

            List<Task<bool>> tasks =
            [
                UpdateCharacterImageAsync(characterInformation),
                UpdateSkillImageAsync(
                    characterInformation.Skills!.Concat(characterInformation.ServantDetail!.ServantSkills!)),
                UpdateRankImageAsync(characterInformation.Ranks!),
                UpdateRelicImageAsync(characterInformation.Relics!.Concat(characterInformation.Ornaments!), relicWiki)
            ];

            if (characterInformation.Equip != null)
            {
                if (!equipWiki.TryGetValue(characterInformation.Equip.Id.ToString()!, out var equipWikiUrl))
                    Logger.LogWarning("No wiki URL found for equip {EquipName} ID {EquipId}",
                        characterInformation.Equip.Name, characterInformation.Equip.Id);
                else
                    tasks.Add(UpdateEquipImageAsync(characterInformation.Equip, equipWikiUrl));
            }

            Logger.LogInformation("Updating HSR character data for {CharacterName}", characterInformation.Name);
            var result = await Task.WhenAll(tasks);

            if (!result.All(x => x))
            {
                throw new CommandException("An error occurred while updating character image");
            }

            Logger.LogInformation("Successfully updated HSR character data for {CharacterName}",
                characterInformation.Name);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "An error occurred while updating character image {Character}",
                characterInformation);
            throw new CommandException("An error occurred while updating character image", e);
        }
    }

    public string GetRelicSetName(int relicId)
    {
        return m_SetMapping.TryGetValue(relicId, out var setName) ? setName : string.Empty;
    }

    private async Task<bool> UpdateCharacterImageAsync(HsrCharacterInformation characterInformation)
    {
        var filename = string.Format(BaseString, characterInformation.Id);
        try
        {
            if (await ImageRepository.FileExistsAsync(filename))
            {
                Logger.LogInformation(
                    "Character image for {CharacterName} with {Filename} already exists, skipping download",
                    characterInformation.Name, filename);
                return true;
            }

            Logger.LogInformation("Downloading character image for {CharacterName} with {Filename}",
                characterInformation.Name, filename);
            var client = HttpClientFactory.CreateClient("Default");
            var imageResponse = await client.GetAsync(characterInformation.Image);

            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning(
                    "Failed to download image for character {CharacterName} with {Filename}: {StatusCode}",
                    characterInformation.Name, filename, imageResponse.StatusCode);
                return false;
            }

            await using var imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using var image = await Image.LoadAsync(imageStream);
            image.Mutate(x => x.Resize(1000, 0));
            using var processedStream = new MemoryStream();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;

            await ImageRepository.UploadFileAsync(filename, processedStream, "png");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating character image for {CharacterName} with {Filename}: {Message}",
                characterInformation.Name, filename, e.Message);
            throw;
        }
    }

    private async Task<bool> UpdateEquipImageAsync(Equip equip, string equipWiki)
    {
        var filename = string.Format(BaseString, equip.Id);
        try
        {
            if (await ImageRepository.FileExistsAsync(filename))
            {
                Logger.LogInformation(
                    "Equipment image for {Equipment} with {Filename} already exists, skipping download",
                    equip.Name, filename);
                return true;
            }

            Logger.LogInformation("Downloading equip image for {EquipName} with {Filename}", equip.Name, filename);
            var client = HttpClientFactory.CreateClient("Default");
            var request = new HttpRequestMessage(HttpMethod.Get, $"{WikiApi}?entry_page_id={equipWiki.Split('/')[^1]}");
            request.Headers.Add("X-Rpc-Wiki_app", "hsr");
            var wikiResponse = await client.SendAsync(request);
            var wikiJson = await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync());

            var iconUrl = wikiJson?["data"]?["page"]?["icon_url"]?.GetValue<string>();
            if (string.IsNullOrEmpty(iconUrl))
            {
                Logger.LogWarning("No icon URL found for equip {EquipName} with {Filename}", equip.Name, filename);
                return false;
            }

            var imageResponse = await client.GetAsync(iconUrl);
            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to download image for equip {EquipName} with {Filename}: {StatusCode}",
                    equip.Name, filename, imageResponse.StatusCode);
                return false;
            }

            await using var imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using var image = await Image.LoadAsync(imageStream);
            image.Mutate(x => x.Resize(300, 0));
            using var processedStream = new MemoryStream();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;

            await ImageRepository.UploadFileAsync(filename, processedStream, "png");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating equip data for {EquipName} with {Filename}: {Message}", equip.Name,
                filename, e.Message);
            throw;
        }
    }

    private async Task<bool> UpdateSkillImageAsync(IEnumerable<Skill> skills)
    {
        try
        {
            var client = HttpClientFactory.CreateClient("Default");

            return await skills.ToAsyncEnumerable().SelectAwait(async skill =>
            {
                var filename = string.Format(BaseString,
                    skill.PointType == 1 ? StatBonusRegex().Replace(skill.SkillStages![0].Name!, "") : skill.PointId);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogInformation(
                        "Skill image for {SkillName} with {Filename} already exists, skipping download",
                        skill.SkillStages![0].Name, filename);
                    return true;
                }

                Logger.LogInformation("Downloading skill image for {SkillName} with {Filename}",
                    skill.SkillStages![0].Name, filename);

                var iconUrl = skill.ItemUrl;
                if (string.IsNullOrEmpty(iconUrl))
                {
                    Logger.LogWarning("No icon URL found for skill {SkillName} with {Filename}",
                        skill.SkillStages[0].Name, filename);
                    return false;
                }

                var imageResponse = await client.GetAsync(iconUrl);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("Failed to download image for skill {SkillName} with {Filename}: {StatusCode}",
                        skill.SkillStages[0].Name, filename, imageResponse.StatusCode);
                    return false;
                }

                await using var imageStream = await imageResponse.Content.ReadAsStreamAsync();
                using var image = await Image.LoadAsync(imageStream);
                image.Mutate(x => x.Resize(skill.PointType == 1 ? 50 : 80, 0));
                using var processedStream = new MemoryStream();
                await image.SaveAsPngAsync(processedStream);
                processedStream.Position = 0;

                await ImageRepository.UploadFileAsync(filename, processedStream, "png");

                Logger.LogInformation("Successfully processed skill image for {SkillName}", skill.SkillStages[0].Name);
                return true;
            }).AllAsync(x => x);
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating skill data: {Message}", e.Message);
            throw;
        }
    }

    private async Task<bool> UpdateRankImageAsync(IEnumerable<Rank> ranks)
    {
        var client = HttpClientFactory.CreateClient("Default");
        try
        {
            return await ranks.ToAsyncEnumerable().SelectAwait(async rank =>
            {
                var filename = string.Format(BaseString, rank.Id);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogInformation(
                        "Rank image for {Rank} with {Filename} already exists, skipping download",
                        rank.Name, filename);
                    return true;
                }

                Logger.LogInformation("Downloading rank image for {RankName} with {Filename}", rank.Name, filename);

                var iconUrl = rank.Icon;
                if (string.IsNullOrEmpty(iconUrl))
                {
                    Logger.LogWarning("No icon URL found for rank {RankName} with {Filename}", rank.Name, filename);
                    return false;
                }

                var imageResponse = await client.GetAsync(iconUrl);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("Failed to download image for rank {RankName} with {Filename}: {StatusCode}",
                        rank.Name, filename, imageResponse.StatusCode);
                    return false;
                }

                await using var imageStream = await imageResponse.Content.ReadAsStreamAsync();
                using var image = await Image.LoadAsync(imageStream);
                image.Mutate(x => x.Resize(80, 0));
                using var processedStream = new MemoryStream();
                await image.SaveAsPngAsync(processedStream);
                processedStream.Position = 0;

                await ImageRepository.UploadFileAsync(filename, processedStream, "png");

                Logger.LogInformation("Successfully processed rank image for {RankName}", rank.Name);
                return true;
            }).AllAsync(x => x);
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating rank data: {Message}", e.Message);
            throw new InvalidOperationException("Failed to update rank data", e);
        }
    }

    private async Task<bool> UpdateRelicImageAsync(IEnumerable<Relic> relics, Dictionary<string, string> relicWikiPages)
    {
        try
        {
            var allRelics = relics.ToList();
            var (relicsInWiki, relicsNotInWiki) = SeparateRelicsByWikiAvailability(allRelics, relicWikiPages);

            var client = HttpClientFactory.CreateClient("Default");
            var overallSuccess = true;

            // Process relics that have wiki entries
            if (relicsInWiki.Count != 0)
            {
                var success = await ProcessRelicsWithWikiData(relicsInWiki, relicWikiPages);
                overallSuccess = overallSuccess && success;
            } // Process relics without wiki entries using direct icon URLs

            if (relicsNotInWiki.Count != 0)
            {
                Logger.LogInformation("Processing {Count} relics not found in wiki pages", relicsNotInWiki.Count);

                var success = await ProcessRelicsWithDirectIcons(relicsNotInWiki, client);
                overallSuccess = overallSuccess && success;
            }

            return overallSuccess;
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating relic data: {Message}", e.Message);
            throw;
        }
    }

    private static (List<Relic> RelicsInWiki, List<Relic> RelicsNotInWiki) SeparateRelicsByWikiAvailability(
        List<Relic> allRelics, Dictionary<string, string> relicWikiPages)
    {
        var relicsInWiki = allRelics
            .Where(r => relicWikiPages.ContainsKey(r.Id.ToString() ?? string.Empty))
            .ToList();

        var relicsNotInWiki = allRelics
            .Where(r => !relicWikiPages.ContainsKey(r.Id.ToString() ?? string.Empty))
            .ToList();

        return (relicsInWiki, relicsNotInWiki);
    }

    private async Task<bool> ProcessRelicsWithWikiData(List<Relic> relics, Dictionary<string, string> relicWikiPages)
    {
        var relicsByWikiPage = relics.GroupBy(r => relicWikiPages[r.Id.ToString() ?? string.Empty]);
        var relicNameToIdMap = relics.ToDictionary(r => r.Name!.ToLowerInvariant(), r => r.Id.ToString());
        var overallSuccess = true;

        foreach (var wikiGroup in relicsByWikiPage)
        {
            var client = HttpClientFactory.CreateClient("Default");
            var wikiPageUrl = wikiGroup.Key;
            var wikiPageId = wikiPageUrl.Split('/')[^1];
            Logger.LogInformation("Fetching wiki data for page ID {WikiPageId}", wikiPageId);

            var wikiData = await FetchWikiDataAsync(client, wikiPageId);
            if (wikiData == null)
            {
                // Fallback to direct icons for this group
                var success = await ProcessRelicsWithDirectIcons(wikiGroup.ToList(), client);
                overallSuccess = overallSuccess && success;
                continue;
            }

            var (setName, relicList) = wikiData.Value;
            if (relicList == null || relicList.Count == 0)
            {
                Logger.LogWarning("No relic list found for wiki page ID {WikiPageId}, falling back to direct icons",
                    wikiPageId);
                var success = await ProcessRelicsWithDirectIcons(wikiGroup.ToList(), client, setName);
                overallSuccess = overallSuccess && success;
                continue;
            }

            Logger.LogInformation("Found {Count} relics in set with page ID {WikiPageId}", relicList.Count, wikiPageId);

            var success2 = await ProcessWikiRelicList(relicList, setName, relicNameToIdMap, client);
            overallSuccess = overallSuccess && success2;
        }

        return overallSuccess;
    }

    private async Task<(string SetName, JsonArray? RelicList)?> FetchWikiDataAsync(HttpClient client, string wikiPageId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{WikiApi}?entry_page_id={wikiPageId}");
            request.Headers.Add("X-Rpc-Wiki_app", "hsr");
            var wikiResponse = await client.SendAsync(request);
            var wikiJson = await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync());

            if (wikiJson == null)
            {
                Logger.LogWarning("Failed to parse relic wiki JSON for page ID {WikiPageId}", wikiPageId);
                return null;
            }

            var setName = wikiJson["data"]?["page"]?["name"]?.GetValue<string>();
            var wikiModules = wikiJson["data"]?["page"]?["modules"]?.AsArray();
            var setEntry = wikiModules?.FirstOrDefault(x => x?["name"]?.GetValue<string>() == "Set");

            if (setEntry == null)
            {
                Logger.LogWarning("No set entry found for wiki page ID {WikiPageId}", wikiPageId);
                return (setName ?? string.Empty, null);
            }

            var wikiEntry = setEntry["components"]?.AsArray().First()?["data"]?.GetValue<string>();
            if (string.IsNullOrEmpty(wikiEntry))
            {
                Logger.LogWarning("No wiki entry found for wiki page ID {WikiPageId}", wikiPageId);
                return (setName ?? string.Empty, null);
            }

            var jsonObject = JsonNode.Parse(wikiEntry);
            var relicList = jsonObject?["list"]?.AsArray();

            return (setName ?? string.Empty, relicList);
        }
        catch (Exception e)
        {
            Logger.LogError("Error fetching wiki data for page ID {WikiPageId}: {Message}", wikiPageId, e.Message);
            return null;
        }
    }

    private async Task<bool> ProcessWikiRelicList(JsonArray relicList, string setName,
        Dictionary<string, string?> relicNameToIdMap, HttpClient client)
    {
        var overallSuccess = true;

        foreach (var relicNode in relicList)
        {
            var relicName = relicNode?["title"]?.GetValue<string>();
            var wikiRelicId = relicNode?["id"]?.GetValue<string>();
            var iconUrl = relicNode?["icon_url"]?.GetValue<string>();

            if (string.IsNullOrEmpty(relicName) || string.IsNullOrEmpty(wikiRelicId) || string.IsNullOrEmpty(iconUrl))
            {
                Logger.LogWarning("Missing data for relic in set {SetName}", setName);
                overallSuccess = false;
                continue;
            }

            relicName = QuotationMarkRegex().Replace(relicName, "'"); // Normalize quotes

            if (!relicNameToIdMap.TryGetValue(relicName.ToLowerInvariant(), out var actualRelicId) ||
                actualRelicId == null)
            {
                Logger.LogInformation("No mapping found for relic {RelicName} in set {SetName}", relicName, setName);
                continue;
            }

            Logger.LogInformation("Matched wiki relic {RelicName} to relic ID {RelicId}", relicName, actualRelicId);

            // Add to set mapping regardless of download status
            var relicIdInt = int.Parse(actualRelicId);
            if (m_SetMapping.TryAdd(relicIdInt, setName))
                Logger.LogInformation("Added relic ID {RelicId} to set mapping with set name {SetName}", relicIdInt,
                    setName);

            var success = await DownloadAndSaveRelicImage(actualRelicId, iconUrl, relicName, client);
            overallSuccess = overallSuccess && success;
        }

        return overallSuccess;
    }

    private async Task<bool> ProcessRelicsWithDirectIcons(List<Relic> relics, HttpClient client, string? setName = null)
    {
        var overallSuccess = true;

        foreach (var relic in relics)
        {
            if (string.IsNullOrEmpty(relic.Icon))
            {
                Logger.LogWarning("No icon URL found for relic {RelicName} with ID {RelicId}", relic.Name, relic.Id);
                overallSuccess = false;
                continue;
            }

            if (setName != null && m_SetMapping.TryAdd(relic.Id!.Value, setName))
                Logger.LogInformation("Added relic ID {RelicId} to set mapping with set name {SetName}",
                    relic.Id.Value, setName);

            var success = await DownloadAndSaveRelicImage(relic.Id.ToString()!, relic.Icon, relic.Name!, client,
                "from Icon URL");
            overallSuccess = overallSuccess && success;
        }

        return overallSuccess;
    }

    private async Task<bool> DownloadAndSaveRelicImage(string relicId, string iconUrl, string relicName,
        HttpClient client, string source = "")
    {
        var filename = string.Format(BaseString, relicId);

        // Skip if already exists
        if (await ImageRepository.FileExistsAsync(filename))
        {
            Logger.LogInformation("Relic image for {RelicName} with ID {RelicId} already exists, skipping download",
                relicName, relicId);
            return true;
        }

        try
        {
            if (string.IsNullOrEmpty(source))
                Logger.LogInformation("Downloading relic image for {RelicName} with ID {RelicId}", relicName, relicId);
            else
                Logger.LogInformation("Downloading relic image {Source} for {RelicName} with ID {RelicId}", source,
                    relicName, relicId);

            var imageResponse = await client.GetAsync(iconUrl);

            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to download image for relic {RelicName}: {StatusCode}", relicName,
                    imageResponse.StatusCode);
                return false;
            }

            await using var imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using var image = await Image.LoadAsync(imageStream);
            image.Mutate(x =>
            {
                x.Resize(150, 0);
                x.ApplyGradientFade(0.5f);
            });
            using var processedStream = new MemoryStream();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;

            await ImageRepository.UploadFileAsync(filename, processedStream, "png");

            if (string.IsNullOrEmpty(source))
                Logger.LogInformation("Successfully processed relic image for {RelicName}", relicName);
            else
                Logger.LogInformation("Successfully processed relic image for {RelicName} {Source}", relicName, source);

            return true;
        }
        catch (Exception e)
        {
            Logger.LogError("Error downloading relic image for {RelicName} with {Filename}: {Message}", relicName,
                filename, e.Message);
            return false;
        }
    }

    [GeneratedRegex(@"\u2018|\u2019")]
    private static partial Regex QuotationMarkRegex();

    [GeneratedRegex(@"[\s:]")]
    public static partial Regex StatBonusRegex();
}
