#region

using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationService : BaseAttachmentApplicationService<GenshinAbyssApplicationContext>
{
    private readonly ICardService<GenshinAbyssInformation> m_CardService;

    private readonly IApiService<GenshinAbyssInformation, BaseHoYoApiContext> m_ApiService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApi;

    private readonly IImageUpdaterService m_ImageUpdaterService;

    public GenshinAbyssApplicationService(
        ICardService<GenshinAbyssInformation> cardService,
        IApiService<GenshinAbyssInformation, BaseHoYoApiContext> apiService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext> characterApi,
        IImageUpdaterService imageUpdaterService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<GenshinAbyssApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
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
            var floor = int.Parse(context.GetParameter("floor")!);
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin,
                region);
            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server.ToString());

            var abyssInfo = await m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid,
                    region));
            if (!abyssInfo.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError,
                    "Abyss Data", context.UserId, profile.GameUid, abyssInfo);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Spiral Abyss data"));
            }

            var abyssData = abyssInfo.Data;
            var floorData = abyssData.Floors!.FirstOrDefault(x => x.Index == floor);

            if (floorData == null)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, $"Abyss (Floor {floor})", context.UserId,
                    profile.GameUid);
                return CommandResult.Success([
                    new CommandText(
                        string.Format(ResponseMessage.NoClearRecords, $"Spiral Abyss (Floor {floor})"))
                ], isEphemeral: true);
            }

            var filename = GetFileName(JsonSerializer.Serialize(floorData), "jpg", profile.GameUid);
            if (await AttachmentExistsAsync(filename))
            {
                return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Spiral Abyss Summary (Floor {floor})",
                        CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{abyssData.StartTime}:f>\nCycle end: <t:{abyssData.EndTime}:f>"),
                    new CommandAttachment(filename),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ], isEphemeral: false);
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
                new GenshinCharacterApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid,
                    region));

            if (!charListResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError,
                    "Character List", context.UserId, profile.GameUid, charListResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var charList = charListResponse.Data.ToList();

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

            var completed = await Task.WhenAll(tasks);
            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Abyss", context.UserId,
                    JsonSerializer.Serialize(abyssData));
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<GenshinAbyssInformation>(context.UserId, abyssData, profile);

            cardContext.SetParameter("constMap", constMap);
            cardContext.SetParameter("server", server);
            cardContext.SetParameter("floor", floor);

            using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, filename, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.AttachmentStoreError);
            }

            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Spiral Abyss Summary (Floor {floor})",
                        CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{abyssData.StartTime}:f>\nCycle end: <t:{abyssData.EndTime}:f>"),
                    new CommandAttachment(filename),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true
            );
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Abyss", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Spiral Abyss"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Abyss", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
