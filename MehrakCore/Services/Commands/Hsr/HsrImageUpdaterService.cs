#region

using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

public class HsrImageUpdaterService : ImageUpdaterService<HsrCharacterInformation>
{
    private const string BaseString = "hsr_{0}";
    private const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/hsr/wapi/entry_page";

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
        var wikiArr = wiki.ToArray();
        var equipWiki = wikiArr[0];
        var relicWiki = wikiArr[1];

        Logger.LogInformation("Updating HSR character data for {CharacterName}", characterInformation.Name);
        var result = await Task.WhenAll(UpdateCharacterImageAsync(characterInformation),
            UpdateEquipImageAsync(characterInformation.Equip, equipWiki[characterInformation.Equip.Id.ToString()!]),
            UpdateSkillImageAsync(characterInformation.Skills), UpdateRankImageAsync(characterInformation.Ranks),
            UpdateRelicImageAsync(characterInformation.Relics.Concat(characterInformation.Ornaments), relicWiki));

        if (!result.All(x => x))
        {
            Logger.LogWarning("Some images failed to update for character {CharacterName}",
                characterInformation.Name);
            throw new InvalidOperationException(
                "Failed to update some images for the character. Check logs for details.");
        }

        Logger.LogInformation("Successfully updated HSR character data for {CharacterName}",
            characterInformation.Name);
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
            var client = HttpClientFactory.CreateClient();
            var imageResponse = await client.GetAsync(characterInformation.Image);

            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning(
                    "Failed to download image for character {CharacterName} with {Filename}: {StatusCode}",
                    characterInformation.Name, filename, imageResponse.StatusCode);
                return false;
            }

            var imageStream = await imageResponse.Content.ReadAsStreamAsync();
            await ImageRepository.UploadFileAsync(filename, imageStream);
            return true;
        }
        catch (HttpRequestException e)
        {
            Logger.LogError("Error downloading character image for {CharacterName} with {Filename}: {Message}",
                characterInformation.Name, filename, e.Message);
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating character data for {CharacterName} with {Filename}: {Message}",
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
            var client = HttpClientFactory.CreateClient();
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

            var imageStream = await imageResponse.Content.ReadAsStreamAsync();
            await ImageRepository.UploadFileAsync(filename, imageStream);
            return true;
        }
        catch (HttpRequestException e)
        {
            Logger.LogError("Error downloading equip image for {EquipName} with {Filename}: {Message}", equip.Name,
                filename, e.Message);
            throw;
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
            var client = HttpClientFactory.CreateClient();

            return await skills.ToAsyncEnumerable().SelectAwait(async skill =>
            {
                var filename = string.Format(BaseString, skill.PointId);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogInformation(
                        "Skill image for {SkillName} with {Filename} already exists, skipping download",
                        skill.SkillStages[0].Name, filename);
                    return true;
                }

                Logger.LogInformation("Downloading skill image for {SkillName} with {Filename}",
                    skill.SkillStages[0].Name, filename);

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

                var imageStream = await imageResponse.Content.ReadAsStreamAsync();
                await ImageRepository.UploadFileAsync(filename, imageStream);

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
        var client = HttpClientFactory.CreateClient();
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

                var imageStream = await imageResponse.Content.ReadAsStreamAsync();
                await ImageRepository.UploadFileAsync(filename, imageStream);

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
            var relicsList = relics.Where(r => relicWikiPages.ContainsKey(r.Id.ToString() ?? string.Empty)).ToList();

            // First check which relics already exist in the repository
            var relicsToDownload = new List<Relic>();
            foreach (var relic in relicsList)
            {
                var filename = string.Format(BaseString, relic.Id);
                if (!await ImageRepository.FileExistsAsync(filename))
                    relicsToDownload.Add(relic);
                else
                    Logger.LogInformation(
                        "Relic image for {RelicName} with ID {RelicId} already exists, skipping download",
                        relic.Name, relic.Id);
            }

            // If all relics already exist, return success
            if (relicsToDownload.Count == 0)
            {
                Logger.LogInformation("All relic images already exist, skipping wiki page fetching");
                return true;
            }

            // Group relics that need downloading by their wiki page
            var relicsByWikiPage = relicsToDownload
                .GroupBy(r => relicWikiPages[r.Id.ToString() ?? string.Empty]);

            var client = HttpClientFactory.CreateClient();
            var overallSuccess = true;

            // Create a dictionary to map wiki relic names to our relic IDs for correct file naming
            var relicNameToIdMap = relicsList.ToDictionary(
                r => r.Name.ToLowerInvariant(),
                r => r.Id.ToString());

            foreach (var wikiGroup in relicsByWikiPage)
            {
                var wikiPageUrl = wikiGroup.Key;
                var wikiPageId = wikiPageUrl.Split('/')[^1];
                Logger.LogInformation("Fetching wiki data for page ID {WikiPageId}", wikiPageId);

                // Make a single request for this wiki page
                var request = new HttpRequestMessage(HttpMethod.Get, $"{WikiApi}?entry_page_id={wikiPageId}");
                request.Headers.Add("X-Rpc-Wiki_app", "hsr");
                var wikiResponse = await client.SendAsync(request);
                var wikiJson = await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync());

                if (wikiJson == null)
                {
                    Logger.LogWarning("Failed to parse relic wiki JSON for page ID {WikiPageId}", wikiPageId);
                    overallSuccess = false;
                    continue;
                }

                var wikiModules = wikiJson["data"]?["page"]?["modules"]?.AsArray();
                var setEntry = wikiModules?.FirstOrDefault(x => x?["name"]?.GetValue<string>() == "Set");

                if (setEntry == null)
                {
                    Logger.LogWarning("No set entry found for wiki page ID {WikiPageId}", wikiPageId);
                    overallSuccess = false;
                    continue;
                }

                var wikiEntry = setEntry["components"]?.AsArray().First()?["data"]?.GetValue<string>();

                if (string.IsNullOrEmpty(wikiEntry))
                {
                    Logger.LogWarning("No wiki entry found for wiki page ID {WikiPageId}", wikiPageId);
                    overallSuccess = false;
                    continue;
                }

                // Parse the JSON response
                var jsonObject = JsonNode.Parse(wikiEntry);
                var relicList = jsonObject?["list"]?.AsArray();

                if (relicList == null || relicList.Count == 0)
                {
                    Logger.LogWarning("No relic list found for wiki page ID {WikiPageId}", wikiPageId);
                    overallSuccess = false;
                    continue;
                }

                Logger.LogInformation("Found {Count} relics in set with page ID {WikiPageId}", relicList.Count,
                    wikiPageId);

                // Process each relic in this wiki page group
                foreach (var relicNode in relicList)
                {
                    var relicName = relicNode?["title"]?.GetValue<string>();
                    var wikiRelicId = relicNode?["id"]?.GetValue<string>();
                    var iconUrl = relicNode?["icon_url"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(relicName) || string.IsNullOrEmpty(wikiRelicId) ||
                        string.IsNullOrEmpty(iconUrl))
                    {
                        Logger.LogWarning("Missing data for relic in set {WikiPageId}", wikiPageId);
                        overallSuccess = false;
                        continue;
                    }

                    // Try to match with our original relics by name
                    string actualRelicId;
                    if (relicNameToIdMap.TryGetValue(relicName.ToLowerInvariant(), out var matchedId))
                    {
                        // Use our original relic ID if we have a match
                        actualRelicId = matchedId!;
                        Logger.LogInformation("Matched wiki relic {RelicName} to relic ID {RelicId}", relicName,
                            actualRelicId);
                    }
                    else
                    {
                        // Use the wiki ID if no match is found
                        actualRelicId = wikiRelicId;
                        Logger.LogInformation("No match found for wiki relic {RelicName}, using wiki ID {RelicId}",
                            relicName, actualRelicId);
                    }

                    var filename = string.Format(BaseString, actualRelicId);

                    // Skip if already exists
                    if (await ImageRepository.FileExistsAsync(filename))
                    {
                        Logger.LogInformation(
                            "Relic image for {RelicName} with ID {RelicId} already exists, skipping download",
                            relicName, actualRelicId);
                        continue;
                    }

                    // Download and upload the image
                    try
                    {
                        Logger.LogInformation("Downloading relic image for {RelicName} with ID {RelicId}", relicName,
                            actualRelicId);
                        var imageResponse = await client.GetAsync(iconUrl);

                        if (!imageResponse.IsSuccessStatusCode)
                        {
                            Logger.LogWarning("Failed to download image for relic {RelicName}: {StatusCode}",
                                relicName, imageResponse.StatusCode);
                            overallSuccess = false;
                            continue;
                        }

                        var imageStream = await imageResponse.Content.ReadAsStreamAsync();
                        await ImageRepository.UploadFileAsync(filename, imageStream);

                        Logger.LogInformation("Successfully processed relic image for {RelicName}", relicName);
                    }
                    catch (HttpRequestException e)
                    {
                        Logger.LogError("Error downloading relic image for {RelicName} with {Filename}: {Message}",
                            relicName, filename, e.Message);
                        overallSuccess = false;
                    }
                }
            }

            return overallSuccess;
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating relic data: {Message}", e.Message);
            throw;
        }
    }
}
