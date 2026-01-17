#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Abstractions;
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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.Character;

internal class GenshinCharacterApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<GenshinCharacterInformation> m_CardService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApi;

    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageRepository m_ImageRepository;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApplicationMetrics m_MetricsService;

    public GenshinCharacterApplicationService(
        ICardService<GenshinCharacterInformation> cardService,
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext> characterApi,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageRepository imageRepository,
        IImageUpdaterService imageUpdaterService,
        IApplicationMetrics metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorage,
        ILogger<GenshinCharacterApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorage, logger)
    {
        m_CardService = cardService;
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_CharacterApi = characterApi;
        m_WikiApi = wikiApi;
        m_ImageRepository = imageRepository;
        m_ImageUpdaterService = imageUpdaterService;
        m_MetricsService = metricsService;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();
            var characterName = context.GetParameter("character")!;

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var charListResponse = await
                m_CharacterApi.GetAllCharactersAsync(new GenshinCharacterApiContext(context.UserId, context.LtUid,
                    context.LToken, gameUid, region));
            if (!charListResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, profile.GameUid,
                    charListResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var characters = charListResponse.Data;
            _ = m_CharacterCacheService.UpsertCharacters(Game.Genshin,
                characters.Select(x => x.Name));

            var character =
                characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                m_AliasService.GetAliases(Game.Genshin).TryGetValue(characterName, out var name);

                if (name == null ||
                    (character =
                        characters.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) ==
                    null)
                {
                    Logger.LogInformation(LogMessage.CharNotFoundInfo, characterName, context.UserId, gameUid);
                    return CommandResult.Success(
                        [new CommandText(string.Format(ResponseMessage.CharacterNotFound, characterName))],
                        isEphemeral: true);
                }
            }

            var characterInfo = await m_CharacterApi.GetCharacterDetailAsync(
                new GenshinCharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region,
                    character.Id!.Value));

            if (!characterInfo.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character Detail", context.UserId, profile.GameUid,
                    characterInfo);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character data"));
            }

            var charData = characterInfo.Data.List[0];

            var filename = GetFileName(JsonSerializer.Serialize(charData), "jpg", profile.GameUid);
            if (await AttachmentExistsAsync(filename))
            {
                return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>"),
                    new CommandAttachment(filename)
                ]);
            }

            List<Task<bool>> tasks = [];

            Task<Result<string>>? charImageUrlTask = null;
            Task<Result<string>>? weapImageTask = null;

            if (!await m_ImageRepository.FileExistsAsync(charData.Base.ToImageName()))
            {
                var wikiEntry = characterInfo.Data.AvatarWiki[charData.Base.Id.ToString()].Split('/')[^1];
                charImageUrlTask = GetCharacterImageUrlAsync(context, profile, charData, wikiEntry);
            }

            if (!await m_ImageRepository.FileExistsAsync(charData.Weapon.ToAscendedImageName()))
            {
                var wikiEntry = characterInfo.Data.WeaponWiki[charData.Weapon.Id.ToString()!].Split('/')[^1];
                weapImageTask = GetWeaponUrlsAsync(context, profile, charData, wikiEntry);
            }

            tasks.AddRange(m_ImageUpdaterService.UpdateImageAsync(charData.Weapon.ToImageData(),
                new ImageProcessorBuilder().Resize(200, 0).Build()));
            tasks.AddRange(charData.Constellations.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(90, 0).Build())));
            tasks.AddRange(charData.Skills.Select(x => m_ImageUpdaterService.UpdateImageAsync(
                x.ToImageData(charData.Base.Id),
                new ImageProcessorBuilder().Resize(100, 0).Build())));
            tasks.AddRange(charData.Relics.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(300, 0).AddOperation(ctx => ctx.Pad(300, 300))
                    .AddOperation(ctx => ctx.ApplyGradientFade(0.5f)).Build())));

            if (charImageUrlTask != null)
            {
                var charImage = await charImageUrlTask;
                if (!charImage.IsSuccess)
                {
                    Logger.LogError("Failed to fetch Character {Character} image from wiki", charData.Base.Name);
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                var url = charImage.Data;
                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(charData.Base.ToImageName(), url),
                    new ImageProcessorBuilder().AddOperation(GetCharacterImageProcessor()).Build()));
            }

            if (weapImageTask != null)
            {
                var weapImage = await weapImageTask;
                if (weapImage.IsSuccess)
                {
                    // Special case for catalyst
                    if (charData.Weapon.Type == 10)
                    {
                        tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(charData.Weapon.ToAscendedImageName(), weapImage.Data),
                            new ImageProcessorBuilder().AddOperation(GetCatalystIconProcessor()).Build()));
                    }
                    else
                    {
                        // ignore result from this method
                        await m_ImageUpdaterService.UpdateMultiImageAsync(
                            new MultiImageData(charData.Weapon.ToAscendedImageName(),
                                [charData.Weapon.Icon, weapImage.Data]),
                            new GenshinWeaponImageProcessor()
                        );
                    }
                }
            }

            await Task.WhenAll(tasks);

            if (tasks.Any(x => !x.Result))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                    JsonSerializer.Serialize(charData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<GenshinCharacterInformation>(context.UserId,
                characterInfo.Data.List[0], profile);
            cardContext.SetParameter("server", server);

            using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, filename, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.AttachmentStoreError);
            }


            m_MetricsService.TrackCharacterSelection(nameof(Game.Genshin), character.Name.ToLowerInvariant());

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>"),
                new CommandAttachment(filename)
            ]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Character", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Character"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Character", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    private async Task<Result<string>> GetCharacterImageUrlAsync(IApplicationContext context, GameProfileDto profile,
        GenshinCharacterInformation charData, string wikiEntry)
    {
        string? url = null;

        // Prio to CN locale
        foreach (var locale in Enum.GetValues<WikiLocales>().OrderBy(x => x == WikiLocales.CN ? 0 : 1))
        {
            var charWiki = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, locale));

            if (!charWiki.IsSuccess)
            {
                Logger.LogWarning(LogMessage.ApiError, "Character Wiki", context.UserId, profile.GameUid, charWiki);
                continue;
            }

            url = charWiki.Data["data"]?["page"]?["header_img_url"]?.ToString();

            if (!string.IsNullOrEmpty(url)) break;

            Logger.LogWarning("Character wiki image URL is empty for CharacterId: {CharacterId}, Locale: {Locale}, Data:\n{Data}",
                charData.Base.Id, locale, charWiki.Data.ToJsonString());
        }

        if (string.IsNullOrEmpty(url))
        {
            return Result<string>.Failure(StatusCode.ExternalServerError);
        }

        return Result<string>.Success(url);
    }

    private async Task<Result<string>>
        GetWeaponUrlsAsync(IApplicationContext context, GameProfileDto profile,
            GenshinCharacterInformation charData, string wikiEntry)
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

            Logger.LogWarning("Character wiki image URL is empty for CharacterId: {CharacterId}, Locale: {Locale}, Data:\n{Data}",
                charData.Base.Id, locale, weapWiki.Data.ToJsonString());
        }

        return Result<string>.Failure(StatusCode.ExternalServerError);
    }

    private static Action<IImageProcessingContext> GetCharacterImageProcessor()
    {
        return ctx =>
        {
            ctx.CropTransparentPixels();

            var size = ctx.GetCurrentSize();
            if (size.Width >= size.Height)
                ctx.Resize(0,
                    (int)Math.Round(1280 * Math.Min(1.2 * size.Height / size.Width, 1f)),
                    KnownResamplers.Lanczos3);
            else
                ctx.Resize(1400, 0, KnownResamplers.Lanczos3);

            size = ctx.GetCurrentSize();

            if (size.Height > 1280)
                ctx.Resize(0, 1280, KnownResamplers.Lanczos3);

            size = ctx.GetCurrentSize();

            if (size.Width > 1280)
                ctx.Crop(new Rectangle((size.Width - 1280) / 2, 0, 1280, size.Height));

            ctx.ApplyGradientFade();
        };
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
