#region

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moq;
using NetCord;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Rest.JsonModels;

#endregion

namespace MehrakCore.Tests.TestHelpers;

/// <summary>
/// Helper class for Discord interaction testing
/// </summary>
public partial class DiscordTestHelper : IDisposable
{
    private readonly List<(HttpRequestMessage Request, HttpContent? Content)> m_CapturedRequests = [];

    private readonly JsonApplicationCommand m_Command;
    private readonly Mock<IRestRequestHandler> m_RestRequestHandlerMock;

    /// <summary>
    /// Creates a Discord Test Helper for command invocation unit testing
    /// </summary>
    /// <param name="command">The command to be created</param>
    public DiscordTestHelper(JsonApplicationCommand command)
    {
        m_Command = command;

        // Set up Discord client with mocked handler
        m_RestRequestHandlerMock = new Mock<IRestRequestHandler>();
        var options = new GatewayClientConfiguration
        {
            RestClientConfiguration = new RestClientConfiguration
            {
                RequestHandler = m_RestRequestHandlerMock.Object
            }
        };

        DiscordClient = new GatewayClient(Mock.Of<IEntityToken>(), options);

        // Set up HTTP request capture
        m_RestRequestHandlerMock.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new HttpResponseMessage { Content = JsonContent.Create(new[] { command }) });
    }

    public GatewayClient DiscordClient { get; }

    public void Dispose()
    {
        DiscordClient.Dispose();
    }

    /// <summary>
    /// Creates a mock slash command interaction
    /// Can create one level of nesting for subcommand
    /// <param name="userId">The user ID to be used</param>
    /// <param name="subcommand">The name of the subcommand to invoke</param>
    /// <param name="parameters">The parameters to be passed to the command</param>
    /// </summary>
    public SlashCommandInteraction CreateCommandInteraction(ulong userId, string? subcommand = null,
        params (string Name, object Value, ApplicationCommandOptionType Type)[] parameters)
    {
        var message = new JsonMessage
        {
            Author = new JsonUser(),
            MentionedUsers = [],
            Attachments = [],
            Embeds = []
        };
        m_RestRequestHandlerMock.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequestMessage, CancellationToken>(async void (request, cancellationToken) =>
            {
                try
                {
                    HttpContent? clonedContent = null;
                    if (request.Content != null)
                    {
                        // Clone the content before it's consumed
                        await request.Content.LoadIntoBufferAsync(cancellationToken);
                        var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                        clonedContent = new ByteArrayContent(contentBytes);

                        foreach (var header in request.Content.Headers)
                            clonedContent.Headers.Add(header.Key, header.Value);
                    }

                    m_CapturedRequests.Add((request, clonedContent));
                }
                catch (Exception)
                {
                    // no-op
                }
            })
            .ReturnsAsync(() => new HttpResponseMessage { Content = JsonContent.Create(message) });

        var subcommandOption = new JsonApplicationCommandInteractionDataOption
        {
            Name = subcommand ?? string.Empty,
            Type = ApplicationCommandOptionType.SubCommand
        };

        var option = new JsonApplicationCommandInteractionDataOption[parameters.Length];

        // Add parameters if specified
        if (parameters.Length > 0)
            for (int i = 0; i < parameters.Length; i++)
                option[i] = new JsonApplicationCommandInteractionDataOption
                {
                    Name = parameters[i].Name,
                    Type = parameters[i].Type,
                    Value = parameters[i].Value.ToString()
                };

        if (!string.IsNullOrEmpty(subcommand))
        {
            // Add subcommand options if specified
            subcommandOption.Options = option;
            option = [subcommandOption];
        }

        var jsonInteractionData = new JsonInteractionData
        {
            Id = m_Command.Id,
            Name = m_Command.Name,
            Type = ApplicationCommandType.ChatInput,
            Options = string.IsNullOrEmpty(subcommand) ? option : [subcommandOption]
        };

        var jsonInteraction = new JsonInteraction
        {
            ApplicationId = 123456789UL,
            Type = InteractionType.ApplicationCommand,
            AppPermissions = Permissions.SendMessages,
            Token = "sample_token",
            Data = jsonInteractionData,
            User = new JsonUser
            {
                Id = userId
            },
            Channel = new JsonChannel
            {
                Id = 987654321UL,
                Type = ChannelType.TextGuildChannel
            },
            Entitlements = []
        };

        return new SlashCommandInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id, interaction.Token, callback,
                    null, cancellationToken),
            DiscordClient.Rest);
    }

    /// <summary>
    /// Extracts the message content from all interaction response multipart form data
    /// and combines them into a single string
    /// </summary>
    public async Task<string> ExtractInteractionResponseDataAsync()
    {
        var interactionRequest = m_CapturedRequests.Last();

        if (interactionRequest.Request.Content == null)
            return string.Empty;

        var contentBytes = await interactionRequest.Content.ReadAsByteArrayAsync();
        var contentType = interactionRequest.Content.Headers.ContentType?.ToString();

        if (contentType?.StartsWith("multipart/form-data") == true)
            // Parse multipart form data
            return ParseMultipartFormData(contentBytes, contentType);

        if (contentType?.Contains("application/json") != true) return string.Empty;

        // Parse JSON content
        var jsonString = Encoding.UTF8.GetString(contentBytes);
        var jsonDocument = JsonDocument.Parse(jsonString);

        // Try to extract content from the JSON
        if (jsonDocument.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.TryGetProperty("content", out var contentElement))
            return contentElement.GetString() ?? string.Empty;

        return jsonString;
    }

    /// <summary>
    /// Extracts file bytes from the last interaction response's multipart form data
    /// </summary>
    /// <returns>The file bytes if found, otherwise null</returns>
    public async Task<byte[]?> ExtractInteractionResponseAsBytesAsync()
    {
        var interactionRequest = m_CapturedRequests.LastOrDefault();

        if (interactionRequest.Content == null || interactionRequest.Request.Content == null)
            return null;

        var contentBytes = await interactionRequest.Content.ReadAsByteArrayAsync();
        var contentType = interactionRequest.Content.Headers.ContentType?.ToString();

        if (contentType?.StartsWith("multipart/form-data") != true)
            return null;

        // Extract boundary
        var boundaryMatch = MultipartBoundaryRegex().Match(contentType);
        if (!boundaryMatch.Success)
            return null;

        var boundary = "--" + boundaryMatch.Groups[1].Value.Trim('"');

        // Find file boundary indices to properly extract binary data
        int[] boundaryIndices = FindAllOccurrences(contentBytes, Encoding.ASCII.GetBytes(boundary));

        for (int i = 0; i < boundaryIndices.Length - 1; i++)
        {
            // Get this part's content
            int partStart = boundaryIndices[i];
            int partEnd = boundaryIndices[i + 1];
            byte[] partBytes = new byte[partEnd - partStart];
            Array.Copy(contentBytes, partStart, partBytes, 0, partBytes.Length);

            // Convert to string to check headers
            string partText = Encoding.UTF8.GetString(partBytes);

            // Check if this is a file part
            if (partText.Contains("Content-Disposition: form-data") &&
                partText.Contains("filename="))
            {
                // Find the boundary between headers and file data
                int headerEndIndex = partText.IndexOf("\r\n\r\n");
                if (headerEndIndex > 0)
                {
                    // Calculate the start position of file bytes in the overall content
                    int fileStart = partStart + headerEndIndex + 4; // +4 for \r\n\r\n
                    int fileEnd = partEnd - 2; // -2 for \r\n at end

                    // Extract file bytes
                    int fileLength = fileEnd - fileStart;
                    byte[] fileBytes = new byte[fileLength];
                    Array.Copy(contentBytes, fileStart, fileBytes, 0, fileLength);
                    return fileBytes;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds all occurrences of a byte pattern in a byte array
    /// </summary>
    private static int[] FindAllOccurrences(byte[] source, byte[] pattern)
    {
        var positions = new List<int>();

        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
                if (source[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }

            if (found)
                positions.Add(i);
        }

        return positions.ToArray();
    }

    /// <summary>
    /// Clears the captured HTTP requests
    /// </summary>
    public void ClearCapturedRequests()
    {
        m_CapturedRequests.Clear();
    }

    /// <summary>
    /// Parses multipart form data to extract text content from all JSON parts
    /// </summary>
    private static string ParseMultipartFormData(byte[] contentBytes, string contentType)
    {
        // Extract boundary
        var boundaryMatch = MultipartBoundaryRegex().Match(contentType);
        if (!boundaryMatch.Success)
            return string.Empty;

        var boundary = "--" + boundaryMatch.Groups[1].Value;
        var content = Encoding.UTF8.GetString(contentBytes);
        // Split the content by boundary
        var parts = content.Split([boundary], StringSplitOptions.RemoveEmptyEntries);
        // Look for parts with application/json content type
        var jsonContents = new List<string>();

        foreach (var part in parts)
        {
            if (part is "--\r\n" or "--") // End boundary
                continue;
            if (!part.Contains("Content-Type: application/json")) continue;

            // Extract the JSON payload from this part
            var jsonMatch = JsonRegex().Match(part);
            if (jsonMatch.Success)
            {
                var jsonData = jsonMatch.Groups[1].Value.Trim();
                jsonContents.Add(jsonData);
            }
        }

        // Process all found JSON contents
        foreach (var jsonContent in jsonContents)
            try
            {
                var jsonDocument = JsonDocument.Parse(jsonContent);
                // Check for content or embeds in each JSON
                if (TryExtractTextFromJson(jsonDocument.RootElement, out var textContent))
                    return textContent ?? string.Empty; // Return the first found text content
            }
            catch
            {
                // If parsing fails, continue to the next JSON
            }

        // If no specific text content found, but we have JSON, return the first one
        return jsonContents.Count > 0 ? jsonContents[0] : content; // Return full content as fallback
    }

    /// <summary>
    /// Attempts to extract meaningful text content from a JSON element
    /// </summary>
    private static bool TryExtractTextFromJson(JsonElement element, out string? content)
    {
        content = null;
        // Check direct content property
        if (element.TryGetProperty("content", out var contentElement) &&
            contentElement.ValueKind == JsonValueKind.String)
        {
            content = contentElement.GetString();
            return true;
        }

        // Check data.content path
        if (!element.TryGetProperty("data", out var dataElement)) return false;

        if (dataElement.TryGetProperty("content", out var dataContentElement) &&
            dataContentElement.ValueKind == JsonValueKind.String)
        {
            content = dataContentElement.GetString();
            return true;
        }

        // Check for embeds
        if (!dataElement.TryGetProperty("embeds", out var embedsElement) ||
            embedsElement.ValueKind != JsonValueKind.Array ||
            embedsElement.GetArrayLength() <= 0) return false;

        // Try to get description from the first embed
        foreach (var embed in embedsElement.EnumerateArray())
        {
            if (embed.TryGetProperty("description", out var descElement) &&
                descElement.ValueKind == JsonValueKind.String)
            {
                content = descElement.GetString();
                return true;
            }

            // Try to get title if no description
            if (!embed.TryGetProperty("title", out var titleElement) ||
                titleElement.ValueKind != JsonValueKind.String) continue;

            content = titleElement.GetString();
            return true;
        }

        return false;
    }

    [GeneratedRegex("boundary=(.+)")]
    private static partial Regex MultipartBoundaryRegex();

    [GeneratedRegex(@"Content-Type: application/json.+?\r\n\r\n(.+?)(?:\r\n)?$", RegexOptions.Singleline)]
    private static partial Regex JsonRegex();
}
