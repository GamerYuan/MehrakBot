#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Wiki;
using Mehrak.Infrastructure.User;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin.CharList;

public class GenshinCharListApplicationService : BaseAttachmentApplicationService
{
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICardService<IEnumerable<GenshinBasicCharacterData>> m_CardService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCache;
    private readonly IImageRepository m_ImageRepository;
    private readonly IMultiImageProcessor m_WeaponImageProcessor;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;


    protected override string CommandName => "CharList";
    protected override string CardName => "Character List";
    public GenshinCharListApplicationService(
        IImageUpdaterService imageUpdaterService,
        ICardService<IEnumerable<GenshinBasicCharacterData>> cardService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext> characterApi,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ICharacterCacheService characterCache,
        IImageRepository imageRepository,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IAttachmentStorageService attachmentStorageService,
        [FromKeyedServices(Mehrak.Domain.Shared.Common.CommandName.ImageProcessor.Weapon)] IMultiImageProcessor weaponImageProcessor,
        ILogger<GenshinCharListApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CardService = cardService;
        m_CharacterApi = characterApi;
        m_CharacterCache = characterCache;
        m_ImageRepository = imageRepository;
        m_WikiApi = wikiApi;
        m_WeaponImageProcessor = weaponImageProcessor;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var cachedGameUid = await GetCachedGameUidAsync(context.UserId, context.LtUid, Game.Genshin, region, cancellationToken);
        var profileTask = FetchGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region, cancellationToken);

        Task<Result<IEnumerable<GenshinBasicCharacterData>>>? primaryTask = null;
        if (cachedGameUid != null)
        {
            primaryTask = m_CharacterApi.GetAllCharactersAsync(new GenshinCharacterApiContext(context.UserId, context.LtUid,
                context.LToken, cachedGameUid, region), cancellationToken);
        }

