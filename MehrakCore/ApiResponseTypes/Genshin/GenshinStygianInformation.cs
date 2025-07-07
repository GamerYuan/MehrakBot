#region

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class StygianBestRecord
{
    [JsonPropertyName("difficulty")] public int Difficulty { get; init; }

    [JsonPropertyName("second")] public int Second { get; init; }

    [JsonPropertyName("icon")] public required string Icon { get; init; }
}

public class StygianBestAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; init; }

    [JsonPropertyName("side_icon")] public required string SideIcon { get; init; }

    [JsonPropertyName("dps")] public required string Dps { get; init; }

    [JsonPropertyName("type")] public int Type { get; init; }
}

public class Challenge
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("second")] public int Second { get; init; }

    [JsonPropertyName("teams")] public required List<StygianAvatar> Teams { get; init; }

    [JsonPropertyName("best_avatar")] public required List<StygianBestAvatar> BestAvatar { get; init; }

    [JsonPropertyName("monster")] public required Monster Monster { get; init; }
}

public class GenshinStygianInformation
{
    [MemberNotNullWhen(true, nameof(IsUnlock))]
    [JsonPropertyName("data")]
    public List<StygianData>? Data { get; init; }

    [JsonPropertyName("is_unlock")] public bool IsUnlock { get; init; }
}

public class Monster
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("icon")] public required string Icon { get; init; }

    [JsonPropertyName("monster_id")] public int MonsterId { get; init; }
}

public class StygianSchedule
{
    [JsonPropertyName("schedule_id")] public required string ScheduleId { get; init; }

    [JsonPropertyName("start_time")] public required string StartTime { get; init; }

    [JsonPropertyName("end_time")] public required string EndTime { get; init; }

    [JsonPropertyName("is_valid")] public bool IsValid { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }
}

public class StygianChallengeData
{
    [MemberNotNullWhen(true, nameof(HasData))]
    [JsonPropertyName("best")]
    public StygianBestRecord? StygianBestRecord { get; init; }

    [JsonPropertyName("challenge")] public List<Challenge>? Challenge { get; init; }

    [JsonPropertyName("has_data")] public bool HasData { get; init; }
}

public class StygianData
{
    [JsonPropertyName("schedule")] public StygianSchedule? Schedule { get; init; }
    [JsonPropertyName("single")] public required StygianChallengeData Single { get; init; }
    [JsonPropertyName("mp")] public required StygianChallengeData Multi { get; init; }
}

public class StygianAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("element")] public string? Element { get; init; }

    [JsonPropertyName("image")] public required string Image { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    [JsonPropertyName("rank")] public int Rank { get; init; }
}
