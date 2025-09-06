#region

using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

public partial class HsrImageUpdaterService : ImageUpdaterService<HsrCharacterInformation>
{
    private const string BaseString = FileNameFormat.HsrFileName;
    private const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/hsr/wapi/entry_page";

    private readonly IRelicRepository<Relic> m_RelicRepository;

    protected override string AvatarString => FileNameFormat.HsrAvatarName;
    protected override string SideAvatarString => FileNameFormat.HsrSideAvatarName;

    public HsrImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        IRelicRepository<Relic> relicRepository, ILogger<HsrImageUpdaterService> logger)
        : base(imageRepository, httpClientFactory, logger)
    {
        m_RelicRepository = relicRepository;
    }

    /// <summary>
    /// Updates the character data for HSR using the provided information and
    /// wiki data
    /// </summary>
    /// <param name="characterInformation">The character information</param>
    /// <param name="wiki">
    /// The list of wiki. For Hsr, the first should be equipWiki and the second
    /// is relicWiki
    /// </param>
    /// <returns></returns>
    public override async Task UpdateDataAsync(HsrCharacterInformation characterInformation,
        IEnumerable<Dictionary<string, string>> wiki)
    {
        try
        {
            Dictionary<string, string>[] wikiArr = [.. wiki];
            Dictionary<string, string> equipWiki = wikiArr[0];
            Dictionary<string, string> relicWiki = wikiArr[1];

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
                if (!equipWiki.TryGetValue(characterInformation.Equip.Id.ToString()!, out string? equipWikiUrl))
                    Logger.LogWarning("No wiki URL found for equip {EquipName} ID {EquipId}",
                        characterInformation.Equip.Name, characterInformation.Equip.Id);
                else
                    tasks.Add(UpdateEquipImageAsync(characterInformation.Equip, equipWikiUrl));
            }

            Logger.LogInformation("Updating HSR character data for {CharacterName}", characterInformation.Name);
            bool[] result = await Task.WhenAll(tasks);

            if (!result.All(x => x)) throw new CommandException("An error occurred while updating character image");

            Logger.LogInformation("Successfully updated HSR character data for {CharacterName}",
                characterInformation.Name);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "An error occurred while updating character data {Character}",
                characterInformation);
            throw new CommandException("An error occurred while updating character image", e);
        }
    }

    public async Task UpdateEquipIconAsync(int id, string iconUrl)
    {
        string filename = string.Format(FileNameFormat.HsrWeaponIconName, id);
        try
        {
            if (await ImageRepository.FileExistsAsync(filename))
            {
                Logger.LogDebug("Equip image for ID {EquipId} already exists, skipping download", id);
                return;
            }

            Logger.LogInformation("Downloading equip icon for {EquipId} with {Filename}", id, filename);
            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpResponseMessage imageResponse = await client.GetAsync(iconUrl);

            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to download image for equip ID {EquipId} with {Filename}: {StatusCode}",
                    id, filename, imageResponse.StatusCode);
                throw new CommandException("An error occurred while updating light cone icon");
            }

            await using Stream imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using Image image = await Image.LoadAsync(imageStream);
            image.Mutate(x => x.Resize(150, 0));
            using MemoryStream processedStream = new();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;
            await ImageRepository.UploadFileAsync(filename, processedStream, "png");

            Logger.LogInformation("Successfully updated equip icon for ID {EquipId} with {Filename}", id, filename);
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating equip icon for ID {EquipId} with {Filename}", id, filename);
            throw new CommandException("An error occurred while updating light cone icon", e);
        }
    }

    private async Task<bool> UpdateCharacterImageAsync(HsrCharacterInformation characterInformation)
    {
        string filename = string.Format(BaseString, characterInformation.Id);
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
            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpResponseMessage imageResponse = await client.GetAsync(characterInformation.Image);

            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning(
                    "Failed to download image for character {CharacterName} with {Filename}: {StatusCode}",
                    characterInformation.Name, filename, imageResponse.StatusCode);
                return false;
            }

            await using Stream imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using Image image = await Image.LoadAsync(imageStream);
            image.Mutate(x => x.Resize(1000, 0));
            using MemoryStream processedStream = new();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;

            await ImageRepository.UploadFileAsync(filename, processedStream, "png");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating character image for {CharacterName} with {Filename}",
                characterInformation.Name, filename);
            throw;
        }
    }

    private async Task<bool> UpdateEquipImageAsync(Equip equip, string equipWiki)
    {
        string filename = string.Format(BaseString, equip.Id);
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
            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, $"{WikiApi}?entry_page_id={equipWiki.Split('/')[^1]}");
            request.Headers.Add("X-Rpc-Wiki_app", "hsr");
            HttpResponseMessage wikiResponse = await client.SendAsync(request);
            JsonNode? wikiJson = await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync());

            string? iconUrl = wikiJson?["data"]?["page"]?["icon_url"]?.GetValue<string>();
            if (string.IsNullOrEmpty(iconUrl))
            {
                Logger.LogWarning("No icon URL found for equip {EquipName} with {Filename}", equip.Name, filename);
                return false;
            }

            HttpResponseMessage imageResponse = await client.GetAsync(iconUrl);
            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to download image for equip {EquipName} with {Filename}: {StatusCode}",
                    equip.Name, filename, imageResponse.StatusCode);
                return false;
            }

            await using Stream imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using Image image = await Image.LoadAsync(imageStream);
            image.Mutate(x => x.Resize(300, 0));
            using MemoryStream processedStream = new();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;

            await ImageRepository.UploadFileAsync(filename, processedStream, "png");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating equip data for {EquipName} with {Filename}", equip.Name,
                filename);
            throw;
        }
    }

    private async Task<bool> UpdateSkillImageAsync(IEnumerable<Skill> skills)
    {
        try
        {
            HttpClient client = HttpClientFactory.CreateClient("Default");

            return await skills.ToAsyncEnumerable().SelectAwait(async skill =>
            {
                string filename = string.Format(BaseString,
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

                string? iconUrl = skill.ItemUrl;
                if (string.IsNullOrEmpty(iconUrl))
                {
                    Logger.LogWarning("No icon URL found for skill {SkillName} with {Filename}",
                        skill.SkillStages[0].Name, filename);
                    return false;
                }

                HttpResponseMessage imageResponse = await client.GetAsync(iconUrl);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("Failed to download image for skill {SkillName} with {Filename}: {StatusCode}",
                        skill.SkillStages[0].Name, filename, imageResponse.StatusCode);
                    return false;
                }

                await using Stream imageStream = await imageResponse.Content.ReadAsStreamAsync();
                using Image image = await Image.LoadAsync(imageStream);
                image.Mutate(x => x.Resize(skill.PointType == 1 ? 50 : 80, 0));
                using MemoryStream processedStream = new();
                await image.SaveAsPngAsync(processedStream);
                processedStream.Position = 0;

                await ImageRepository.UploadFileAsync(filename, processedStream, "png");

                Logger.LogInformation("Successfully processed skill image for {SkillName}", skill.SkillStages[0].Name);
                return true;
            }).AllAsync(x => x);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating skill data");
            throw;
        }
    }

    private async Task<bool> UpdateRankImageAsync(IEnumerable<Rank> ranks)
    {
        HttpClient client = HttpClientFactory.CreateClient("Default");
        try
        {
            return await ranks.ToAsyncEnumerable().SelectAwait(async rank =>
            {
                string filename = string.Format(BaseString, rank.Id);
                if (await ImageRepository.FileExistsAsync(filename))
                {
                    Logger.LogInformation(
                        "Rank image for {Rank} with {Filename} already exists, skipping download",
                        rank.Name, filename);
                    return true;
                }

                Logger.LogInformation("Downloading rank image for {RankName} with {Filename}", rank.Name, filename);

                string? iconUrl = rank.Icon;
                if (string.IsNullOrEmpty(iconUrl))
                {
                    Logger.LogWarning("No icon URL found for rank {RankName} with {Filename}", rank.Name, filename);
                    return false;
                }

                HttpResponseMessage imageResponse = await client.GetAsync(iconUrl);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("Failed to download image for rank {RankName} with {Filename}: {StatusCode}",
                        rank.Name, filename, imageResponse.StatusCode);
                    return false;
                }

                await using Stream imageStream = await imageResponse.Content.ReadAsStreamAsync();
                using Image image = await Image.LoadAsync(imageStream);
                image.Mutate(x => x.Resize(80, 0));
                using MemoryStream processedStream = new();
                await image.SaveAsPngAsync(processedStream);
                processedStream.Position = 0;

                await ImageRepository.UploadFileAsync(filename, processedStream, "png");

                Logger.LogInformation("Successfully processed rank image for {RankName}", rank.Name);
                return true;
            }).AllAsync(x => x);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating rank data");
            throw new InvalidOperationException("Failed to update rank data", e);
        }
    }

    private async Task<bool> UpdateRelicImageAsync(IEnumerable<Relic> relics, Dictionary<string, string> relicWikiPages)
    {
        try
        {
            List<Relic> allRelics = [.. relics];
            (List<Relic> relicsInWiki, List<Relic> relicsNotInWiki) = SeparateRelicsByWikiAvailability(allRelics, relicWikiPages);

            HttpClient client = HttpClientFactory.CreateClient("Default");
            bool overallSuccess = true;

            // Process relics that have wiki entries
            if (relicsInWiki.Count != 0)
            {
                bool success = await ProcessRelicsWithWikiData(relicsInWiki, relicWikiPages);
                overallSuccess = overallSuccess && success;
            } // Process relics without wiki entries using direct icon URLs

            if (relicsNotInWiki.Count != 0)
            {
                Logger.LogInformation("Processing {Count} relics not found in wiki pages", relicsNotInWiki.Count);

                bool success = await ProcessRelicsWithDirectIcons(relicsNotInWiki, client);
                overallSuccess = overallSuccess && success;
            }

            return overallSuccess;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating relic data");
            throw;
        }
    }

    private static (List<Relic> RelicsInWiki, List<Relic> RelicsNotInWiki) SeparateRelicsByWikiAvailability(
        List<Relic> allRelics, Dictionary<string, string> relicWikiPages)
    {
        List<Relic> relicsInWiki = [.. allRelics.Where(r => relicWikiPages.ContainsKey(r.Id.ToString() ?? string.Empty))];

        List<Relic> relicsNotInWiki = [.. allRelics.Where(r => !relicWikiPages.ContainsKey(r.Id.ToString() ?? string.Empty))];

        return (relicsInWiki, relicsNotInWiki);
    }

    private async Task<bool> ProcessRelicsWithWikiData(List<Relic> relics, Dictionary<string, string> relicWikiPages)
    {
        IEnumerable<IGrouping<string, Relic>> relicsByWikiPage = relics.GroupBy(r => relicWikiPages[r.Id.ToString() ?? string.Empty]);
        Dictionary<string, string?> relicNameToIdMap = relics.ToDictionary(r => r.Name!.ToLowerInvariant(), r => r.Id.ToString());
        bool overallSuccess = true;

        foreach (IGrouping<string, Relic> wikiGroup in relicsByWikiPage)
        {
            HttpClient client = HttpClientFactory.CreateClient("Default");
            string wikiPageUrl = wikiGroup.Key;
            string wikiPageId = wikiPageUrl.Split('/')[^1];
            Logger.LogInformation("Fetching wiki data for page ID {WikiPageId}", wikiPageId);

            (string SetName, JsonArray? RelicList)? wikiData = await FetchWikiDataAsync(client, wikiPageId);
            if (wikiData == null)
            {
                // Fallback to direct icons for this group
                bool success = await ProcessRelicsWithDirectIcons([.. wikiGroup], client);
                overallSuccess = overallSuccess && success;
                continue;
            }

            (string? setName, JsonArray? relicList) = wikiData.Value;
            if (relicList == null || relicList.Count == 0)
            {
                Logger.LogWarning("No relic list found for wiki page ID {WikiPageId}, falling back to direct icons",
                    wikiPageId);
                bool success = await ProcessRelicsWithDirectIcons([.. wikiGroup], client, setName);
                overallSuccess = overallSuccess && success;
                continue;
            }

            Logger.LogInformation("Found {Count} relics in set with page ID {WikiPageId}", relicList.Count, wikiPageId);

            bool success2 = await ProcessWikiRelicList(relicList, setName, relicNameToIdMap, client);
            overallSuccess = overallSuccess && success2;
        }

        return overallSuccess;
    }

    private async Task<(string SetName, JsonArray? RelicList)?> FetchWikiDataAsync(HttpClient client, string wikiPageId)
    {
        try
        {
            HttpRequestMessage request = new(HttpMethod.Get, $"{WikiApi}?entry_page_id={wikiPageId}");
            request.Headers.Add("X-Rpc-Wiki_app", "hsr");
            HttpResponseMessage wikiResponse = await client.SendAsync(request);
            JsonNode? wikiJson = await JsonNode.ParseAsync(await wikiResponse.Content.ReadAsStreamAsync());

            if (wikiJson == null)
            {
                Logger.LogWarning("Failed to parse relic wiki JSON for page ID {WikiPageId}", wikiPageId);
                return null;
            }

            string? setName = wikiJson["data"]?["page"]?["name"]?.GetValue<string>();
            JsonArray? wikiModules = wikiJson["data"]?["page"]?["modules"]?.AsArray();
            JsonNode? setEntry = wikiModules?.FirstOrDefault(x => x?["name"]?.GetValue<string>() == "Set");

            if (setEntry == null)
            {
                Logger.LogWarning("No set entry found for wiki page ID {WikiPageId}", wikiPageId);
                return (setName ?? string.Empty, null);
            }

            string? wikiEntry = setEntry["components"]?.AsArray()[0]?["data"]?.GetValue<string>();
            if (string.IsNullOrEmpty(wikiEntry))
            {
                Logger.LogWarning("No wiki entry found for wiki page ID {WikiPageId}", wikiPageId);
                return (setName ?? string.Empty, null);
            }

            JsonNode? jsonObject = JsonNode.Parse(wikiEntry);
            JsonArray? relicList = jsonObject?["list"]?.AsArray();

            return (setName ?? string.Empty, relicList);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error fetching wiki data for page ID {WikiPageId}", wikiPageId);
            return null;
        }
    }

    private async Task<bool> ProcessWikiRelicList(JsonArray relicList, string setName,
        Dictionary<string, string?> relicNameToIdMap, HttpClient client)
    {
        bool overallSuccess = true;

        foreach (JsonNode? relicNode in relicList)
        {
            string? relicName = relicNode?["title"]?.GetValue<string>();
            string? wikiRelicId = relicNode?["id"]?.GetValue<string>();
            string? iconUrl = relicNode?["icon_url"]?.GetValue<string>();

            if (string.IsNullOrEmpty(relicName) || string.IsNullOrEmpty(wikiRelicId) || string.IsNullOrEmpty(iconUrl))
            {
                Logger.LogWarning("Missing data for relic in set {SetName}", setName);
                overallSuccess = false;
                continue;
            }

            relicName = QuotationMarkRegex().Replace(relicName, "'"); // Normalize quotes

            if (!relicNameToIdMap.TryGetValue(relicName.ToLowerInvariant(), out string? actualRelicId) ||
                actualRelicId == null)
            {
                Logger.LogInformation("No mapping found for relic {RelicName} in set {SetName}", relicName, setName);
                continue;
            }

            Logger.LogInformation("Matched wiki relic {RelicName} to relic ID {RelicId}", relicName, actualRelicId);

            await m_RelicRepository.AddSetName(int.Parse(actualRelicId[1..^1]), setName);

            bool success = await DownloadAndSaveRelicImage(actualRelicId, iconUrl, relicName, client);
            overallSuccess = overallSuccess && success;
        }

        return overallSuccess;
    }

    private async Task<bool> ProcessRelicsWithDirectIcons(List<Relic> relics, HttpClient client, string? setName = null)
    {
        bool overallSuccess = true;

        foreach (Relic relic in relics)
        {
            if (string.IsNullOrEmpty(relic.Icon))
            {
                Logger.LogWarning("No icon URL found for relic {RelicName} with ID {RelicId}", relic.Name, relic.Id);
                overallSuccess = false;
                continue;
            }

            if (setName != null) await m_RelicRepository.AddSetName(relic.GetSetId(), setName);

            bool success = await DownloadAndSaveRelicImage(relic.Id.ToString()!, relic.Icon, relic.Name!, client,
                "from Icon URL");
            overallSuccess = overallSuccess && success;
        }

        return overallSuccess;
    }

    private async Task<bool> DownloadAndSaveRelicImage(string relicId, string iconUrl, string relicName,
        HttpClient client, string source = "")
    {
        string filename = string.Format(BaseString, relicId);

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

            HttpResponseMessage imageResponse = await client.GetAsync(iconUrl);

            if (!imageResponse.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to download image for relic {RelicName}: {StatusCode}", relicName,
                    imageResponse.StatusCode);
                return false;
            }

            await using Stream imageStream = await imageResponse.Content.ReadAsStreamAsync();
            using Image image = await Image.LoadAsync(imageStream);
            image.Mutate(x =>
            {
                x.Resize(150, 0);
                x.ApplyGradientFade(0.5f);
            });
            using MemoryStream processedStream = new();
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
            Logger.LogError(e, "Error downloading relic image for {RelicName} with {Filename}", relicName,
                filename);
            return false;
        }
    }

    [GeneratedRegex(@"\u2018|\u2019")]
    private static partial Regex QuotationMarkRegex();

    [GeneratedRegex(@"[\s:]")]
    public static partial Regex StatBonusRegex();
}
