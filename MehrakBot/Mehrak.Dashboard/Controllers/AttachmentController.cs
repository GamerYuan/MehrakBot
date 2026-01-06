using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("attachments")]
public sealed class AttachmentController : ControllerBase
{
    private readonly IAttachmentStorageService m_AttachmentStorage;
    private readonly ILogger<AttachmentController> m_Logger;

    public AttachmentController(IAttachmentStorageService attachmentStorage, ILogger<AttachmentController> logger)
    {
        m_AttachmentStorage = attachmentStorage;
        m_Logger = logger;
    }

    [HttpGet("{fileName}")]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        fileName = fileName.ReplaceLineEndings("").Trim();

        if (!AttachmentStorageService.IsValidStorageFileName(fileName))
            return BadRequest(new { error = "Invalid attachment identifier." });

        var attachment = await m_AttachmentStorage.DownloadAsync(fileName, cancellationToken).ConfigureAwait(false);
        if (attachment is null)
        {
            m_Logger.LogWarning("Attachment {FileName} was not found in storage", fileName);
            return NotFound(new { error = "Attachment not found." });
        }

        return File(attachment.Content, attachment.ContentType);
    }
}