        var profileResult = await profileTask;
        if (!profileResult.IsSuccess)
        {
            if (profileResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Cancelled");
            if (profileResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
            return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
        }
        var profile = profileResult.Data;

        var gameUid = profile.GameUid;

        if (cachedGameUid == null)
        {
            await SaveGameUidAsync(context.UserId, context.LtUid, Game.Genshin, region, profile.GameUid, profile.Level, cancellationToken);
            primaryTask = m_CharacterApi.GetAllCharactersAsync(new GenshinCharacterApiContext(context.UserId, context.LtUid,
                context.LToken, gameUid, region), cancellationToken);
        }

        var charResponse = await primaryTask!;

        if (!charResponse.IsSuccess)
        {
            if (charResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(charResponse.ErrorMessage ?? "Cancelled");
            if (charResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character List"));
        }

        var characterList = charResponse.Data.ToList();
        _ = m_CharacterCache.UpsertCharacters(Game.Genshin,
            characterList.Select(x => new CharacterUpsertEntry(x.Name, x.Id)));

        var filename = GetCardFileName("genshin", "char-list", "v1", characterList, profile,
            new { Server = server });
        if (await AttachmentExistsAsync(filename, cancellationToken))
        {
            return CommandResult.Success(
            [
                new CommandText($"<@{context.UserId}>"),
                    new CommandAttachment(filename)
            ]);
        }

        // Start avatar/weapon image updates immediately
        var avatarTasks = characterList.DistinctBy(x => x.ToImageName()).Select(x =>
            m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken)).ToList();
        var weaponProcessor = new ImageProcessorBuilder().Resize(200, 0).Build();
        var weaponTasks = characterList.DistinctBy(x => x.Weapon.ToBaseImageName()).Select(x =>
            m_ImageUpdaterService.UpdateImageAsync(x.Weapon.ToImageData(),
                weaponProcessor, cancellationToken)).ToList();

        // Share in-flight existence checks for repeated weapon artwork.
        var ascendedExistsTasks = characterList.Where(x => x.Weapon.Level > 40)
            .Select(x => x.Weapon.ToAscendedImageName()).Distinct()
            .ToDictionary(name => name, name => m_ImageRepository.FileExistsAsync(name, cancellationToken));
        var existenceChecks = characterList.Select(async x =>
        {
            var needsAscended = x.Weapon.Level > 40 && !await ascendedExistsTasks[x.Weapon.ToAscendedImageName()];
            return (Character: x, NeedsAscended: needsAscended, LevelExact40: x.Weapon.Level == 40);
        }).ToList();
        var existenceResults = await Task.WhenAll(existenceChecks);
        var temp = existenceResults.Where(x => x.NeedsAscended || x.LevelExact40).Select(x => x.Character).ToList();

        foreach (var result in existenceResults.Where(x => x.Character.Weapon.Level > 40 && !x.NeedsAscended))
            result.Character.Weapon.Ascended = true;

        var weaponDict = temp.DistinctBy(x => x.Id!.Value).ToDictionary(x => x.Id!.Value, x => x);
        var charToFetch = temp.Select(x => x.Id!.Value).Distinct().ToList();

        // Process ascended weapons concurrently with avatar/weapon updates
        Task? ascendedTask = null;
        if (charToFetch.Count > 0)
        {
            ascendedTask = ProcessAscendedWeaponsAsync(context, profile, gameUid, region, charToFetch, weaponDict, cancellationToken);
        }

        // Wait for everything
        var allTasks = avatarTasks.Concat(weaponTasks).Cast<Task>();
        if (ascendedTask != null) allTasks = allTasks.Append(ascendedTask);
        await Task.WhenAll(allTasks);

        if (avatarTasks.Concat(weaponTasks).Any(x => !x.Result))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId,
                JsonSerializer.Serialize(characterList));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(context.UserId,
                characterList, profile);
        cardContext.SetParameter("server", server);

        using var card = await m_CardService.GetCardAsync(cardContext, cancellationToken);
        if (!await StoreAttachmentAsync(context.UserId, filename, card, cancellationToken))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError,
                ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success([
            new CommandText($"<@{context.UserId}>"), new CommandAttachment(filename)
        ]);
    }

    private async Task ProcessAscendedWeaponsAsync(
        IApplicationContext context, GameProfileDto profile, string gameUid, string region,
        List<int> charToFetch, Dictionary<int, GenshinBasicCharacterData> weaponDict,
        CancellationToken cancellationToken)
    {
        var charDetailResponse = await m_CharacterApi.GetCharacterDetailAsync(new GenshinCharacterApiContext(
            context.UserId, context.LtUid, context.LToken, gameUid, region, charToFetch), cancellationToken);

        if (charDetailResponse.StatusCode == StatusCode.Cancelled)
            throw new OperationCanceledException(charDetailResponse.ErrorMessage ?? "Cancelled");
        if (charDetailResponse.StatusCode == StatusCode.Timeout)
            return;
        if (!charDetailResponse.IsSuccess) return;
        var charDetail = charDetailResponse.Data;

        // A level-40 weapon can be either ascended or unascended. Resolve each
        // equipped instance from detail data, but fetch its shared artwork only once.
        foreach (var detail in charDetail.List.Where(x => x.Weapon.PromoteLevel < 2))
        {
            if (weaponDict.TryGetValue(detail.Base.Id, out var character) && character.Weapon.Level == 40)
                character.Weapon.Ascended = false;
        }

        var updateTasks = charDetail.List.Where(x => x.Weapon.PromoteLevel >= 2)
            .GroupBy(x => x.Weapon.Id)
            .Select(async group =>
            {
                var weapon = group.First().Weapon;
                var imageName = weapon.ToAscendedImageName();
                var ready = await m_ImageRepository.FileExistsAsync(imageName, cancellationToken);
                if (!ready)
                {
                    if (!charDetail.WeaponWiki.TryGetValue(weapon.Id.ToString()!, out var wikiUrl)) return;
                    var url = await GetWeaponUrlsAsync(context, profile, weapon.Name, wikiUrl.Split('/')[^1], cancellationToken);
                    if (!url.IsSuccess) return;
                    ready = await m_ImageUpdaterService.UpdateMultiImageAsync(
                        new MultiImageData(imageName, [weapon.Icon, url.Data!]),
                        m_WeaponImageProcessor, cancellationToken);
                }

                if (ready)
                {
                    foreach (var detail in group)
                    {
                        if (weaponDict.TryGetValue(detail.Base.Id, out var character))
                            character.Weapon.Ascended = true;
                    }
                }
            }).ToList();

        await Task.WhenAll(updateTasks);
    }

    private async Task<Result<string>>
        GetWeaponUrlsAsync(IApplicationContext context, GameProfileDto profile,
            string weaponName, string wikiEntry, CancellationToken cancellationToken = default)
    {
        var bestStatus = StatusCode.ExternalServerError;

        var cnResult = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, WikiLocales.CN), cancellationToken);
        if (cnResult.IsSuccess)
        {
            var cnUrls = ParseWeaponAscendedUrls(cnResult.Data);
            if (cnUrls.Count == 2)
                return Result<string>.Success(cnUrls[1]);
        }

        var otherLocales = Enum.GetValues<WikiLocales>().Where(x => x != WikiLocales.CN);
        var tasks = otherLocales.Select(async locale =>
        {
            var result = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, locale), cancellationToken);
            if (!result.IsSuccess) return (Result: result, Parsed: (List<string>?)null);
            return (Result: result, Parsed: ParseWeaponAscendedUrls(result.Data));
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var urls = results.FirstOrDefault(x => x.Parsed is { Count: 2 });

        if (urls.Parsed == null)
        {
            foreach (var (result, _) in results)
            {
                if (result.StatusCode == StatusCode.Cancelled) bestStatus = StatusCode.Cancelled;
                else if (result.StatusCode == StatusCode.Timeout && bestStatus != StatusCode.Cancelled)
                    bestStatus = StatusCode.Timeout;
            }
            return Result<string>.Failure(bestStatus);
        }

        return Result<string>.Success(urls.Parsed[1]);
    }

    private static List<string> ParseWeaponAscendedUrls(JsonNode data)
    {
        var jsonStr = data["data"]?["page"]?["modules"]?.AsArray().SelectMany(x => x?["components"]?.AsArray() ?? [])
            .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "gallery_character")?["data"]?.GetValue<string>();

        if (string.IsNullOrEmpty(jsonStr)) return [];

        var json = JsonNode.Parse(jsonStr);
        return json?["list"]?.AsArray().Select(x => x?["img"]?.GetValue<string>())
            .Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList() ?? [];
    }
}
