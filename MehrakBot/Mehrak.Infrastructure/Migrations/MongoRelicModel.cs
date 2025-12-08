using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Mehrak.Infrastructure.Migrations;

[Obsolete]
public class MongoRelicModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Each document stores a single mapping: set_id -> set_name
    [BsonElement("set_id")] public int SetId { get; set; }

    [BsonElement("set_name")] public required string SetName { get; set; }
}
