using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.CodeRedeem;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.CodeRedeem;

[TestFixture]
public class CodeRedeemApiServiceTests
{
    [TestCase(0, RedemptionOutcome.Redeemed, CodeStatus.Valid)]
    [TestCase(-2001, RedemptionOutcome.Expired, CodeStatus.Invalid)]
    [TestCase(-2003, RedemptionOutcome.Invalid, CodeStatus.Invalid)]
    [TestCase(-2016, RedemptionOutcome.Cooldown, CodeStatus.Valid)]
    [TestCase(-2017, RedemptionOutcome.AlreadyUsed, CodeStatus.Valid)]
    public async Task Retcode_ProducesSemanticOutcomeAndPreservesCacheClassification(
        int retcode, RedemptionOutcome outcome, CodeStatus status)
    {
        var handler = new FakeHttpMessageHandler();
        // Deliberately unrelated message: semantics must be based only on the retcode.
        handler.EnqueueJson($$"""{"retcode":{{retcode}},"message":"untrusted unrelated text","data":null}""");
        var service = new CodeRedeemApiService(handler.ToHttpClientFactory(), new Mock<ILogger<CodeRedeemApiService>>().Object);
        var result = await service.GetAsync(new(1, 2, "synthetic-token", "800000001", "os_asia", Game.Genshin, "TESTCODE"));
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data.Outcome, Is.EqualTo(outcome));
            Assert.That(result.Data.Status, Is.EqualTo(status));
            Assert.That(result.Data.Message, Does.Not.Contain("untrusted"));
        });
    }

    [TestCase(-100, StatusCode.Unauthorized)]
    [TestCase(12345, StatusCode.ExternalServerError)]
    public async Task UnsupportedOrUnauthorizedRetcode_DoesNotInventSuccessfulOutcome(int retcode, StatusCode status)
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$"""{"retcode":{{retcode}},"message":"credential or unsafe details","data":null}""");
        var service = new CodeRedeemApiService(handler.ToHttpClientFactory(), new Mock<ILogger<CodeRedeemApiService>>().Object);
        var result = await service.GetAsync(new(1, 2, "synthetic-token", "800000001", "os_asia", Game.Genshin, "TESTCODE"));
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(result.ErrorMessage, Does.Not.Contain("credential or unsafe details"));
        });
    }
}
