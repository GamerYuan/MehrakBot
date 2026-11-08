#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.GameApi.Shared.Types;

/// <summary>
/// Availability of one game-record endpoint response. <see cref="Empty"/> and <see cref="Locked"/> are successful
/// domain outcomes without a payload; they are not failures and must never reuse <c>Unauthorized</c> or
/// <c>ExternalServerError</c>.
/// </summary>
public enum GameApiAvailability
{
    Available = 0,
    Empty = 1,
    Locked = 2
}

/// <summary>
/// Typed outcome of one game-record endpoint. A successful <see cref="Mehrak.Domain.Shared.Models.Result{T}"/> always
/// contains a non-null instance, and <see cref="GameApiAvailability.Available"/> always carries a non-null
/// <see cref="Payload"/>.
/// </summary>
public sealed class GameApiResponse<T> where T : class
{
    private GameApiResponse(GameApiAvailability availability, T? payload)
    {
        Availability = availability;
        Payload = payload;
    }

    public GameApiAvailability Availability { get; }

    /// <summary>Parsed endpoint payload; non-null exactly when <see cref="IsAvailable"/> is <see langword="true"/>.</summary>
    public T? Payload { get; }

    [MemberNotNullWhen(true, nameof(Payload))]
    public bool IsAvailable => Availability == GameApiAvailability.Available;

    public static GameApiResponse<T> Available(T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new GameApiResponse<T>(GameApiAvailability.Available, payload);
    }

    public static GameApiResponse<T> Empty()
    {
        return new GameApiResponse<T>(GameApiAvailability.Empty, null);
    }

    public static GameApiResponse<T> Locked()
    {
        return new GameApiResponse<T>(GameApiAvailability.Locked, null);
    }
}
