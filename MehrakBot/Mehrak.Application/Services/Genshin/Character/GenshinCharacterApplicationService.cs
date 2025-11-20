#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.Character;

internal class GenshinCharacterApplicationService : BaseApplicationService<GenshinCharacterApplicationContext>
{
    private readonly ICardService<GenshinCharacterInformation> m_CardService;
    private readonly ICharacterCacheService m_CharacterCacheService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>
        m_CharacterApi;

    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageRepository m_ImageRepository;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IMetricsService m_MetricsService;

    public GenshinCharacterApplicationService(
        ICardService<GenshinCharacterInformation> cardService,
        ICharacterCacheService characterCacheService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> characterApi,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageRepository imageRepository,
        IImageUpdaterService imageUpdaterService,
        IMetricsService metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<GenshinCharacterApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_CharacterCacheService = characterCacheService;
        m_CharacterApi = characterApi;
        m_WikiApi = wikiApi;
        m_ImageRepository = imageRepository;
        m_ImageUpdaterService = imageUpdaterService;
        m_MetricsService = metricsService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinCharacterApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            var region = server.ToRegion();
            var characterName = context.GetParameter<string>("character")!;

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server);

            var gameUid = profile.GameUid;

            var charListResponse = await
                m_CharacterApi.GetAllCharactersAsync(new CharacterApiContext(context.UserId, context.LtUid,
                    context.LToken, gameUid, region));
            if (!charListResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, profile.GameUid,
                    charListResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
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
                    Logger.LogInformation(LogMessage.CharNotFoundInfo, characterName, context.UserId, gameUid);
                    return CommandResult.Success(
                        [new CommandText(string.Format(ResponseMessage.CharacterNotFound, characterName))],
                        isEphemeral: true);
                }
            }

            var characterInfo = await m_CharacterApi.GetCharacterDetailAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region,
                    character.Id!.Value));

            if (!characterInfo.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character Detail", context.UserId, profile.GameUid,
                    characterInfo);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character data"));
            }

            var charData = characterInfo.Data.List[0];
            var wikiEntry = characterInfo.Data.AvatarWiki[charData.Base.Id.ToString()].Split('/')[^1];

            List<Task<bool>> tasks = [];

            if (!await m_ImageRepository.FileExistsAsync(
                    string.Format(FileNameFormat.Genshin.FileName, charData.Base.Id)))
            {
                var charWiki = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry));

                if (!charWiki.IsSuccess)
                {
                    Logger.LogError(LogMessage.ApiError, "Character Wiki", context.UserId, profile.GameUid, charWiki);
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                var url = charWiki.Data["data"]?["page"]?["header_img_url"]?.ToString();

                if (string.IsNullOrEmpty(url))
                {
                    Logger.LogError("Character wiki image URL is empty for characterId: {CharacterId}, Data:\n{Data}",
                        charData.Base.Id, charWiki.Data.ToJsonString());
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(
                        string.Format(FileNameFormat.Genshin.FileName, charData.Base.Id), url),
                    new ImageProcessorBuilder().AddOperation(GetCharacterImageProcessor()).Build()));
            }

            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(charData.Weapon.ToImageData(),
                new ImageProcessorBuilder().Resize(200, 0).Build()));
            tasks.AddRange(charData.Constellations.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(90, 0).Build())));
            tasks.AddRange(charData.Skills.Select(x => m_ImageUpdaterService.UpdateImageAsync(
                x.ToImageData(charData.Base.Id),
                new ImageProcessorBuilder().Resize(100, 0).Build())));
            tasks.AddRange(charData.Relics.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(300, 0).AddOperation(ctx => ctx.Pad(300, 300))
                    .AddOperation(ctx => ctx.ApplyGradientFade(0.5f)).Build())));

            await Task.WhenAll(tasks);

            if (tasks.Any(x => !x.Result))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                    JsonSerializer.Serialize(charData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(new BaseCardGenerationContext<GenshinCharacterInformation>(
                context.UserId,
                characterInfo.Data.List[0], server, profile));

            m_MetricsService.TrackCharacterSelection(nameof(Game.Genshin), character.Name.ToLowerInvariant());

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>"), new CommandAttachment("character_card.jpg", card)
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

            if (maxX > minX && maxY > minY) ctx.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));

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
