#region

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class StygianBestRecord
{
    [JsonPropertyName("difficulty")] public int Difficulty { get; set; }

    [JsonPropertyName("second")] public int Second { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }
}

public class StygianBestAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; set; }

    [JsonPropertyName("side_icon")] public required string SideIcon { get; set; }

    [JsonPropertyName("dps")] public required string Dps { get; set; }

    [JsonPropertyName("type")] public int Type { get; set; }
}

public class Challenge
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("second")] public int Second { get; set; }

    [JsonPropertyName("teams")] public required List<StygianAvatar> Teams { get; set; }

    [JsonPropertyName("best_avatar")] public required List<StygianBestAvatar> BestAvatar { get; set; }

    [JsonPropertyName("monster")] public required Monster Monster { get; set; }
}

public class GenshinStygianInformation
{
    [JsonPropertyName("data")] public List<StygianData>? Data { get; set; }

    [JsonPropertyName("is_unlock")] public bool IsUnlock { get; set; }
}

public class Monster
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }

    [JsonPropertyName("monster_id")] public int MonsterId { get; set; }
}

public class StygianSchedule
{
    [JsonPropertyName("schedule_id")] public required string ScheduleId { get; set; }

    [JsonPropertyName("start_time")] public required string StartTime { get; set; }

    [JsonPropertyName("end_time")] public required string EndTime { get; set; }

    [JsonPropertyName("is_valid")] public bool IsValid { get; set; }

    [JsonPropertyName("name")] public required string Name { get; set; }
}

public class StygianData
{
    [JsonPropertyName("schedule")] public StygianSchedule? Schedule { get; set; }

    [MemberNotNullWhen(true, nameof(HasData))]
    [JsonPropertyName("best")]
    public StygianBestRecord? StygianBestRecord { get; set; }

    [MemberNotNullWhen(true, nameof(HasData))]
    [JsonPropertyName("challenge")]
    public List<Challenge>? Challenge { get; set; }

    [JsonPropertyName("has_data")] public bool HasData { get; set; }
}

public class StygianAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("element")] public string? Element { get; set; }

    [JsonPropertyName("image")] public string? Image { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("rarity")] public int Rarity { get; set; }

    [JsonPropertyName("rank")] public int Rank { get; set; }
}
