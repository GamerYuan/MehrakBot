namespace Mehrak.Domain.Models;

public class GameRecordDto
{
    public bool HasRole { get; set; }
    public int GameId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int Level { get; set; }
    public string GameName { get; set; } = string.Empty;
}
