#region

using System.Text.Json;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterInitializationService : IHostedService
{
    private readonly CharacterDbContext m_CharacterContext;
    private readonly ILogger<CharacterInitializationService> m_Logger;
    private readonly string m_AssetsPath;

    public CharacterInitializationService(
        CharacterDbContext characterContext,
        ILogger<CharacterInitializationService> logger,
        string? assetsPath = null)
    {
        m_CharacterContext = characterContext;
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

        var characterJsonFiles =
            Directory.GetFiles(m_AssetsPath, "*characters*.json", SearchOption.AllDirectories);

        if (characterJsonFiles.Length == 0)
        {
            m_Logger.LogInformation("No character JSON files found in Assets directory");
            return;
        }

        m_Logger.LogInformation("Found {Count} character JSON files", characterJsonFiles.Length);

        foreach (var jsonFile in characterJsonFiles) await ProcessCharacterJsonFile(jsonFile);
    }

    private async Task ProcessCharacterJsonFile(string jsonFilePath)
    {
        try
        {
            m_Logger.LogDebug("Processing character JSON file: {FilePath}", jsonFilePath);

            var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
            var characterJsonModel = JsonSerializer.Deserialize<CharacterJsonModel>(jsonContent);

            if (characterJsonModel == null)
            {
                m_Logger.LogWarning("Failed to deserialize character JSON file: {FilePath}", jsonFilePath);
                return;
            }

            var gameName = characterJsonModel.Game;
            var newCharacters = characterJsonModel.Characters;

            var incoming = newCharacters.ToHashSet();
            if (incoming.Count > 0)
            {
                var existing = await m_CharacterContext.Characters
                    .Where(x => x.Game == gameName && incoming.Contains(x.Name))
                    .Select(x => x.Name)
                    .ToListAsync();

                var newEntities = incoming.Except(existing).Select(name => new CharacterModel
                {
                    Game = gameName,
                    Name = name
                }).ToList();

                if (newEntities.Count > 0)
                {
                    m_Logger.LogInformation("Upserting {Count} characters for game {Game}",
                        newEntities.Count, gameName);

                    m_CharacterContext.Characters.AddRange(newEntities);
                    await m_CharacterContext.SaveChangesAsync();
                }
            }

            m_Logger.LogInformation("Processed character JSON file: {FilePath}", jsonFilePath);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error processing character JSON file: {FilePath}", jsonFilePath);
        }
    }
}
