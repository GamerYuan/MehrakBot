namespace Mehrak.Domain.Enums;

public enum HsrEndGameMode
{
    PureFiction,
    ApocalypticShadow
}

public static class HsrEndGameModeExtensions
{
    public static string GetString(this HsrEndGameMode mode)
    {
        return mode switch
        {
            HsrEndGameMode.PureFiction => "Pure Fiction",
            HsrEndGameMode.ApocalypticShadow => "Apocalyptic Shadow",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
