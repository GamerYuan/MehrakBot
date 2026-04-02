using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mehrak.Bot.Models;

internal class HylPostStructuredInsert
{
    [JsonPropertyName("insert")] public HylPostInsertContent? Insert { get; set; }
    [JsonPropertyName("attributes")] public HylPostInsertAttributes? Attributes { get; set; }
}

[JsonConverter(typeof(HylPostInsertContentConverter))]
internal class HylPostInsertContent
{
    public string? Text { get; set; }
    public HylPostInsertImage? Image { get; set; }
    public HylPostInsertVideo? Video { get; set; }
    public HylPostInsertCard? Card { get; set; }
    public HylPostVote? Vote { get; set; }
    public HylPostEmoji? Emoji { get; set; }
}

internal class HylPostInsertImage
{
    [JsonPropertyName("image")] public required string Image { get; set; }
    [JsonPropertyName("describe")] public string? Describe { get; set; }
}

internal class HylPostInsertVideo
{
    [JsonPropertyName("video")] public required string Video { get; set; }
    [JsonPropertyName("describe")] public string? Describe { get; set; }
}

internal class HylPostVote
{
    [JsonPropertyName("vote")] public required HylPostVoteDetails Vote { get; set; }
}

internal class HylPostVoteDetails
{
    [JsonPropertyName("id")] public required string Id { get; set; }
    [JsonPropertyName("uid")] public required string Uid { get; set; }
    [JsonPropertyName("url")] public required string Url { get; set; }
    [JsonPropertyName("title")] public required string Title { get; set; }
    [JsonPropertyName("vote_options")] public required List<string> VoteOptions { get; set; }
    [JsonPropertyName("vote_limit")] public int VoteLimit { get; set; }
    [JsonPropertyName("end_time")] public required string EndTime { get; set; }
    [JsonPropertyName("end_time_type")] public required string EndTimeType { get; set; }
    [JsonPropertyName("sync_end_time_type")] public bool SyncEndTimeType { get; set; }
    [JsonPropertyName("status")] public required string Status { get; set; }
}

internal class HylPostInsertCard
{
    [JsonPropertyName("card_group")] public required HylPostInsertCardGroup CardGroup { get; set; }
}

internal class HylPostInsertCardGroup
{
    [JsonPropertyName("article_cards")] public required List<HylPostArticleCard> ArticleCards { get; set; }
}

internal class HylPostArticleCard
{
    [JsonPropertyName("meta")] public required HylPostMetadata Meta { get; set; }
    [JsonPropertyName("info")] public required HylPostArticleInfo Info { get; set; }
    [JsonPropertyName("user")] public required HylPostArticleUser User { get; set; }
}

internal class HylPostEmoji
{
    [JsonPropertyName("backup")] public required HylPostBackup Backup { get; set; }
    [JsonPropertyName("emoticon")] public required HylPostEmoticon Emoticon { get; set; }
}

internal class HylPostBackup
{
    [JsonPropertyName("type")] public required string Type { get; set; }
    [JsonPropertyName("text")] public required string Text { get; set; }
}

internal class HylPostEmoticon
{
    [JsonPropertyName("id")] public required string Id { get; set; }
    [JsonPropertyName("package_id")] public required string PackageId { get; set; }
    [JsonPropertyName("url")] public required string Url { get; set; }
    [JsonPropertyName("type")] public required string Type { get; set; }
}

internal class HylPostInsertAttributes
{
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("bold")] public bool? Bold { get; set; }
    [JsonPropertyName("list")] public string? List { get; set; }
    [JsonPropertyName("header")] public int? Header { get; set; }
    [JsonPropertyName("align")] public string? Align { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("background")] public string? Background { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
}

internal class HylPostMetadata
{
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("meta_id")] public required string MetaId { get; set; }
    [JsonPropertyName("origin_url")] public required string OriginUrl { get; set; }
}

internal class HylPostArticleInfo
{
    [JsonPropertyName("title")] public required string Title { get; set; }
    [JsonPropertyName("cover")] public required string Cover { get; set; }
    [JsonPropertyName("has_cover")] public bool HasCover { get; set; }
    [JsonPropertyName("view_num")] public required string ViewNum { get; set; }
    [JsonPropertyName("created_at")] public required string CreatedAt { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("tip_msg")] public required string TipMsg { get; set; }
    [JsonPropertyName("type_desc")] public required string TypeDesc { get; set; }
    [JsonPropertyName("view_type")] public int ViewType { get; set; }
    [JsonPropertyName("sub_type")] public int SubType { get; set; }
    [JsonPropertyName("jump_url")] public required string JumpUrl { get; set; }
}

internal class HylPostArticleUser
{
    [JsonPropertyName("uid")] public required string Uid { get; set; }
    [JsonPropertyName("avatar")] public required string Avatar { get; set; }
    [JsonPropertyName("icon_url")] public required string IconUrl { get; set; }
    [JsonPropertyName("nickname")] public required string Nickname { get; set; }
    [JsonPropertyName("is_owner")] public bool IsOwner { get; set; }
}

internal class HylPostInsertContentConverter : JsonConverter<HylPostInsertContent>
{
    public override HylPostInsertContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new HylPostInsertContent { Text = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return null!;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;

        if (element.TryGetProperty("image", out _))
        {
            return new HylPostInsertContent
            {
                Image = JsonSerializer.Deserialize<HylPostInsertImage>(element, options)
            };
        }

        if (element.TryGetProperty("video", out _))
        {
            return new HylPostInsertContent
            {
                Video = JsonSerializer.Deserialize<HylPostInsertVideo>(element, options)
            };
        }

        if (element.TryGetProperty("card_group", out _))
        {
            return new HylPostInsertContent
            {
                Card = JsonSerializer.Deserialize<HylPostInsertCard>(element, options)
            };
        }

        if (element.TryGetProperty("vote", out _))
        {
            return new HylPostInsertContent
            {
                Vote = JsonSerializer.Deserialize<HylPostVote>(element, options)
            };
        }

        if (element.TryGetProperty("backup", out _) && element.TryGetProperty("emoticon", out _))
        {
            return new HylPostInsertContent
            {
                Emoji = JsonSerializer.Deserialize<HylPostEmoji>(element, options)
            };
        }

        return null!;
    }

    public override void Write(Utf8JsonWriter writer, HylPostInsertContent value, JsonSerializerOptions options)
    {
        if (value.Text != null)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        if (value.Image != null)
        {
            JsonSerializer.Serialize(writer, value.Image, options);
            return;
        }

        if (value.Video != null)
        {
            JsonSerializer.Serialize(writer, value.Video, options);
            return;
        }

        if (value.Card != null)
        {
            JsonSerializer.Serialize(writer, value.Card, options);
            return;
        }

        if (value.Vote != null)
        {
            JsonSerializer.Serialize(writer, value.Vote, options);
            return;
        }

        if (value.Emoji != null)
        {
            JsonSerializer.Serialize(writer, value.Emoji, options);
            return;
        }

        writer.WriteNullValue();
    }
}
