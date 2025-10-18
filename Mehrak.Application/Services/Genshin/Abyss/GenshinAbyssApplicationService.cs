using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationService : BaseApplicationService<GenshinAbyssApplicationContext>
{
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
            var floor = context.GetParameter<int>("floor");

            if (floor is < 9 or > 12)
            {
                return CommandResult.Failure("Invalid floor number. Please provide a floor between 9 and 12");
            }

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, context.Server.ToRegion());
            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var abyssInfo = await m_ApiService.GetAsync(
                new(context.UserId, context.LtUid, context.LToken, profile.GameUid, context.Server.ToRegion()));
            if (!abyssInfo.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch Abyss information for gameUid: {GameUid}, region: {Server}, error: {Error}",
                    profile.GameUid, context.Server, abyssInfo.ErrorMessage);
                return CommandResult.Failure(abyssInfo.ErrorMessage);
            }

            var abyssData = abyssInfo.Data;
            var floorData = abyssData.Floors!.FirstOrDefault(x => x.Index == floor);

            if (floorData == null)
            {
                return CommandResult.Failure($"No clear record found for floor {floor}.");
            }

            var tasks = floorData.Levels!.SelectMany(x => x.Battles!.SelectMany(y => y.Avatars!))
                .Concat(abyssData.RevealRank!.Select(x => new AbyssAvatar
                {
                    Icon = x.AvatarIcon,
                    Id = x.AvatarId,
                    Rarity = x.Rarity
                }))
                .DistinctBy(x => x.Id)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));

            var sideAvatarTasks = abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
                .Concat(abyssData.EnergySkillRank!)
                .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                        new ImageProcessorBuilder().Resize(0, 150).Build()));

            var charListResponse = await m_CharacterApi.GetAllCharactersAsync(
                new(context.UserId, context.LtUid, context.LToken, profile.GameUid, context.Server.ToRegion()));

            if (!charListResponse.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch character list for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    profile.GameUid, context.Server, charListResponse.ErrorMessage);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }
            var charList = charListResponse.Data.ToList();

            if (charList.Count == 0)
            {
                Logger.LogWarning("No characters found for gameUid: {GameUid}, server: {Server}", profile.GameUid, context.Server);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

            await Task.WhenAll(tasks);
            await Task.WhenAll(sideAvatarTasks);

            var card = await m_CardService.GetCardAsync(new(context.UserId, (uint)floor, abyssData, context.Server, profile, constMap));

            return CommandResult.Success(
                $"<@{context.UserId}>'s Spiral Abyss Summary (Floor {floor})",
                $"Cycle start: <t:{abyssData.StartTime}:f>\nCycle end: <t:{abyssData.EndTime}:f>",
                $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.",
                [new("abyss_card.jpg", card)]
            );
        }
        catch (Exception e)
        {
            Logger.LogError(e, "An error occurred while executing");
            return CommandResult.Failure(e.Message);
        }
    }
}
