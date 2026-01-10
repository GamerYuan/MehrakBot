namespace Mehrak.Application.Utility;

internal static class LogMessage
{
    public const string UnknownError = "Failed to process {Command} for User {UserId}. Error: {ErrorMessage}";

    public const string ApiError = "Failed to fetch {DataType} for User {UserId}, UID {GameUid}. Result: {@Result}";

    public const string InvalidLogin = "Invalid Ltuid or LToken for User {UserId}";

    public const string CardGenStartInfo = "Start generating {CardType} Card for User {UserId}";

    public const string CardGenSuccess =
        "Successfully generated {CardType} Card for User {UserId} in {ElapsedMilliseconds} ms";

    public const string CardGenError = "Failed to generate {CardType} Card for User {UserId}, Data:\n{Data}";

    public const string ImageUpdateError =
        "Failed to update images for Command {Command} for User {UserId}\nData:\n{Data}";

    public const string CharNotFoundInfo = "Character {CharacterName} not found for User {UserId} UID {GameUid}";

    public const string NoClearRecords = "No clear records found for {DataType} for User {UserId} UID {GameUid}";

    public const string ServiceInitialized = "{ServiceName} initialized";

    public const string AttachmentStoreError =
        "Failed to store attachment {FileName} for User {UserId}";
}
