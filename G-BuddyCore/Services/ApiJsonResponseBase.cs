#region

using System.Text.Json.Nodes;

#endregion

namespace G_BuddyCore.Services;

public abstract class ApiJsonResponseBase
{
    public JsonObject Data { get; set; }
    public string Message { get; set; }
    public int RetCode { get; set; }
}

public class GameRecordApiResponse
{
    public JsonObject List { get; set; }
    public string Message { get; set; }
    public int RetCode { get; set; }
}

public class CharacterListPayload
{
    public string RoleId { get; set; }
    public string Server { get; set; }
    public int SortType { get; set; }
}
