namespace Mehrak.Domain.Shared.Utility;

public enum EasingType
{
    None,
    Linear,
    InCubic,
    OutCubic,
    InOutCubic,
    InQuint,
    OutQuint,
    InOutQuint
}

public static class Easing
{
    /// <summary>
    /// Evaluates the easing curve at position t (0..1).
    /// Returns a value that goes from 1 → 0 (used as alpha multiplier for fade).
    /// </summary>
    public static float Evaluate(EasingType type, float t)
    {
        return type switch
        {
            EasingType.None => 1f,
            EasingType.Linear => 1f - t,
            EasingType.InCubic => MathF.Pow(1f - t, 3),
            EasingType.OutCubic => 1f - MathF.Pow(t, 3),
            // InOut curves are the standard Penner easeInOut mirrored to the 1 -> 0 alpha
            // contract: 1 - easeInOut(t). Mirroring keeps them consistent with the other
            // types (opaque at t=0, transparent at t=1) and continuous at the midpoint.
            EasingType.InOutCubic => t < 0.5f
                ? 1f - 4f * MathF.Pow(t, 3)
                : MathF.Pow(-2f * t + 2f, 3) / 2f,
            EasingType.InQuint => MathF.Pow(1f - t, 5),
            EasingType.OutQuint => 1f - MathF.Pow(t, 5),
            EasingType.InOutQuint => t < 0.5f
                ? 1f - 16f * MathF.Pow(t, 5)
                : MathF.Pow(-2f * t + 2f, 5) / 2f,
            _ => 1f - t
        };
    }
}
