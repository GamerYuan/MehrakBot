using System.Text.Json;
using Mehrak.GameApi.Zzz.Types;

namespace Mehrak.GameApi.Tests.Zzz;

[TestFixture]
public class ZzzAssaultDataTests
{
    [Test]
    public void Deserialize_HardFields_ReadsHardFloorAndInitializesOmittedValues()
    {
        const string payload = """
            {
              "start_time": {},
              "end_time": {},
              "list": [],
              "has_hard": true,
              "hard_list": [
                {
                  "score": 10694,
                  "star": 1,
                  "boss": [],
                  "avatar_list": [],
                  "buffer": [],
                  "buddy": null
                }
              ],
              "hard_rank_percent": 200
            }
            """;

        var data = JsonSerializer.Deserialize<ZzzAssaultData>(payload);

        Assert.That(data, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data!.HasHard, Is.True);
            Assert.That(data.HardList, Has.Count.EqualTo(1));
            Assert.That(data.HardList[0].Score, Is.EqualTo(10694));
            Assert.That(data.HardRankPercent, Is.EqualTo(200));
        }

        const string omittedPayload = """
            {
              "start_time": {},
              "end_time": {},
              "list": []
            }
            """;

        var defaults = JsonSerializer.Deserialize<ZzzAssaultData>(omittedPayload);

        Assert.That(defaults, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(defaults!.HasHard, Is.False);
            Assert.That(defaults.HardList, Is.Not.Null);
            Assert.That(defaults.HardList, Is.Empty);
            Assert.That(defaults.HardRankPercent, Is.EqualTo(0));
        }
    }
}
