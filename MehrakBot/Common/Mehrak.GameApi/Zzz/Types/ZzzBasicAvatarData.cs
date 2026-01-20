#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Zzz.Types;

internal class ZzzBasicAvatarResponse
{
    [JsonPropertyName("avatar_list")] public required List<ZzzBasicAvatarData> AvatarList { get; init; }
}

public class ZzzBasicAvatarData
{
    [JsonPropertyName("id")] public int Id { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("name_mi18n")] public required string Name { get; init; }

    [JsonPropertyName("full_name_mi18n")] public required string FullName { get; init; }

    [JsonPropertyName("element_type")] public int ElementType { get; init; }

    [JsonPropertyName("camp_name_mi18n")] public required string CampName { get; init; }

    [JsonPropertyName("avatar_profession")]
    public int AvatarProfession { get; init; }

    [JsonPropertyName("rarity")] public required string Rarity { get; init; }

    [JsonPropertyName("group_icon_path")] public required string GroupIconPath { get; init; }

    [JsonPropertyName("hollow_icon_path")] public required string HollowIconPath { get; init; }

    [JsonPropertyName("rank")] public int Rank { get; init; }

    [JsonPropertyName("is_chosen")] public bool IsChosen { get; init; }

    [JsonPropertyName("role_square_url")] public required string RoleSquareUrl { get; init; }

    [JsonPropertyName("sub_element_type")] public int SubElementType { get; init; }

    [JsonPropertyName("awaken_state")] public required string AwakenState { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.AvatarName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), RoleSquareUrl);
    }
}
