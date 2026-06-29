#region

using System.Text;
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

        var profileResult =
            await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, game, region, cancellationToken);
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

        await UpdateGameUidAsync(context.UserId, context.LtUid, game, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;

        var codeStr = context.GetParameter("code");

        var codes = RegexExpressions.RedeemCodeSplitRegex().Split(codeStr!).Where(x => !string.IsNullOrEmpty(x))
            .ToList();

        if (codes.Count == 0)
            codes = await m_CodeContext.Codes.AsNoTracking()
                .Where(x => x.Game == game)
                .Select(x => x.Code)
                .ToListAsync();

        if (codes.Count == 0)
        {
            Logger.LogWarning(
                "User {UserId} used the code command but no codes were provided or found in the cache",
                context.UserId);
            return CommandResult.Success(
                [new CommandText("No known codes found in database. Please provide a valid code")],
                isEphemeral: true);
        }

        StringBuilder sb = new();
        Dictionary<string, CodeStatus> successfulCodes = [];

        for (var i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            var trimmedCode = code.ToUpperInvariant().Trim();
            var response = await m_ApiService.GetAsync(
                new CodeRedeemApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region,
                    game, code.ToUpperInvariant().Trim()), cancellationToken);
            if (response.IsSuccess)
            {
                Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", trimmedCode,
                    context.UserId);
                sb.Append($"{trimmedCode}: {response.Data.Message}\n");
                successfulCodes.Add(trimmedCode, response.Data.Status);
            }
            else if (response.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(response.ErrorMessage ?? "Cancelled");
            }
            else if (response.StatusCode == StatusCode.Timeout)
            {
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            }
            else
            {
                Logger.LogError("Failed to redeem code {Code} for user {UserId} Result {@Result}", trimmedCode,
                    context.UserId, response);
                sb.Append($"{trimmedCode}: An error occurred while redeeming the code\n");
            }

            if (i < codes.Count - 1) await Task.Delay(m_RedeemDelay, cancellationToken);
        }

        if (successfulCodes.Count > 0)
            _ = UpdateCodesAsync(game, successfulCodes);

        return CommandResult.Success([new CommandText(sb.ToString().TrimEnd())]);
    }

    private async Task UpdateCodesAsync(Game game, Dictionary<string, CodeStatus> codes)
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
            await m_CodeContext.SaveChangesAsync();
            Logger.LogInformation("Added {Count} new codes, removed {Removed} expired codes for game: {Game}.",
                newValidCodes.Count, codesToRemove.Count, game);
        }
        catch (DbUpdateException e)
        {
            Logger.LogError(e, "Failed to update Codes for game: {Game}", game);
        }
    }
}
