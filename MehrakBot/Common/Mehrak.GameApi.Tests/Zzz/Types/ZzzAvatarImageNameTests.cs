using Mehrak.Domain.Common;
using Mehrak.GameApi.Zzz.Types;

namespace Mehrak.GameApi.Tests.Zzz.Types;

[TestFixture]
public class ZzzBasicAvatarDataTests
{
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491_3114911.png",
        "1491_3114911")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491_3114911.png?x=1",
        "1491_3114911")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491.png",
        "1491")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491.png?x=1",
        "1491")]
    public void ToImageName_ReturnsExpectedValue(string roleSquareUrl, string expectedIdPart)
    {
        var avatar = new ZzzBasicAvatarData
        {
            Id = 1491,
            Level = 1,
            Name = "Test",
            FullName = "Test Full",
            ElementType = 0,
            CampName = "Camp",
            AvatarProfession = 0,
            Rarity = "S",
            GroupIconPath = "group.png",
            HollowIconPath = "hollow.png",
            Rank = 0,
            IsChosen = false,
            RoleSquareUrl = roleSquareUrl,
            SubElementType = 0,
            AwakenState = "0"
        };

        var actual = avatar.ToImageName();

        Assert.That(actual, Is.EqualTo(string.Format(FileNameFormat.Zzz.AvatarName, expectedIdPart)));
    }
}

[TestFixture]
public class ZzzChallengeAvatarTests
{
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491_3114911.png",
        "1491_3114911")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491_3114911.png?x=1",
        "1491_3114911")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491.png",
        "1491")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491.png?x=1",
        "1491")]
    public void ToImageName_ReturnsExpectedValue(string roleSquareUrl, string expectedIdPart)
    {
        var avatar = new ZzzChallengeAvatar
        {
            Id = 1491,
            Level = 1,
            ElementType = 0,
            AvatarProfession = 0,
            Rarity = "S",
            Rank = 0,
            RoleSquareUrl = roleSquareUrl,
            SubElementType = 0
        };

        var actual = avatar.ToImageName();

        Assert.That(actual, Is.EqualTo(string.Format(FileNameFormat.Zzz.AvatarName, expectedIdPart)));
    }
}

[TestFixture]
public class ZzzTowerAvatarTests
{
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491_3114911.png",
        "1491_3114911")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491_3114911.png?x=1",
        "1491_3114911")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491.png",
        "1491")]
    [TestCase(
        "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1491.png?x=1",
        "1491")]
    public void ToImageName_ReturnsExpectedValue(string iconUrl, string expectedIdPart)
    {
        var avatar = new ZzzTowerAvatar
        {
            AvatarId = 1491,
            Icon = iconUrl,
            Name = "Test",
            Rarity = "S",
            RankPercent = 0,
            Score = 0,
            DisplayRank = false,
            Selected = false
        };

        var actual = avatar.ToImageName();

        Assert.That(actual, Is.EqualTo(string.Format(FileNameFormat.Zzz.AvatarName, expectedIdPart)));
    }
}
