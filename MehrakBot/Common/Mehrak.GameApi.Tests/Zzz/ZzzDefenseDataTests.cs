using System.Text.Json;
using Mehrak.GameApi.Zzz.Types;

namespace Mehrak.GameApi.Tests.Zzz;

[TestFixture]
public class ZzzDefenseDataTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Deserialize_WithOrWithoutChallengeTime_ReadsDefenseBrief(bool includeChallengeTime)
    {
        var challengeTime = includeChallengeTime ? "\"challenge_time\": {}," : string.Empty;
        var payload = $$"""
            {
              "hadal_ver": "2",
              "nick_name": "Test",
              "icon": "",
              "hadal_info_v2": {
                "hadal_begin_time": {},
                "hadal_end_time": {},
                "begin_time": "0",
                "end_time": "0",
                "brief": {
                  "cur_period_zone_layer_count": 5,
                  "score": 10000,
                  "rank_percent": 100,
                  "rating": "S",
                  {{challengeTime}}
                  "max_score": 20000
                }
              }
            }
            """;

        var data = JsonSerializer.Deserialize<ZzzDefenseDataWrapper>(payload);

        Assert.That(data, Is.Not.Null);
        var brief = data!.HadalInfoV2.Brief;
        Assert.That(brief, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(brief!.ZoneLayerCount, Is.EqualTo(5));
            Assert.That(brief.Score, Is.EqualTo(10000));
            Assert.That(brief.RankPercent, Is.EqualTo(100));
            Assert.That(brief.Rating, Is.EqualTo("S"));
            Assert.That(brief.MaxScore, Is.EqualTo(20000));
        }
    }
}
