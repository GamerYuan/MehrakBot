using Mehrak.Domain.Common;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Text.Json.Nodes;

namespace Mehrak.GameApi.Zzz;

internal class ZzzImageUpdaterService : ImageUpdaterService<ZzzFullAvatarData>
{
    protected override string AvatarString => FileNameFormat.ZzzAvatarName;
    protected override string SideAvatarString => string.Empty;

    private const string WikiEndpoint = "/hoyowiki/zzz/wapi/entry_page";

    public ZzzImageUpdaterService(IImageRepository imageRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<ZzzImageUpdaterService> logger)
        : base(imageRepository, httpClientFactory, logger)
    {
    }

    public override async Task UpdateDataAsync(ZzzFullAvatarData characterInformation, IEnumerable<Dictionary<string, string>> wiki)
    {
        try
        {
            ZzzAvatarData character = characterInformation.AvatarList[0];

            List<Task> tasks = [];

            tasks.Add(UpdateCharacterImageAsync(character.Id,
                characterInformation.AvatarWiki[character.Id.ToString()].Split('/')[^1]));
            if (character.Weapon != null) tasks.Add(UpdateWeaponImageAsync(character.Weapon));
            tasks.Add(UpdateDiskImageAsync(character.Equip));

            await Task.WhenAll(tasks);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to update data for character {CharacterName}", characterInformation.AvatarList[0].Name);
            throw new CommandException("An error occurred while updating character images", e);
        }
    }

    public virtual async Task UpdateBuddyImageAsync(int id, string url)
    {
        try
        {
            string fileName = string.Format(FileNameFormat.ZzzBuddyName, id);
            if (await ImageRepository.FileExistsAsync(fileName))
            {
                Logger.LogDebug("Buddy image for {BuddyId} already exists. Skipping update.", id);
                return;
            }
            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpResponseMessage result = await client.GetAsync(url);
            using Image image = await Image.LoadAsync(await result.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(300, 0, KnownResamplers.Lanczos3));
            using MemoryStream processedStream = new();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;
            await ImageRepository.UploadFileAsync(fileName, processedStream, "png");
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to update buddy image for {BuddyId}", id);
            throw new CommandException("An error occurred while updating bangboo images", e);
        }
    }

    private async Task UpdateCharacterImageAsync(int characterId, string wikiEntry)
    {
        try
        {
            string fileName = string.Format(FileNameFormat.ZzzFileName, characterId);
            if (await ImageRepository.FileExistsAsync(fileName))
            {
                Logger.LogDebug("Character image for {CharacterId} already exists. Skipping update.", characterId);
                return;
            }

            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                RequestUri = new Uri($"{HoYoLabDomains.WikiApi}{WikiEndpoint}?entry_page_id={wikiEntry}")
            };
            request.Headers.Add("X-Rpc-Wiki_app", "zzz");
            HttpResponseMessage wikiContent = await client.SendAsync(request);

            JsonNode? json = await JsonNode.ParseAsync(await wikiContent.Content.ReadAsStreamAsync());
            JsonNode? modules;

            if ((modules = json?["data"]?["page"]?["modules"]) == null)
            {
                Logger.LogWarning("No image found for character {CharacterId} in wiki data", characterId);
                return;
            }

            JsonNode? galleryData = modules.AsArray().FirstOrDefault(x => x?["name"]?
                .GetValue<string>() == "Gallery")?["components"]?[0]?["data"];

            if (galleryData == null)
            {
                Logger.LogWarning("No gallery data found for character {CharacterId} in wiki data", characterId);
                return;
            }

            JsonNode? gallery = JsonNode.Parse(galleryData.GetValue<string>()!);
            if (gallery == null)
            {
                Logger.LogWarning("Failed to parse gallery data for character {CharacterId}", characterId);
                return;
            }

            string? imageUrl = gallery["list"]?.AsArray()
                .FirstOrDefault(x => x?["key"]?.GetValue<string>() == "Splash Art")?["img"]?.GetValue<string>();

            if (string.IsNullOrEmpty(imageUrl))
            {
                Logger.LogWarning("No image URL found for character {CharacterId} in gallery data", characterId);
                return;
            }

            HttpResponseMessage imageResponse = await client.GetAsync(imageUrl);
            using Image image = await Image.LoadAsync(await imageResponse.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(2000, 0));
            using MemoryStream processedStream = new();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;
            await ImageRepository.UploadFileAsync(fileName, processedStream, "png");
            Logger.LogInformation("Updated character image for {CharacterId}", characterId);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to update character image for {CharacterId}", characterId);
            throw;
        }
    }

    private async Task UpdateWeaponImageAsync(Weapon weapon)
    {
        try
        {
            string fileName = string.Format(FileNameFormat.ZzzFileName, weapon.Id);
            if (await ImageRepository.FileExistsAsync(fileName))
            {
                Logger.LogDebug("Weapon image for {WeaponId} already exists. Skipping update.", weapon.Id);
                return;
            }
            HttpClient client = HttpClientFactory.CreateClient("Default");
            HttpResponseMessage result = await client.GetAsync(weapon.Icon);
            using Image image = await Image.LoadAsync(await result.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(150, 0));
            using MemoryStream processedStream = new();
            await image.SaveAsPngAsync(processedStream);
            processedStream.Position = 0;
            await ImageRepository.UploadFileAsync(fileName, processedStream, "png");
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to update weapon image for {WeaponId}: {WeaponName}", weapon.Id, weapon.Name);
            throw;
        }
    }

    private async Task UpdateDiskImageAsync(IEnumerable<DiskDrive> equips)
    {
        try
        {
            await equips.DistinctBy(x => x.EquipSuit).ToAsyncEnumerable().SelectAwait(async x =>
            {
                string fileName = string.Format(FileNameFormat.ZzzFileName, x.EquipSuit.SuitId);
                if (await ImageRepository.FileExistsAsync(fileName))
                {
                    Logger.LogDebug("Disk image for suit {SuitId} already exists. Skipping update.", x.EquipSuit.SuitId);
                    return true;
                }

                HttpClient client = HttpClientFactory.CreateClient("Default");
                HttpResponseMessage result = await client.GetAsync(x.Icon);
                using Image image = await Image.LoadAsync(await result.Content.ReadAsStreamAsync());
                image.Mutate(x => x.Resize(140, 0));
                using MemoryStream processedStream = new();
                await image.SaveAsPngAsync(processedStream);
                processedStream.Position = 0;

                await ImageRepository.UploadFileAsync(fileName, processedStream, "png");

                return true;
            }).AllAsync(x => x);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to update disk images, disks:{Disk}", string.Join(", ", equips.Select(x => $"{x.Id}: {x.Name}")));
            throw;
        }
    }
}
