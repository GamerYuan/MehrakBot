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
            UpdateEquipImageAsync(characterInformation.Equip, equipWiki["equipWiki"]),
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
        var filename = string.Format(BaseString, characterInformation.Name);
        try
        {
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
            Logger.LogInformation("Downloading equip image for {EquipName} with {Filename}", equip.Name, filename);
            var client = HttpClientFactory.CreateClient();
            var wikiResponse = await client.GetAsync($"{WikiApi}?entry_page_id={equipWiki.Split('/')[^1]}");
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
            // Group relics by their wiki page to make only one request per wiki page
            var relicsByWikiPage = relics
                .Where(r => relicWikiPages.ContainsKey(r.Id.ToString() ?? string.Empty))
                .GroupBy(r => relicWikiPages[r.Id.ToString() ?? string.Empty]);

            var client = HttpClientFactory.CreateClient();

            return await relicsByWikiPage.ToAsyncEnumerable().SelectAwait(async wikiGroup =>
            {
                var wikiPageUrl = wikiGroup.Key;
                var wikiPageId = wikiPageUrl.Split('/')[^1];
                Logger.LogInformation("Fetching wiki data for page ID {WikiPageId}", wikiPageId);

                // Make a single request for this wiki page
                var wikiResponse = await client.GetAsync($"{WikiApi}?entry_page_id={wikiPageId}");
                var wikiJson = await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync());

                if (wikiJson == null)
                {
                    Logger.LogWarning("Failed to parse relic wiki JSON for page ID {WikiPageId}", wikiPageId);
                    return false;
                }

                var wikiModules = wikiJson["data"]?["modules"]?.AsArray();
                var setEntry = wikiModules?.FirstOrDefault(x => x?["name"]?.GetValue<string>() == "Set");

                if (setEntry == null)
                {
                    Logger.LogWarning("No set entry found for wiki page ID {WikiPageId}", wikiPageId);
                    return false;
                }

                var wikiEntry = setEntry["components"]?.AsArray().First()?.GetValue<string>();

                if (string.IsNullOrEmpty(wikiEntry))
                {
                    Logger.LogWarning("No wiki entry found for wiki page ID {WikiPageId}", wikiPageId);
                    return false;
                }

                // Parse the JSON response
                var jsonObject = JsonNode.Parse(wikiEntry);
                var relicList = jsonObject?["list"]?.AsArray();

                if (relicList == null)
                {
                    Logger.LogWarning("No relic list found for wiki page ID {WikiPageId}", wikiPageId);
                    return false;
                }

                var result = true;
                // Process each relic in this wiki page group
                foreach (var relic in wikiGroup)
                {
                    var filename = string.Format(BaseString, relic.Id);

                    // Find the matching relic by name in the wiki data
                    var matchingRelic =
                        relicList.FirstOrDefault(item => item?["title"]?.GetValue<string>() == relic.Name);

                    if (matchingRelic == null)
                    {
                        Logger.LogWarning("No matching relic found for {RelicName}", relic.Name);
                        result = false;
                        continue;
                    }

                    var iconUrl = matchingRelic["icon_url"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(iconUrl))
                    {
                        Logger.LogWarning("No icon URL found for relic {RelicName}", relic.Name);
                        result = false;
                        continue;
                    }

                    // Download and upload the image
                    try
                    {
                        var imageResponse = await client.GetAsync(iconUrl);

                        if (!imageResponse.IsSuccessStatusCode)
                        {
                            Logger.LogWarning("Failed to download image for relic {RelicName}: {StatusCode}",
                                relic.Name, imageResponse.StatusCode);
                            result = false;
                            continue;
                        }

                        var imageStream = await imageResponse.Content.ReadAsStreamAsync();
                        await ImageRepository.UploadFileAsync(filename, imageStream);

                        Logger.LogInformation("Successfully processed relic image for {RelicName}", relic.Name);
                    }
                    catch (HttpRequestException e)
                    {
                        Logger.LogError("Error downloading relic image for {RelicName} with {Filename}: {Message}",
                            relic.Name, filename, e.Message);
                        result = false;
                    }
                }

                return result;
            }).AllAsync(x => x);
        }
        catch (Exception e)
        {
            Logger.LogError("Error updating relic data: {Message}", e.Message);
            throw;
        }
    }
}
