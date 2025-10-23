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
using System.Text.Json;

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationService : BaseApplicationService<GenshinAbyssApplicationContext>
{
    private const string CharListEmpty = "No characters found in character list for User {UserId} with gameUid: {GameUid}";

    private readonly ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation> m_CardService;
    private readonly IApiService<GenshinAbyssInformation, BaseHoYoApiContext> m_ApiService;
    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> m_CharacterApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;

    public GenshinAbyssApplicationService(
        ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation> cardService,
        IApiService<GenshinAbyssInformation, BaseHoYoApiContext> apiService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> characterApi,
        IImageUpdaterService imageUpdaterService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinAbyssApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ApiService = apiService;
        m_CharacterApi = characterApi;
        m_ImageUpdaterService = imageUpdaterService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinAbyssApplicationContext context)
    {
        try
        {
            var floor = context.GetParameter<uint>("floor");

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, context.Server.ToRegion());
            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            var abyssInfo = await m_ApiService.GetAsync(
                new(context.UserId, context.LtUid, context.LToken, profile.GameUid, context.Server.ToRegion()));
            if (!abyssInfo.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError,
                    "Abyss Data", context.UserId, profile.GameUid, abyssInfo.ErrorMessage);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Spiral Abyss data"));
            }

            var abyssData = abyssInfo.Data;
            var floorData = abyssData.Floors!.FirstOrDefault(x => x.Index == floor);

            if (floorData == null)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, $"Abyss (Floor {floor})", context.UserId, profile.GameUid);
                return CommandResult.Success([new CommandText(
                    string.Format(ResponseMessage.NoClearRecords, $"Spiral Abyss (Floor {floor})"))], isEphemeral: true);
            }

            List<Task<bool>> tasks = [];

            tasks.AddRange(floorData.Levels!.SelectMany(x => x.Battles!.SelectMany(y => y.Avatars!))
                .Concat(abyssData.RevealRank!.Select(x => new AbyssAvatar
                {
                    Icon = x.AvatarIcon,
                    Id = x.AvatarId,
                    Rarity = x.Rarity
                }))
                .DistinctBy(x => x.Id)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor)));

            tasks.AddRange(abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
                .Concat(abyssData.EnergySkillRank!)
                .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                        new ImageProcessorBuilder().Resize(0, 150).Build())));

            var charListResponse = await m_CharacterApi.GetAllCharactersAsync(
                new(context.UserId, context.LtUid, context.LToken, profile.GameUid, context.Server.ToRegion()));

            if (!charListResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError,
                    "Character List", context.UserId, profile.GameUid, charListResponse.ErrorMessage);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }
            var charList = charListResponse.Data.ToList();

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

            var completed = await Task.WhenAll(tasks);
            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Abyss", context.UserId, JsonSerializer.Serialize(abyssData));
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(new(context.UserId, floor, abyssData, context.Server, profile, constMap));

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s Spiral Abyss Summary (Floor {floor})", CommandText.TextType.Header3),
                new CommandText($"Cycle start: <t:{abyssData.StartTime}:f>\nCycle end: <t:{abyssData.EndTime}:f>"),
                new CommandAttachment("abyss_card.jpg", card),
                new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)],
                true
            );
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Abyss", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError, string.Format(ResponseMessage.CardGenError, "Spiral Abyss"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Abyss", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
