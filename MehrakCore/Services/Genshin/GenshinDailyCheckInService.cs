#region

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinDailyCheckInService : IDailyCheckInService
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinDailyCheckInService> m_Logger;

    private const string CheckInApiUrl = "https://sg-hk4e-api.hoyolab.com/event/sol/sign";
    private const string CheckInActId = "e202102251931481";

    public GenshinDailyCheckInService(IHttpClientFactory httpClientFactory, ILogger<GenshinDailyCheckInService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task CheckInAsync(IInteractionContext context, ulong ltuid, string ltoken)
    {
        var userId = context.Interaction.User.Id;
        m_Logger.LogInformation("User {UserId} is performing daily check-in for Genshin Impact", userId);
        var httpClient = m_HttpClientFactory.CreateClient("Default");
        var request = new HttpRequestMessage(HttpMethod.Post, CheckInApiUrl);
        var requestBody = new CheckInApiPayload(CheckInActId);
        request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        m_Logger.LogDebug("Sending check-in request to {Endpoint}", request.RequestUri);
        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Check-in request failed for user {UserId} with status code {StatusCode}",
                userId, response.StatusCode);
            await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .AddComponents(new TextDisplayProperties("An unknown error occurred."))
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
            return;
        }

        var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (json == null)
        {
            m_Logger.LogError("Failed to parse JSON response for user {UserId}", userId);
            await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .AddComponents(new TextDisplayProperties("An unknown error occurred."))
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
            return;
        }

        var retcode = json["retcode"]?.GetValue<int>();

        switch (retcode)
        {
            case -5003:
                m_Logger.LogInformation("User {UserId} has already checked in today", userId);
                await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .AddComponents(new TextDisplayProperties("You have already checked in today."))
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
                break;
            case 0:
                m_Logger.LogInformation("User {UserId} check-in successful", userId);
                await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .AddComponents(new TextDisplayProperties("Check in successful!"))
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
                break;
            default:
                m_Logger.LogError("Check-in failed for user {UserId} with retcode {Retcode}", userId, retcode);
                await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .AddComponents(new TextDisplayProperties("An unknown error occurred."))
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
                break;
        }
    }
}
