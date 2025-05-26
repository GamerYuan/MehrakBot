#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes;

public record CharacterListPayload(
    [property: JsonPropertyName("role_id")]
    string RoleId,
    [property: JsonPropertyName("server")] string Server,
    [property: JsonPropertyName("sort_type")]
    int SortType
);

public record CharacterDetailPayload(
    [property: JsonPropertyName("role_id")]
    string RoleId,
    [property: JsonPropertyName("server")] string Server,
    [property: JsonPropertyName("character_ids")]
    IReadOnlyList<uint> CharacterId
);

public record CheckInApiPayload(
    [property: JsonPropertyName("act_id")] string ActId
);
