#region

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using Moq;
using NetCord;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Rest.JsonModels;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Tests.Modules;

public class ProfileCommandModuleTests
{
    private const ulong TestUserId = 123456789UL;

    private ApplicationCommandService<ApplicationCommandContext> m_CommandService;
    private GatewayClient m_DiscordClient;
    private Mock<IRestRequestHandler> m_DiscordHandlerMock;
    private UserRepository m_UserRepository;
    private MongoDbService m_MongoDbService;
    private ServiceProvider m_ServiceProvider;
    private MongoDbRunner m_MongoDbRunner;
    private IConfiguration m_Configuration;
    private List<(HttpRequestMessage Request, HttpContent? Content)> m_CapturedRequests;

    [SetUp]
    public async Task Setup()
    {
        m_CapturedRequests = new List<(HttpRequestMessage, HttpContent?)>();

        // Set up MongoDB
        m_MongoDbRunner = MongoDbRunner.Start();

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["MongoDB:ConnectionString"] = m_MongoDbRunner.ConnectionString,
            ["MongoDB:DatabaseName"] = "TestDb"
        };

        m_Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Set up Discord client with mocked handler
        m_DiscordHandlerMock = new Mock<IRestRequestHandler>();
        var options = new GatewayClientConfiguration
        {
            RestClientConfiguration = new RestClientConfiguration
            {
                RequestHandler = m_DiscordHandlerMock.Object
            }
        };

        m_DiscordClient = new GatewayClient(Mock.Of<IEntityToken>(), options);

        // Set up mock response for application commands
        var commands = new[]
        {
            new JsonApplicationCommand
            {
                Id = 1UL,
                Name = "profile",
                Description = "Manage your profile",
                Type = ApplicationCommandType.ChatInput
            }
        };

        m_DiscordHandlerMock.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequestMessage, CancellationToken>(async void (request, cancellationToken) =>
            {
                try
                {
                    HttpContent? clonedContent = null;
                    if (request.Content != null)
                    {
                        // Clone the content before it's consumed
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
            .ReturnsAsync(new HttpResponseMessage { Content = JsonContent.Create(commands) });

        // Set up command service
        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        m_CommandService.AddModule<ProfileCommandModule>();
        await m_CommandService.CreateCommandsAsync(m_DiscordClient.Rest, 123456789UL);

        // Set up real repository
        m_MongoDbService = new MongoDbService(m_Configuration, NullLogger<MongoDbService>.Instance);
        m_UserRepository = new UserRepository(m_MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up service provider
        m_ServiceProvider = new ServiceCollection()
            .AddSingleton(m_CommandService)
            .AddSingleton(m_UserRepository)
            .AddLogging(l => l.AddProvider(NullLoggerProvider.Instance))
            .BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordClient.Dispose();
        m_ServiceProvider.Dispose();
        m_MongoDbRunner.Dispose();
    }

    [Test]
    public async Task ListProfileCommand_WithProfiles_DisplaysCorrectProfiles()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, LtUid = 111111, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                },
                new()
                {
                    ProfileId = 2, LtUid = 222222, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_CapturedRequests.Clear();

        var interaction = CreateInteraction("list");

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Find interaction response request
        var responseData = await ExtractInteractionResponseDataAsync();

        // Verify the response contains both profile IDs
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("Profile 1"));
        Assert.That(responseData, Contains.Substring("111111"));
        Assert.That(responseData, Contains.Substring("Profile 2"));
        Assert.That(responseData, Contains.Substring("222222"));
    }

    [Test]
    public async Task ListProfileCommand_WithNoProfiles_DisplaysNoProfilesMessage()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>()
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_CapturedRequests.Clear();

        var interaction = CreateInteraction("list");

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        var responseData = await ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("No profile found"));
    }

