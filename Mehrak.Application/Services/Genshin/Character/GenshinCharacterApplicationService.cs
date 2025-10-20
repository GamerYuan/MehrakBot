using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Text.Json.Nodes;

namespace Mehrak.Application.Services.Genshin.Character;

internal class GenshinCharacterApplicationService : BaseApplicationService<GenshinCharacterApplicationContext>
{
    private readonly ICardService<GenshinCharacterInformation> m_CardService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> m_CharacterApi;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;

    public GenshinCharacterApplicationService(
        ICardService<GenshinCharacterInformation> cardService,
        ICharacterCacheService characterCacheService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> characterApi,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageUpdaterService imageUpdaterService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinCharacterApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_CharacterCacheService = characterCacheService;
        m_CharacterApi = characterApi;
        m_WikiApi = wikiApi;
        m_ImageUpdaterService = imageUpdaterService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinCharacterApplicationContext context)
    {
        try
        {
            Logger.LogInformation("Executing character service for user {UserId}", context.UserId);

            var region = context.Server.ToRegion();
            var characterName = context.GetParameter<string>("character")!;

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var charListResponse = await
                m_CharacterApi.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));
            if (!charListResponse.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch character list for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    profile.GameUid, context.Server, charListResponse.ErrorMessage);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            var characters = charListResponse.Data;

            var character =
                characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                m_CharacterCacheService.GetAliases(Game.Genshin).TryGetValue(characterName, out var name);

                if (name == null ||
                    (character =
                        characters.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) ==
                    null)
                {
                    Logger.LogWarning("Character {CharacterName} not found for user {UserId}", characterName,
                        context.UserId);
                    return CommandResult.Failure($"Character {characterName} not found. Please try again");
                }
            }

            var characterInfo = await m_CharacterApi.GetCharacterDetailAsync(
                new(context.UserId, context.LtUid, context.LToken, gameUid, region, character.Id!.Value));

            if (!characterInfo.IsSuccess)
            {
                Logger.LogInformation("Failed to fetch character detail for gameUid: {GameUid}, characterId: {CharacterId}, error: {Error}",
                    profile.GameUid, character.Id, characterInfo.ErrorMessage);
                return CommandResult.Failure("Failed to retrieve character detail. Please try again later");
            }

            var charData = characterInfo.Data.List[0];
            var wikiEntry = characterInfo.Data.AvatarWiki[charData.Base.Id.ToString()].Split('/')[^1];

            var charWiki = await m_WikiApi.GetAsync(new(context.UserId, Game.Genshin, wikiEntry));

            if (!charWiki.IsSuccess)
            {
                Logger.LogInformation("Failed to fetch character wiki for characterId: {CharacterId}, error: {Error}",
                    charData.Base.Id, charWiki.ErrorMessage);
                return CommandResult.Failure("Failed to retrieve character wiki. Please try again later");
            }

            var url = charWiki.Data["data"]?["page"]?["header_img_url"]?.ToString();

            if (string.IsNullOrEmpty(url))
            {
                Logger.LogInformation("Character wiki image URL is empty for characterId: {CharacterId}",
                    charData.Base.Id);
                return CommandResult.Failure("Failed to retrieve character image url. Please try again later");
            }

            var charTask = m_ImageUpdaterService.UpdateImageAsync(new ImageData(
                    string.Format(FileNameFormat.Genshin.FileName, charData.Base.Id), url),
                new ImageProcessorBuilder().AddOperation(GetCharacterImageProcessor()).Build());
            var weapTask = m_ImageUpdaterService.UpdateImageAsync(charData.Weapon.ToImageData(),
                new ImageProcessorBuilder().Resize(200, 0).Build());
            var constTask = charData.Constellations.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(90, 0).Build()));
            var skillTask = charData.Skills.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(charData.Base.Id),
                new ImageProcessorBuilder().Resize(100, 0).Build()));
            var relicTask = charData.Relics.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(300, 0).AddOperation(ctx => ctx.Pad(300, 300))
                    .AddOperation(ctx => ctx.ApplyGradientFade(0.5f)).Build()));

            await Task.WhenAll(charTask, weapTask);
            await Task.WhenAll(constTask.Concat(skillTask).Concat(relicTask));

            var card = await m_CardService.GetCardAsync(new BaseCardGenerationContext<GenshinCharacterInformation>(context.UserId,
                characterInfo.Data.List[0], context.Server, profile));

            return CommandResult.Success(content: $"<@{context.UserId}>", attachments: [new("character_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Character card for user {UserId}", context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Character card for user {UserId}", context.UserId);
            return CommandResult.Failure("An unknown error occurred while generating character card");
        }
    }

    private static Action<IImageProcessingContext> GetCharacterImageProcessor()
    {
        return ctx =>
        {
            var size = ctx.GetCurrentSize();
            int minX = size.Width;
            int minY = size.Height;
            int maxX = -1;
            int maxY = -1;
            Lock @lock = new();

            ctx.ProcessPixelRowsAsVector4((row, point) =>
            {
                if (row[point.X].W > 0)
                {
                    @lock.Enter();
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                    @lock.Exit();
                }
            });

            if (maxX > minX && maxY > minY)
            {
                ctx.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }

            size = ctx.GetCurrentSize();
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
}
