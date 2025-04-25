using MongoDB.Bson.Serialization.Attributes;

namespace G_BuddyCore.Models;

public class UserModel
{
    [BsonId]
    public ulong Id { get; set; }

    [BsonElement("ltuid")]
    public ulong LtUid { get; set; }

    [BsonElement("ltoken")]
    public string LToken { get; set; } = string.Empty;
}
