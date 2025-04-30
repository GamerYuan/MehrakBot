#region

using MongoDB.Bson.Serialization.Attributes;

#endregion

namespace MehrakCore.Models;

public class UserModel
{
    [BsonId] public ulong Id { get; set; }

    [BsonElement("ltuid")] public ulong LtUid { get; set; }

    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;

    [BsonElement("ts")] public DateTime Timestamp { get; set; }
}
