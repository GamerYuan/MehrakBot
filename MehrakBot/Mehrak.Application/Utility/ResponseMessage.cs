namespace Mehrak.Application.Utility;

internal static class ResponseMessage
{
    public const string CharacterNotFound = "Character {0} not found. Please check the name and try again";

    public const string NoClearRecords = "No clear records found for {0}";

    public const string AuthError = "Invalid HoYoLAB UID or Cookies. Please authenticate again";

    public const string ApiError = "An error occurred while retrieving {0}. Please try again later\n" +
                                   "For repeated errors, please contact the developers";

    public const string UnknownError =
        "An unknown error occurred while processing your request. Please try again later\n" +
        "For repeated errors, please contact the developers";

    public const string CardGenError = "An error occurred while generating {0} card. Please try again later\n" +
                                       "For repeated errors, please contact the developers";

    public const string ImageUpdateError = "An error occurred while retrieving images. Please try again later\n" +
                                           "For repeated errors, please contact the developers";

    public const string ApiLimitationFooter =
        "Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.";

    public const string AttachmentStoreError =
        "An error occurred while storing the generated attachment. Please try again later\n" +
        "For repeated errors, please contact the developers";
}
