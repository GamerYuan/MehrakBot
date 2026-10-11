#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.CodeRedeem;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure.CodeRedeem;
using Mehrak.Infrastructure.CodeRedeem.Models;
using Mehrak.Infrastructure.User;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Application.CodeRedeem;

public class CodeRedeemApplicationService : BaseApplicationService
{
    private readonly CodeRedeemDbContext m_CodeContext;
    private readonly IApiService<CodeRedeemResult, CodeRedeemApiContext> m_ApiService;

    private readonly int m_RedeemDelay = 5500;

    protected override string CommandName => "Codes";

    public CodeRedeemApplicationService(
        CodeRedeemDbContext codeContext,
        IApiService<CodeRedeemResult, CodeRedeemApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<CodeRedeemApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_CodeContext = codeContext;
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var game = Enum.Parse<Game>(context.GetParameter("game")!);
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion(game);

        var cachedGameUid = await GetCachedGameUidAsync(context.UserId, context.LtUid, game, region, cancellationToken);
        var profileResult = await FetchGameProfileAsync(context.UserId, context.LtUid, context.LToken, game,
            region, cancellationToken);
        if (!profileResult.IsSuccess)
        {
            if (profileResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Cancelled");
            if (profileResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.GameProfile);
            Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
            return CommandResult.Failure(CommandFailureReason.AuthError, CommandErrorCode.AuthenticationRequired, CommandErrorTarget.GameProfile);
        }
        var profile = profileResult.Data;
        var gameUid = profile.GameUid;

        if (cachedGameUid == null)
        {
            await SaveGameUidAsync(context.UserId, context.LtUid, game, region, gameUid, profile.Level, cancellationToken);
        }

        var codeStr = context.GetParameter("code");

        var codes = RegexExpressions.RedeemCodeSplitRegex().Split(codeStr!)
            .Select(NormalizeCode)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
            codes = (await m_CodeContext.Codes.AsNoTracking()
                .Where(x => x.Game == game)
                .Select(x => x.Code)
                .ToListAsync(cancellationToken))
                .Select(NormalizeCode)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (codes.Count == 0)
        {
            Logger.LogWarning(
                "User {UserId} used the code command but no codes were provided or found in the cache",
                context.UserId);
            return CommandResult.Success(CodeRedemptionOutcome.Empty(game, gameUid));
        }

        List<RedemptionItem> items = [];
        Dictionary<string, CodeStatus> successfulCodes = [];

        for (var i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            var response = await m_ApiService.GetAsync(
                new CodeRedeemApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region,
                    game, code), cancellationToken);
            if (response.IsSuccess)
            {
                Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", code,
                    context.UserId);
                items.Add(response.Data.Outcome is RedemptionOutcome.Redeemed or RedemptionOutcome.Expired
                    or RedemptionOutcome.Invalid or RedemptionOutcome.Cooldown or RedemptionOutcome.AlreadyUsed
                    ? RedemptionItem.Completed(code, response.Data.Outcome)
                    : RedemptionItem.Failed(code, new(CommandErrorCode.InvalidResponse, CommandErrorTarget.CodeRedemption)));
                successfulCodes.Add(code, response.Data.Status);
            }
            else if (response.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(response.ErrorMessage ?? "Cancelled");
            }
            else if (response.StatusCode == StatusCode.Timeout)
            {
                items.Add(RedemptionItem.Failed(code, new(CommandErrorCode.Timeout, CommandErrorTarget.CodeRedemption)));
                items.AddRange(codes.Skip(i + 1).Select(RedemptionItem.NotAttempted));
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout,
                    CommandErrorTarget.CodeRedemption, CodeRedemptionOutcome.Create(game, gameUid, items,
                        items.Any(x => x.Outcome != RedemptionOutcome.Failed && x.Outcome != RedemptionOutcome.NotAttempted)
                            ? OperationOutcome.Partial : OperationOutcome.Failed));
            }
            else
            {
                Logger.LogError("Failed to redeem code {Code} for user {UserId} Result {@Result}", code,
                    context.UserId, response);
                items.Add(RedemptionItem.Failed(code, new(
                    response.StatusCode == StatusCode.Unauthorized ? CommandErrorCode.AuthenticationRequired : CommandErrorCode.ApiUnavailable,
                    CommandErrorTarget.CodeRedemption)));
            }

            if (i < codes.Count - 1) await Task.Delay(m_RedeemDelay, cancellationToken);
        }

        if (successfulCodes.Count > 0)
            await UpdateCodesAsync(game, successfulCodes, cancellationToken);

        var completedCount = items.Count(x => x.Outcome != RedemptionOutcome.Failed);
        return CommandResult.Success(CodeRedemptionOutcome.Create(game, gameUid, items,
            completedCount == items.Count ? OperationOutcome.Complete : completedCount > 0 ? OperationOutcome.Partial : OperationOutcome.Failed));
    }

    private async Task UpdateCodesAsync(Game game, Dictionary<string, CodeStatus> codes,
        CancellationToken cancellationToken)
    {
        var incoming = codes.Select(x => x.Key).ToHashSet();

        var existingCodes = await m_CodeContext.Codes.AsNoTracking()
            .Where(x => x.Game == game && incoming.Contains(x.Code))
            .ToListAsync();

        var expiredCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Invalid)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<CodeRedeemModel> codesToRemove = [];

        if (expiredCodes.Count > 0)
        {
            codesToRemove.AddRange(existingCodes.Where(x => expiredCodes.Contains(x.Code)));
            m_CodeContext.Codes.RemoveRange(codesToRemove);
        }

        var newValidCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Valid)
            .Select(kvp => kvp.Key)
            .Except(existingCodes.Select(x => x.Code), StringComparer.OrdinalIgnoreCase)
            .Select(x => new CodeRedeemModel
            {
                Game = game,
                Code = x
            })
            .ToList();

        if (newValidCodes.Count > 0)
        {
            m_CodeContext.Codes.AddRange(newValidCodes);
        }

        try
        {
            await m_CodeContext.SaveChangesAsync(cancellationToken);
            Logger.LogInformation("Added {Count} new codes, removed {Removed} expired codes for game: {Game}.",
                newValidCodes.Count, codesToRemove.Count, game);
        }
        catch (DbUpdateException e)
        {
            Logger.LogError(e, "Failed to update Codes for game: {Game}", game);
        }
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
