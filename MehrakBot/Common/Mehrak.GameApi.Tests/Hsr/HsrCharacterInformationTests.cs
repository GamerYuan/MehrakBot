using System.Text.Json;
using Mehrak.Domain.Image.Models;
using Mehrak.GameApi.Hsr.Types;

namespace Mehrak.GameApi.Tests.Hsr;

[TestFixture]
public class HsrCharacterInformationTests
{
    private const string PayloadWithExRank = """
        {
          "id": 1308,
          "level": 80,
          "name": "Acheron",
          "element": "Lightning",
          "icon": "https://example.com/avatar.png",
          "image": "https://example.com/portrait.png",
          "relics": [],
          "ornaments": [],
          "ranks": [
            {
              "id": 130801,
              "pos": 1,
              "name": "Eidolon One",
              "icon": "https://example.com/rank.png",
              "is_unlocked": true
            }
          ],
          "ex_ranks": [
            {
              "id": 130801,
              "pos": 0,
              "name": "Ex Rank",
              "icon": "https://example.com/ex_rank.png",
              "is_unlocked": false
            }
          ],
          "properties": [],
          "skills": []
        }
        """;

    private const string PayloadWithoutExRank = """
        {
          "id": 1308,
          "level": 80,
          "name": "Acheron",
          "element": "Lightning",
          "icon": "https://example.com/avatar.png",
          "image": "https://example.com/portrait.png",
          "relics": [],
          "ornaments": [],
          "ranks": [],
          "properties": [],
          "skills": []
        }
        """;

    [Test]
    public void Deserialize_WithExRank_UsesDistinctExImageKey()
    {
        var character = JsonSerializer.Deserialize<HsrCharacterInformation>(PayloadWithExRank);

        Assert.That(character, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(character!.ExRanks, Has.Count.EqualTo(1));
            Assert.That(character.ExRanks![0], Is.InstanceOf<ExRank>());
            Assert.That(character.ExRanks[0].IsUnlocked, Is.False);
            Assert.That(character.ExRanks[0].ToImageName(),
                Is.EqualTo(string.Format(FileNameFormat.Hsr.ExRankName, 130801)));
            Assert.That(character.ExRanks[0].ToImageData().Name,
                Is.EqualTo(string.Format(FileNameFormat.Hsr.ExRankName, 130801)));
            Assert.That(character.Ranks[0].ToImageName(),
                Is.EqualTo(string.Format(FileNameFormat.Hsr.RankName, 130801)));
        });
    }

    [Test]
    public void Deserialize_WithoutExRank_LeavesExRanksNull()
    {
        var character = JsonSerializer.Deserialize<HsrCharacterInformation>(PayloadWithoutExRank);

        Assert.That(character, Is.Not.Null);
        Assert.That(character!.ExRanks, Is.Null);
    }
}
