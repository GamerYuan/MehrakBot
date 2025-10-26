#region

using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterInitializationService : IHostedService
{
    private readonly ICharacterRepository m_CharacterRepository;
    private readonly ILogger<CharacterInitializationService> m_Logger;
    private readonly string m_AssetsPath;

    public CharacterInitializationService(
        ICharacterRepository characterRepository,
        ILogger<CharacterInitializationService> logger,
        string? assetsPath = null)
    {
        m_CharacterRepository = characterRepository;
        m_Logger = logger;
        m_AssetsPath = assetsPath ?? Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        m_Logger.LogInformation("Starting character initialization from JSON files");

        try
        {
            await InitializeCharactersFromJsonFiles();
            m_Logger.LogInformation("Character initialization completed successfully");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during character initialization");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_Logger.LogInformation("Character initialization service stopping");
        return Task.CompletedTask;
    }

    private async Task InitializeCharactersFromJsonFiles()
    {
        if (!Directory.Exists(m_AssetsPath))
        {
            m_Logger.LogWarning("Assets directory not found at {AssetsPath}, skipping character initialization",
                m_AssetsPath);
            return;
        }

        string[] characterJsonFiles =
            Directory.GetFiles(m_AssetsPath, "*characters*.json", SearchOption.AllDirectories);

        if (characterJsonFiles.Length == 0)
        {
            m_Logger.LogInformation("No character JSON files found in Assets directory");
            return;
        }

        m_Logger.LogInformation("Found {Count} character JSON files", characterJsonFiles.Length);

        foreach (string jsonFile in characterJsonFiles) await ProcessCharacterJsonFile(jsonFile);
    }

    private async Task ProcessCharacterJsonFile(string jsonFilePath)
    {
        try
        {
            m_Logger.LogDebug("Processing character JSON file: {FilePath}", jsonFilePath);

            string jsonContent = await File.ReadAllTextAsync(jsonFilePath);
            var characterJsonModel = JsonSerializer.Deserialize<CharacterJsonModel>(jsonContent);

            if (characterJsonModel == null)
            {
                m_Logger.LogWarning("Failed to deserialize character JSON file: {FilePath}", jsonFilePath);
                return;
            }

            var gameName = characterJsonModel.GetGame();
            var newCharacters = characterJsonModel.Characters;

            var existingModel = await m_CharacterRepository.GetCharacterModelAsync(gameName);
            var existingCharacters = existingModel?.Characters ?? [];

            var missingCharacters = newCharacters.Except(existingCharacters, StringComparer.OrdinalIgnoreCase).ToList();

            if (missingCharacters.Count > 0)
            {
                m_Logger.LogInformation(
                    "Found {Count} missing characters for {Game}: {Characters}",
                    missingCharacters.Count,
                    gameName,
                    string.Join(", ", missingCharacters));

                var mergedCharacters = new List<string>();

                foreach (string character in newCharacters)
                    if (!mergedCharacters.Contains(character, StringComparer.OrdinalIgnoreCase))
                        mergedCharacters.Add(character);

                foreach (string existingChar in existingCharacters)
                    if (!mergedCharacters.Contains(existingChar, StringComparer.OrdinalIgnoreCase))
                    {
                        mergedCharacters.Add(existingChar);
                        m_Logger.LogDebug("Preserving manually added character: {Character}", existingChar);
                    }

                var updatedModel = new CharacterModel
                {
                    Game = gameName,
                    Characters = mergedCharacters
                };

                await m_CharacterRepository.UpsertCharactersAsync(updatedModel);

                m_Logger.LogInformation(
                    "Successfully updated character database for {Game} with {TotalCount} characters",
                    gameName,
                    mergedCharacters.Count);
            }
            else
            {
                m_Logger.LogInformation("No missing characters found for {Game}, database is up to date", gameName);
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error processing character JSON file: {FilePath}", jsonFilePath);
        }
    }
}