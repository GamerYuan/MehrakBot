#region

using System.Text.Json;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterInitializationService : IHostedService
{
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<CharacterInitializationService> m_Logger;
    private readonly string m_AssetsPath;

    public CharacterInitializationService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<CharacterInitializationService> logger,
        string? assetsPath = null)
    {
        m_ServiceScopeFactory = serviceScopeFactory;
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
            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            m_Logger.LogDebug("Processing character JSON file: {FilePath}", jsonFilePath);

            var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
            var characterJsonModel = JsonSerializer.Deserialize<CharacterJson>(jsonContent);

            if (characterJsonModel == null)
            {
                m_Logger.LogWarning("Failed to deserialize character JSON file: {FilePath}", jsonFilePath);
                return;
            }

            var gameName = characterJsonModel.Game;
            var newCharacters = characterJsonModel.Characters;

            if (newCharacters.Count > 0)
            {
                var incomingNames = newCharacters.Select(c => c.Name).ToList();
                var existing = await characterContext.Characters
                    .Where(x => x.Game == gameName && incomingNames.Contains(x.Name))
                    .ToListAsync();

                var newEntities = new List<CharacterModel>();
                var updatedCount = 0;

                foreach (var incoming in newCharacters)
                {
                    var existingEntity = existing.FirstOrDefault(x => x.Name == incoming.Name);
                    if (existingEntity != null)
                    {
                        var update = false;

                        if (existingEntity.BaseVal == null && incoming.BaseHp != null)
                        {
                            existingEntity.BaseVal = incoming.BaseHp;
                            update = true;
                        }

                        if (existingEntity.MaxAscVal == null && incoming.MaxAscHp != null)
                        {
                            existingEntity.MaxAscVal = incoming.MaxAscHp;
                            update = true;
                        }

                        if (update)
                        {
                            characterContext.Characters.Update(existingEntity);
                            updatedCount++;
                        }
                    }
                    else
                    {
                        newEntities.Add(new CharacterModel
                        {
                            Game = gameName,
                            Name = incoming.Name,
                            BaseVal = incoming.BaseHp,
                            MaxAscVal = incoming.MaxAscHp
                        });
                    }
                }

                if (newEntities.Count > 0 || updatedCount > 0)
                {
                    if (newEntities.Count > 0)
                    {
                        m_Logger.LogInformation("Inserting {Count} new characters for game {Game}",
                            newEntities.Count, gameName);
                        characterContext.Characters.AddRange(newEntities);
                    }
                    if (updatedCount > 0)
                    {
                        m_Logger.LogInformation("Updating {Count} characters for game {Game}",
                            updatedCount, gameName);
                    }

                    await characterContext.SaveChangesAsync();
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