    [Test]
    public async Task DeleteProfileCommand_WithSpecificId_RemovesCorrectProfile()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, LtUid = 111111, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                },
                new()
                {
                    ProfileId = 2, LtUid = 222222, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_CapturedRequests.Clear();

        // Set up delete command with profile ID 1
        var interaction = CreateInteraction("delete", 1);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify the profile with ID 1 was removed
        var updatedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(updatedUser, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(updatedUser.Profiles!.Count(), Is.EqualTo(1));
            Assert.That(updatedUser.Profiles!.First().ProfileId,
                Is.EqualTo(1u)); // The second profile should now have ID 1
            Assert.That(updatedUser.Profiles!.First().LtUid, Is.EqualTo(222222UL));
        });

        // Extract interaction response data
        var responseData = await ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("Profile 1 deleted"));
    }

    [Test]
    public async Task DeleteProfileCommand_WithNoId_RemovesAllProfiles()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, LtUid = 111111, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                },
                new()
                {
                    ProfileId = 2, LtUid = 222222, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_CapturedRequests.Clear();

        // Set up delete command with no profile ID (delete all)
        var interaction = CreateInteraction("delete");

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify user was deleted
        var deletedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(deletedUser, Is.Null);

        // Extract interaction response data
        var responseData = await ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("All profiles deleted"));
    }

    [Test]
    public async Task DeleteProfileCommand_WithNoProfiles_ShowsNoProfileMessage()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>()
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_CapturedRequests.Clear();

        var interaction = CreateInteraction("delete", 1);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        var responseData = await ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("No profile with ID 1 found"));
    }

    /// <summary>
    /// Extracts the message content from interaction response multipart form data
    /// </summary>
    private async Task<string?> ExtractInteractionResponseDataAsync()
    {
        var interactionRequest = m_CapturedRequests.FirstOrDefault(r =>
            r.Request.RequestUri?.ToString().Contains("interactions") == true);

        if (interactionRequest.Content == null)
            return null;

        var contentBytes = await interactionRequest.Content.ReadAsByteArrayAsync();
        var contentType = interactionRequest.Content.Headers.ContentType?.ToString();

        if (contentType?.StartsWith("multipart/form-data") == true)
            // Parse multipart form data
            return ParseMultipartFormDataAsync(contentBytes, contentType);

        if (contentType?.Contains("application/json") != true) return Encoding.UTF8.GetString(contentBytes);

        // Parse JSON content
        var jsonString = Encoding.UTF8.GetString(contentBytes);
        var jsonDocument = JsonDocument.Parse(jsonString);

        // Try to extract content from the JSON (adapt this based on your response structure)
        if (jsonDocument.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.TryGetProperty("content", out var contentElement))
            return contentElement.GetString();

        return jsonString;
    }

    /// <summary>
    /// Parses multipart form data to extract text content from all JSON parts
    /// </summary>
    private string? ParseMultipartFormDataAsync(byte[] contentBytes, string contentType)
    {
        // Extract boundary
        var boundaryMatch = Regex.Match(contentType, "boundary=(.+)");
        if (!boundaryMatch.Success)
            return null;

        var boundary = "--" + boundaryMatch.Groups[1].Value;
        var content = Encoding.UTF8.GetString(contentBytes);
        // Split the content by boundary
        var parts = content.Split([boundary], StringSplitOptions.RemoveEmptyEntries);
        // Look for parts with application/json content type
        var jsonContents = new List<string>();

        foreach (var part in parts)
        {
            if (part == "--\r\n" || part == "--") // End boundary
                continue;
            if (!part.Contains("Content-Type: application/json")) continue;

            // Extract the JSON payload from this part
            var jsonMatch = Regex.Match(part, @"Content-Type: application/json.+?\r\n\r\n(.+?)(?:\r\n)?$",
                RegexOptions.Singleline);
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
                    return textContent; // Return the first found text content
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
    private bool TryExtractTextFromJson(JsonElement element, out string? content)
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

    private SlashCommandInteraction CreateInteraction(string subcommand, uint? profileId = null)
    {
        var options = new List<JsonApplicationCommandInteractionDataOption>
        {
            new()
            {
                Name = subcommand,
                Type = ApplicationCommandOptionType.SubCommand
            }
        };

        // Add profile ID parameter if specified
        if (profileId.HasValue)
        {
            options[0].Options = new JsonApplicationCommandInteractionDataOption[1];

            options[0].Options![0] = new JsonApplicationCommandInteractionDataOption
            {
                Name = "profile",
                Type = ApplicationCommandOptionType.Integer,
                Value = profileId.Value.ToString()
            };
        }

        var jsonInteractionData = new JsonInteractionData
        {
            Id = 1UL,
            Name = "profile",
            Type = ApplicationCommandType.ChatInput,
            Options = options.ToArray()
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
                Id = TestUserId
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
                await m_DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id, interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordClient.Rest);
    }
}
