#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Genshin.Abyss;

public class GenshinAbyssApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<GenshinAbyssInformation> m_CardService;

    private readonly IApiService<GenshinAbyssInformation, BaseHoYoApiContext> m_ApiService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApi;

    private readonly IImageUpdaterService m_ImageUpdaterService;

    protected override string CommandName => "Abyss";
    protected override string CardName => "Spiral Abyss";

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

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var floor = int.Parse(context.GetParameter("floor")!);
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var cachedGameUid = await GetCachedGameUidAsync(context.UserId, context.LtUid, Game.Genshin, region, cancellationToken);
        var profileTask = FetchGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin,
            region, cancellationToken);

        Task<Result<GenshinAbyssInformation>>? primaryTask = null;
        if (cachedGameUid != null)
        {
            primaryTask = m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, cachedGameUid, region), cancellationToken);
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

        if (cachedGameUid == null)
        {
            await SaveGameUidAsync(context.UserId, context.LtUid, Game.Genshin, region, profile.GameUid, profile.Level, cancellationToken);
            primaryTask = m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid, region), cancellationToken);
        }

        var abyssInfo = await primaryTask!;
        if (!abyssInfo.IsSuccess)
        {
            if (abyssInfo.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(abyssInfo.ErrorMessage ?? "Cancelled");
            if (abyssInfo.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
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
                await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken)));

        tasks.AddRange(abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
            .Concat(abyssData.EnergySkillRank!)
            .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
            .Select(async x =>
                await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Resize(0, 150).Build(), cancellationToken)));

        List<GenshinBasicCharacterData>? charList = null;

        try
        {
            var charListResponse = await m_CharacterApi.GetAllCharactersAsync(
                new GenshinCharacterApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid,
                    region), cancellationToken);

            if (!charListResponse.IsSuccess)
            {
                if (charListResponse.StatusCode == StatusCode.Cancelled)
                    throw new OperationCanceledException(charListResponse.ErrorMessage ?? "Cancelled");
                if (charListResponse.StatusCode == StatusCode.Timeout)
                    return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
                Logger.LogError(LogMessage.ApiError,
                    "Character List", context.UserId, profile.GameUid, charListResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            charList = charListResponse.Data.ToList();
        }
        finally
        {
            await Task.WhenAll(tasks);
        }

        var constMap = charList!.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

        var completed = tasks.Select(x => x.Result).ToArray();
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
}
