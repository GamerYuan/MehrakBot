using System.Globalization;
using Mehrak.Application.Extensions.Genshin;
using Mehrak.GameApi.Genshin.Types;

namespace Mehrak.Application.Tests.Extensions.Genshin;

public class GenshinCharacterExtensionsTests
{
    [Test]
    public void TryGetAscensionLevelCap_LevelAbove90_ReturnsCurrentLevel()
    {
        var charData = CreateCharacter(level: 91, rarity: 5, statValue: 0f);

        var result = charData.TryGetAscensionLevelCap(baseVal: null, maxAscVal: null, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(ascLevelCap, Is.EqualTo(91));
        }
    }

    [Test]
    public void TryGetAscensionLevelCap_NonBoundaryBelow20_Returns20()
    {
        var charData = CreateCharacter(level: 1, rarity: 4, statValue: 0f);

        var result = charData.TryGetAscensionLevelCap(baseVal: null, maxAscVal: null, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(ascLevelCap, Is.EqualTo(20));
        }
    }

    [Test]
    public void TryGetAscensionLevelCap_NonBoundaryLevel_ReturnsNextAscensionCap()
    {
        var charData = CreateCharacter(level: 65, rarity: 4, statValue: 0f);

        var result = charData.TryGetAscensionLevelCap(baseVal: null, maxAscVal: null, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(ascLevelCap, Is.EqualTo(70));
        }
    }

    [Test]
    public void TryGetAscensionLevelCap_BoundaryLevelWithoutStats_ReturnsFalse()
    {
        var charData = CreateCharacter(level: 20, rarity: 5, statValue: 0f);

        var result = charData.TryGetAscensionLevelCap(baseVal: null, maxAscVal: 100f, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(ascLevelCap, Is.Null);
        }
    }

    [Test]
    public void TryGetAscensionLevelCap_BoundaryLevelValidStats_ReturnsClosestCap()
    {
        const float baseVal = 10f;
        const float maxAscVal = 100f;

        var boundaryMultiplier = 5.176f;
        var expectedAscensionMultiplier = 128f / 182f;
        var statValue = baseVal * boundaryMultiplier + maxAscVal * expectedAscensionMultiplier;
        var charData = CreateCharacter(level: 50, rarity: 5, statValue: statValue);

        var result = charData.TryGetAscensionLevelCap(baseVal, maxAscVal, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(ascLevelCap, Is.EqualTo(70));
        }
    }

    [Test]
    public void TryGetAscensionLevelCap_BoundaryLevelNegativeAscensionValue_ReturnsFalse()
    {
        const float baseVal = 100f;
        const float maxAscVal = 100f;
        var charData = CreateCharacter(level: 20, rarity: 5, statValue: 200f);

        var result = charData.TryGetAscensionLevelCap(baseVal, maxAscVal, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(ascLevelCap, Is.Null);
        }
    }

    [Test]
    public void TryGetAscensionLevelCap_BoundaryLevelAscensionMultiplierTooFar_ReturnsFalse()
    {
        const float baseVal = 100f;
        const float maxAscVal = 100f;
        var statValue = baseVal * 2.594f + maxAscVal * 0.13f;
        var charData = CreateCharacter(level: 20, rarity: 5, statValue: statValue);

        var result = charData.TryGetAscensionLevelCap(baseVal, maxAscVal, out var ascLevelCap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(ascLevelCap, Is.Null);
        }
    }

    private static GenshinCharacterInformation CreateCharacter(int level, int rarity, float statValue)
    {
        var stat = statValue.ToString(CultureInfo.CurrentCulture);

        return new GenshinCharacterInformation
        {
            Base = new BaseCharacterDetail
            {
                Id = 10000002,
                Icon = "https://avatar.icon",
                Name = "Test Character",
                Level = level,
                Rarity = rarity,
                Image = "https://avatar.image",
                Weapon = new Weapon
                {
                    Id = 11401,
                    Icon = "https://weapon.icon",
                    Name = "Test Weapon"
                }
            },
            Weapon = new WeaponDetail
            {
                Id = 11401,
                Icon = "https://weapon.icon",
                Name = "Test Weapon",
                TypeName = "Sword",
                MainProperty = new StatProperty
                {
                    Base = "0",
                    Final = "0"
                }
            },
            Relics = [],
            Constellations = [],
            SelectedProperties = [],
            BaseProperties =
            [
                new StatProperty
                {
                    PropertyType = 2000,
                    Base = stat,
                    Final = stat
                }
            ],
            ExtraProperties = [],
            ElementProperties = [],
            Skills = []
        };
    }
}
