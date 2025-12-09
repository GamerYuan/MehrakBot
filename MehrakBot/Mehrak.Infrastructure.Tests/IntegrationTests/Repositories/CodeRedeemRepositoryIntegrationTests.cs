using Mehrak.Domain.Enums;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Repositories;

[TestFixture]
internal class CodeRedeemRepositoryIntegrationTests
{
    private static CodeRedeemDbContext CreateContext() => PostgresTestHelper.Instance.CreateCodeRedeemDbContext();

    private static CodeRedeemRepository CreateRepository(CodeRedeemDbContext ctx)
        => new(ctx, NullLogger<CodeRedeemRepository>.Instance);

    [Test]
    public async Task UpdateCodesAsync_AddsValidCodes()
    {
        const Game game = Game.Genshin;
        var codeA = $"GEN_{Guid.NewGuid():N}";
        var codeB = $"GEN_{Guid.NewGuid():N}";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpdateCodesAsync(game, new Dictionary<string, CodeStatus>
            {
                { codeA, CodeStatus.Valid },
                { codeB, CodeStatus.Valid }
            });
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var codes = await verificationRepo.GetCodesAsync(game);

        Assert.That(codes, Does.Contain(codeA));
        Assert.That(codes, Does.Contain(codeB));
    }

    [Test]
    public async Task UpdateCodesAsync_RemovesExpiredCodes()
    {
        const Game game = Game.HonkaiStarRail;
        var code = $"HSR_{Guid.NewGuid():N}";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpdateCodesAsync(game, new Dictionary<string, CodeStatus>
            {
                { code, CodeStatus.Valid }
            });
        }

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpdateCodesAsync(game, new Dictionary<string, CodeStatus>
            {
                { code, CodeStatus.Invalid }
            });
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var codes = await verificationRepo.GetCodesAsync(game);

        Assert.That(codes, Does.Not.Contain(code));
    }

    [Test]
    public async Task GetCodesAsync_FiltersByGame()
    {
        var genshinCode = $"GEN_{Guid.NewGuid():N}";
        var hsrCode = $"HSR_{Guid.NewGuid():N}";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpdateCodesAsync(Game.Genshin, new Dictionary<string, CodeStatus>
            {
                { genshinCode, CodeStatus.Valid }
            });
            await repo.UpdateCodesAsync(Game.HonkaiStarRail, new Dictionary<string, CodeStatus>
            {
                { hsrCode, CodeStatus.Valid }
            });
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var genshinCodes = await verificationRepo.GetCodesAsync(Game.Genshin);

        Assert.That(genshinCodes, Does.Contain(genshinCode));
        Assert.That(genshinCodes, Does.Not.Contain(hsrCode));
    }
}
