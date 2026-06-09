#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Zzz.Types;

public class ZzzCharacterEntryPageData
{
    [JsonPropertyName("entry_page_id")] public required string EntryPageId { get; set; }

    [JsonPropertyName("name")] public required string Name { get; set; }
}

public class ZzzCharacterEntryPageList
{
    [JsonPropertyName("list")] public required List<ZzzCharacterEntryPageData> List { get; set; }
}
