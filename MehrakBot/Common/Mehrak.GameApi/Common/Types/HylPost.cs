using System.Text.Json.Serialization;

namespace Mehrak.GameApi.Common.Types;

public class HylPostWrapper
{
    [JsonPropertyName("post")] public required HylPost Post { get; set; }
}

public class HylPost
{
    [JsonPropertyName("post")] public required HylPostDetail Post { get; set; }
}

public class HylPostDetail
{
    [JsonPropertyName("post_id")] public required string PostId { get; set; }
    [JsonPropertyName("uid")] public required string Uid { get; set; }
    [JsonPropertyName("title")] public required string Title { get; set; }
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
    [JsonPropertyName("image_list")] public required List<HylImage> ImageList { get; set; }
    [JsonPropertyName("cover_list")] public required List<HylImage> CoverList { get; set; }
    [JsonPropertyName("structured_content")] public required string StructuredContent { get; set; }
}

public class HylImage
{
    [JsonPropertyName("url")] public required string Url { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("format")] public required string Format { get; set; }
    [JsonPropertyName("size")] public required string Size { get; set; }
    [JsonPropertyName("spoiler")] public bool Spoiler { get; set; }
}
