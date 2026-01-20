using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Zzz.Types;

public class ZzzBuddyResponse
{
    [JsonPropertyName("list")] public List<ZzzBuddyData> List { get; set; } = [];
}

public class ZzzBuddyData
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("rarity")] public required string Rarity { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("star")] public int Star { get; init; }
    [JsonPropertyName("bangboo_square_url")] public required string BangbooSquareUrl { get; init; }
    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.BuddyName, Id);
    }
    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), BangbooSquareUrl);
    }
}
