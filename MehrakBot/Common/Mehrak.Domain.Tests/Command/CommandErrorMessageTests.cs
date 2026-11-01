using Mehrak.Domain.Command.Models;

namespace Mehrak.Domain.Tests.Command;

[TestFixture]
public class CommandErrorMessageTests
{
    [TestCase(CommandErrorCode.Unknown, "An unexpected error occurred. Please try again later.")]
    [TestCase(CommandErrorCode.AuthenticationRequired, "Invalid HoYoLAB UID or cookies. Please re-authenticate.")]
    [TestCase(CommandErrorCode.ApiUnavailable, "Unable to retrieve game data. Please try again later.")]
    [TestCase(CommandErrorCode.ImageUpdateFailed, "Unable to update the required images. Please try again later.")]
    [TestCase(CommandErrorCode.AttachmentStoreFailed, "Unable to store the generated image. Please try again later.")]
    [TestCase(CommandErrorCode.Cancelled, "Command execution cancelled.")]
    [TestCase(CommandErrorCode.Timeout, "Command execution timed out. Please try again later.")]
    [TestCase(CommandErrorCode.InvalidRequest, "Invalid command request.")]
    [TestCase(CommandErrorCode.InvalidResponse, "The command returned an unsupported or invalid response.")]
    [TestCase(CommandErrorCode.CharacterNotFound, "Character not found.")]
    [TestCase(CommandErrorCode.RequestLimitExceeded, "Exceeded the maximum number of characters per request.")]
    public void Message_UsesSafeTextForErrorCode(CommandErrorCode code, string expected)
    {
        var error = new CommandError(code, CommandErrorTarget.None);

        Assert.That(error.Message, Is.EqualTo(expected));
    }
}
