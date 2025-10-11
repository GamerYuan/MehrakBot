#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes;

public record GameRecordCardApiResponse(
    [property: JsonPropertyName("retcode")]
    int Retcode,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("data")] UserData Data
);

public record UserData(
    [property: JsonPropertyName("list")] IReadOnlyList<GameData> List
);

public record GameDataEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] int? Type,
    [property: JsonPropertyName("value")] string Value
);

public record DataSwitch(
    [property: JsonPropertyName("switch_id")]
    int? SwitchId,
    [property: JsonPropertyName("is_public")]
    bool? IsPublic,
    [property: JsonPropertyName("switch_name")]
    string SwitchName
);

public record GameData(
    [property: JsonPropertyName("has_role")]
    bool? HasRole,
    [property: JsonPropertyName("game_id")]
    int? GameId,
    [property: JsonPropertyName("game_role_id")]
    string GameRoleId,
    [property: JsonPropertyName("nickname")]
    string Nickname,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("background_image")]
    string BackgroundImage,
    [property: JsonPropertyName("is_public")]
    bool? IsPublic,
    [property: JsonPropertyName("data")] IReadOnlyList<GameDataEntry> Data,
    [property: JsonPropertyName("region_name")]
    string RegionName,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("data_switches")]
    IReadOnlyList<DataSwitch> DataSwitches,
    [property: JsonPropertyName("h5_data_switches")]
    IReadOnlyList<object> H5DataSwitches,
    [property: JsonPropertyName("background_color")]
    string BackgroundColor,
    [property: JsonPropertyName("background_image_v2")]
    string BackgroundImageV2,
    [property: JsonPropertyName("logo")] string Logo,
    [property: JsonPropertyName("game_name")]
    string Game
);