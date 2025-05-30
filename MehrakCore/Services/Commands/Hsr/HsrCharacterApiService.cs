#region

using System.Net;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

public class HsrCharacterApiService : ICharacterApi<HsrCharacterInformation, HsrCharacterInformation>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrCharacterApiService> m_Logger;

    private const string ApiUrl = "https://sg-public-api.hoyolab.com/event/game_record/hkrpg/api/avatar/info";

    public HsrCharacterApiService(IHttpClientFactory httpClientFactory, ILogger<HsrCharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<IEnumerable<HsrCharacterInformation>> GetAllCharactersAsync(ulong uid, string ltoken,
        string gameUid,
        string region)
    {
        var client = m_HttpClientFactory.CreateClient("Default");
        m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        HttpRequestMessage request = new()
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{ApiUrl}?server={region}&game_uid={gameUid}&need_wiki=true"),
            Headers =
            {
                { "Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}" },
                { "DS", DSGenerator.GenerateDS() }
            }
        };
        m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
        var response = await client.SendAsync(request);
        var data = await JsonSerializer.DeserializeAsync<CharacterListApiResponse>(
            await response.Content.ReadAsStreamAsync());

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Error sending character list request to {Endpoint}", request.RequestUri);
            return [];
        }

        if (data?.Data.AvatarList == null)
        {
            m_Logger.LogWarning("No character data found for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);
            return [];
        }

        m_Logger.LogInformation(
            "Successfully retrieved {Count} characters for user {Uid} on {Region} server (game UID: {GameUid})",
            data.Data.AvatarList.Count, uid, region, gameUid);

        return data.Data.AvatarList;
    }

    public async Task<ApiResult<HsrCharacterInformation>> GetCharacterDataFromIdAsync(ulong uid, string ltoken,
        string gameUid, string region, uint characterId)
    {
        m_Logger.LogInformation("Retrieving character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        var characterList = await GetAllCharactersAsync(uid, ltoken, gameUid, region);
        var character = characterList.FirstOrDefault(x => x.Id == characterId);
        if (character == null)
            return ApiResult<HsrCharacterInformation>.Failure(HttpStatusCode.BadRequest,
                $"Character with ID {characterId} not found for user {uid} on {region} server (game UID: {gameUid})");
        m_Logger.LogInformation(
            "Successfully retrieved character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        return ApiResult<HsrCharacterInformation>.Success(character);
    }

    public async Task<ApiResult<HsrCharacterInformation>> GetCharacterDataFromNameAsync(ulong uid, string ltoken,
        string gameUid, string region, string characterName)
    {
        m_Logger.LogInformation("Retrieving character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        var characterList = await GetAllCharactersAsync(uid, ltoken, gameUid, region);
        var character =
            characterList.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        if (character == null)
            return ApiResult<HsrCharacterInformation>.Failure(HttpStatusCode.BadRequest,
                $"Character with name {characterName} not found for user {uid} on {region} server (game UID: {gameUid})");
        m_Logger.LogInformation(
            "Successfully retrieved character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        return ApiResult<HsrCharacterInformation>.Success(character);
    }
}
