using System.Text.Json.Serialization;

namespace MehrakCore.ApiResponseTypes.Zzz;

public class ZzzChallengeAvatar
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("element_type")] public int ElementType { get; init; }
    [JsonPropertyName("avatar_profession")] public int AvatarProfession { get; init; }
    [JsonPropertyName("rarity")] public required string Rarity { get; init; }
    [JsonPropertyName("rank")] public int Rank { get; init; }
    [JsonPropertyName("role_square_url")] public required string RoleSquareUrl { get; init; }
    [JsonPropertyName("sub_element_type")] public int SubElementType { get; init; }
}

public class ZzzBuddy
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("rarity")] public required string Rarity { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("bangboo_rectangle_url")] public required string BangbooRectangleUrl { get; init; }
}

public class ZzzDefenseData
{
    [JsonPropertyName("begin_time")] public required string BeginTime { get; init; }
    [JsonPropertyName("end_time")] public required string EndTime { get; init; }
    [JsonPropertyName("rating_list")] public required List<RatingData> RatingList { get; init; }
    [JsonPropertyName("has_data")] public bool HasData { get; init; }
    [JsonPropertyName("all_floor_detail")] public required List<FloorDetail> AllFloorDetail { get; init; }
}

public class RatingData
{
    [JsonPropertyName("times")] public int Times { get; init; }
    [JsonPropertyName("rating")] public string Rating { get; init; }
}

public class FloorDetail
{
    [JsonPropertyName("layer_index")] public int LayerIndex { get; init; }
    [JsonPropertyName("rating")] public required string Rating { get; init; }
    [JsonPropertyName("node_1")] public required NodeData Node1 { get; init; }
    [JsonPropertyName("node_2")] public required NodeData Node2 { get; init; }
    [JsonPropertyName("zone_name")] public required string ZoneName { get; init; }
}

public class NodeData
{
    [JsonPropertyName("avatars")] public required List<ZzzChallengeAvatar> Avatars { get; init; }
    [JsonPropertyName("buddy")] public required ZzzBuddy? Buddy { get; init; }
    [JsonPropertyName("battle_time")] public int BattleTime { get; init; }
}
