#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.CharList;

public class GenshinCharListApplicationService : BaseAttachmentApplicationService<GenshinCharListApplicationContext>
{
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICardService<IEnumerable<GenshinBasicCharacterData>> m_CardService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCache;
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;

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
        ILogger<GenshinCharListApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CardService = cardService;
        m_CharacterApi = characterApi;
        m_CharacterCache = characterCache;
        m_ImageRepository = imageRepository;
        m_WikiApi = wikiApi;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinCharListApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var charResponse = await
                m_CharacterApi.GetAllCharactersAsync(new GenshinCharacterApiContext(context.UserId, context.LtUid,
                    context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var characterList = charResponse.Data.ToList();
            _ = m_CharacterCache.UpsertCharacters(Game.Genshin, characterList.Select(x => x.Name));

            var filename = GetFileName(JsonSerializer.Serialize(characterList), "jpg", profile.GameUid);
            if (await AttachmentExistsAsync(filename))
            {
                return CommandResult.Success(
                [
                    new CommandText($"<@{context.UserId}>"),
                    new CommandAttachment(filename)
                ]);
            }

            var avatarTask =
                characterList.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var weaponTask =
                characterList.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.Weapon.ToImageData(),
                        new ImageProcessorBuilder().Resize(200, 0).Build()));
            var temp = await characterList.ToAsyncEnumerable()
                .Where(async (x, token) => (x.Weapon.Level > 40 && !await m_ImageRepository.FileExistsAsync(x.Weapon.ToAscendedImageName()))
                    || x.Weapon.Level == 40).ToListAsync();

            var weaponDict = temp.DistinctBy(x => x.Id!.Value).ToDictionary(x => x.Id!.Value, x => x);
            var charToFetch = temp.Select(x => x.Id!.Value).Distinct().ToList();

            if (charToFetch.Count > 0)
            {
                var charDetailResponse = await m_CharacterApi.GetCharacterDetailAsync(new GenshinCharacterApiContext(
                    context.UserId, context.LtUid, context.LToken, gameUid, region, charToFetch));

                if (charDetailResponse.IsSuccess && charDetailResponse.Data is var charDetail)
                {
                    var result = await charDetail.List.Where(x => x.Weapon.PromoteLevel >= 2)
                        .DistinctBy(x => x.Weapon.Id)
                        .ToAsyncEnumerable()
                        .Select(async (GenshinCharacterInformation x, CancellationToken token) =>
                        {
                            if (!charDetail.WeaponWiki.TryGetValue(x.Weapon.Id.ToString()!, out var wikiUrl))
                            {
                                return (Data: x, Url: Result<string>.Failure(StatusCode.ExternalServerError));
                            }
                            return (Data: x, Url: await GetWeaponUrlsAsync(context, profile, x.Weapon.Name, wikiUrl.Split('/')[^1]));
                        })
                        .Where(x => x.Url.IsSuccess)
                        .Select(x =>
                        {
                            weaponDict[x.Data.Base.Id!].Weapon.Ascended = true;

                            // Special case for catalyst
                            if (x.Data.Weapon.Type == 10)
                            {
                                return m_ImageUpdaterService.UpdateImageAsync(new ImageData(x.Data.Weapon.ToAscendedImageName(), x.Url.Data!),
                                    new ImageProcessorBuilder().AddOperation(GetCatalystIconProcessor()).Build());
                            }
                            else
                            {
                                // ignore result from this method
                                return m_ImageUpdaterService.UpdateMultiImageAsync(
                                    new MultiImageData(x.Data.Weapon.ToAscendedImageName(),
                                        [x.Data.Weapon.Icon, x.Url.Data!]),
                                    new GenshinWeaponImageProcessor()
                                );
                            }
                        }).ToListAsync();

                    await Task.WhenAll(result);
                }
            }

            var completed = await Task.WhenAll(avatarTask.Concat(weaponTask));
            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId,
                    JsonSerializer.Serialize(characterList));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(context.UserId,
                    characterList, profile);
            cardContext.SetParameter("server", server);

            using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, filename, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.AttachmentStoreError);
            }

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>"), new CommandAttachment(filename)
            ]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Character List"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    private async Task<Result<string>>
        GetWeaponUrlsAsync(GenshinCharListApplicationContext context, GameProfileDto profile,
            string weaponName, string wikiEntry)
    {
        foreach (var locale in Enum.GetValues<WikiLocales>())
        {
            var weapWiki = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, locale));

            if (!weapWiki.IsSuccess)
            {
                Logger.LogWarning(LogMessage.ApiError, "Weapon Wiki", context.UserId, profile.GameUid, weapWiki);
                continue;
            }
            List<string> ascendedUrls = [];

            var jsonStr = weapWiki.Data["data"]?["page"]?["modules"]?.AsArray().SelectMany(x => x?["components"]?.AsArray() ?? [])
                .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "gallery_character")?["data"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(jsonStr))
            {
                var json = JsonNode.Parse(jsonStr);
                ascendedUrls.AddRange(json?["list"]?.AsArray().Select(x => x?["img"]?.GetValue<string>())
                    .Where(x => !string.IsNullOrEmpty(x)).Cast<string>() ?? []);
            }

            if (ascendedUrls.Count == 2)
            {
                return Result<string>.Success(ascendedUrls[1]);
            }

            Logger.LogWarning("Weapon wiki image is empty for Weapon: {Weapon}, Locale: {Locale}, Data:\n{Data}",
                weaponName, locale, weapWiki.Data.ToJsonString());
        }

        return Result<string>.Failure(StatusCode.ExternalServerError);
    }

    private static Action<IImageProcessingContext> GetCatalystIconProcessor()
    {
        return ctx =>
        {
            ctx.CropTransparentPixels();
            ctx.Resize(new ResizeOptions()
            {
                Size = new Size(180, 180),
                Mode = ResizeMode.Pad
            });
            ctx.Pad(200, 200);
        };
    }
}
