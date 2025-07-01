namespace MehrakCore.Models;

public class CharacterModel
{
    public GameName Game { get; set; }
    public required List<string> Characters { get; set; }
}
