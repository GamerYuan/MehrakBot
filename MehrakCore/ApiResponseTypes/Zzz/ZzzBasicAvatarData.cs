using System.Text.Json.Serialization;

namespace MehrakCore.ApiResponseTypes.Zzz;

public class ZzzBasicAvatarData : IBasicCharacterData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("name_mi18n")]
    public required string Name { get; set; }

    [JsonPropertyName("full_name_mi18n")]
    public required string FullName { get; set; }

    [JsonPropertyName("element_type")]
    public int ElementType { get; set; }

    [JsonPropertyName("camp_name_mi18n")]
    public required string CampName { get; set; }

    [JsonPropertyName("avatar_profession")]
    public int AvatarProfession { get; set; }

    [JsonPropertyName("rarity")]
    public required string Rarity { get; set; }

    [JsonPropertyName("group_icon_path")]
    public required string GroupIconPath { get; set; }

    [JsonPropertyName("hollow_icon_path")]
    public required string HollowIconPath { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("is_chosen")]
    public bool IsChosen { get; set; }

    [JsonPropertyName("role_square_url")]
    public required string RoleSquareUrl { get; set; }

    [JsonPropertyName("sub_element_type")]
    public int SubElementType { get; set; }

    [JsonPropertyName("awaken_state")]
    public required string AwakenState { get; set; }
}
