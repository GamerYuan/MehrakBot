#region

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

#endregion

namespace MehrakCore.Models;

public class HsrRelicModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("set_names")] public required Dictionary<int, string> SetNames { get; set; }
}
