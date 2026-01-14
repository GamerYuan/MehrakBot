#region

using System.Text;
using Mehrak.Application.Models.Context;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Common;

public class CodeRedeemApplicationService : BaseApplicationService<CodeRedeemApplicationContext>
{
    private readonly CodeRedeemDbContext m_CodeContext;
    private readonly IApiService<CodeRedeemResult, CodeRedeemApiContext> m_ApiService;

    private readonly int m_RedeemDelay = 5500;

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

    public override async Task<CommandResult> ExecuteAsync(CodeRedeemApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            var region = server.ToRegion(context.Game);

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, context.Game, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, context.Game, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var codeStr = context.GetParameter<string>("code");

            var codes = RegexExpressions.RedeemCodeSplitRegex().Split(codeStr!).Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (codes.Count == 0)
                codes = await m_CodeContext.Codes.AsNoTracking()
                    .Where(x => x.Game == context.Game)
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
                        context.Game, code.ToUpperInvariant().Trim()));
                if (response.IsSuccess)
                {
                    Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", trimmedCode,
                        context.UserId);
                    sb.Append($"{trimmedCode}: {response.Data.Message}\n");
                    successfulCodes.Add(trimmedCode, response.Data.Status);
                }
                else
                {
                    Logger.LogError("Failed to redeem code {Code} for user {UserId} Result {@Result}", trimmedCode,
                        context.UserId, response);
                    sb.Append($"{trimmedCode}: An error occurred while redeeming the code\n");
                }

                if (i < codes.Count - 1) await Task.Delay(m_RedeemDelay);
            }

            if (successfulCodes.Count > 0)
                await UpdateCodesAsync(context.Game, successfulCodes).ConfigureAwait(false);

            return CommandResult.Success([new CommandText(sb.ToString().TrimEnd())]);
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, $"Codes {context.Game}", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
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
