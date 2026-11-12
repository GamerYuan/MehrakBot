#region

using Mehrak.Dashboard.Asset.Models;
using Mehrak.Domain.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

#endregion

namespace Mehrak.Dashboard.Asset;

[ApiController]
[Authorize(Policy = "RequireSuperAdmin")]
[Route("assets")]
public sealed class AssetCleanupController : ControllerBase
{
    private readonly IAssetPrefixCleanupService m_CleanupService;
    private readonly ILogger<AssetCleanupController> m_Logger;

    public AssetCleanupController(
        IAssetPrefixCleanupService cleanupService,
        ILogger<AssetCleanupController> logger)
    {
        m_CleanupService = cleanupService;
        m_Logger = logger;
    }

    [HttpPost("cleanup-by-prefix")]
    public async Task<IActionResult> CleanupByPrefix(
        [FromBody] CleanupByPrefixRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new
            {
                code = AssetCleanupErrorCodes.InvalidPrefix,
                error = "A request body with a non-whitespace prefix is required."
            });

        try
        {
            var result = await m_CleanupService.CleanupByPrefixAsync(
                new AssetPrefixCleanupRequest(request.Prefix ?? "", request.DryRun),
                cancellationToken).ConfigureAwait(false);

            return Ok(result);
        }
        catch (AssetCleanupException ex)
        {
            m_Logger.LogWarning(
                "Asset prefix cleanup failed with {ErrorCode} from {FailureType}",
                ex.Code, FailureTypeName(ex));
            return StatusCode(MapStatusCode(ex.Code), new { code = ex.Code, error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the innermost exception type of a failure chain. Provider exception messages are not logged because
    /// storage providers can echo request keys or other untrusted content in them.
    /// </summary>
    private static string FailureTypeName(Exception exception)
    {
        var current = exception;
        while (current.InnerException is { } inner)
            current = inner;

        return current.GetType().Name;
    }

    internal static int MapStatusCode(string code) => code switch
    {
        AssetCleanupErrorCodes.InvalidPrefix => StatusCodes.Status400BadRequest,
        AssetCleanupErrorCodes.SourceBucketUnconfigured => StatusCodes.Status409Conflict,
        AssetCleanupErrorCodes.SourceBucketConflict => StatusCodes.Status409Conflict,
        AssetCleanupErrorCodes.BackupBucketUnconfigured => StatusCodes.Status409Conflict,
        AssetCleanupErrorCodes.InvalidBackupPrefix => StatusCodes.Status409Conflict,
        AssetCleanupErrorCodes.BackupBucketConflict => StatusCodes.Status409Conflict,
        AssetCleanupErrorCodes.BackupBucketUnavailable => StatusCodes.Status502BadGateway,
        AssetCleanupErrorCodes.LifecycleUnavailable => StatusCodes.Status502BadGateway,
        AssetCleanupErrorCodes.LifecycleRejected => StatusCodes.Status502BadGateway,
        AssetCleanupErrorCodes.LifecycleConflict => StatusCodes.Status409Conflict,
        AssetCleanupErrorCodes.ListingFailed => StatusCodes.Status502BadGateway,
        AssetCleanupErrorCodes.ListingTokenMissing => StatusCodes.Status502BadGateway,
        AssetCleanupErrorCodes.ListingTokenRepeated => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };
}
