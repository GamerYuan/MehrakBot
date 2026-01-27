using System.Text.Json.Serialization;

namespace Mehrak.GameApi.Zzz.Types;

public class ZzzTowerData
{
    [JsonPropertyName("layer_info")] public required LayerInfo LayerInfo { get; set; }
    [JsonPropertyName("mvp_info")] public required MvpInfo MvpInfo { get; set; }
    [JsonPropertyName("display_avatar_rank_list")] public required ZzzTowerAvatar DisplayAvatarRankList { get; set; }
}

public class LayerInfo
{
    [JsonPropertyName("climbing_tower_layer")] public int ClimbingTowerLayer { get; set; }
    [JsonPropertyName("total_score")] public long TotalScore { get; set; }
    [JsonPropertyName("medal_icon")] public required string MedalIcon { get; set; }
}

public class MvpInfo
{
    [JsonPropertyName("floor_mvp_num")] public int FloorMvpNum { get; set; }
    [JsonPropertyName("rank_percent")] public int RankPercent { get; set; }
}

public class ZzzTowerAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; set; }
    [JsonPropertyName("icon")] public required string Icon { get; set; }
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("rarity")] public required string Rarity { get; set; }
    [JsonPropertyName("rank_percent")] public int RankPercent { get; set; }
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("display_rank")] public bool DisplayRank { get; set; }
    [JsonPropertyName("selected")] public bool Selected { get; set; }
}
