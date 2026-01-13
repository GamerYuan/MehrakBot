#region

using System.Text.Json;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

internal class AliasInitializationService : IHostedService
{
    private readonly CharacterDbContext m_CharacterContext;
    private readonly ILogger<AliasInitializationService> m_Logger;
    private readonly string m_AssetsPath;

    public AliasInitializationService(
        CharacterDbContext characterContext,
        ILogger<AliasInitializationService> logger,
        string? assetsPath = null)
    {
        m_CharacterContext = characterContext;
        m_Logger = logger;
        m_AssetsPath = assetsPath ?? Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        m_Logger.LogInformation("Starting alias initialization from JSON files");

        try
        {
            await InitializeAliasesFromJsonFiles();
            m_Logger.LogInformation("Alias initialization completed successfully");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during alias initialization");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_Logger.LogInformation("Alias initialization service stopping");
        return Task.CompletedTask;
    }

    private async Task InitializeAliasesFromJsonFiles()
    {
        if (!Directory.Exists(m_AssetsPath))
        {
            m_Logger.LogWarning(
                "Assets directory not found at {AssetsPath}, skipping alias initialization",
                m_AssetsPath);
            return;
        }

        var aliasJsonFiles = Directory.GetFiles(m_AssetsPath, "*aliases*.json", SearchOption.AllDirectories);

        if (aliasJsonFiles.Length == 0)
        {
            m_Logger.LogDebug("No alias JSON files found in Assets directory");
            return;
        }

        m_Logger.LogDebug("Found {Count} alias JSON files", aliasJsonFiles.Length);

        foreach (var file in aliasJsonFiles) await ProcessAliasJsonFileAsync(file);
    }

    private async Task ProcessAliasJsonFileAsync(string filePath)
    {
        try
        {
            m_Logger.LogDebug("Processing alias JSON file {FilePath}", filePath);

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var aliasJsonModel = JsonSerializer.Deserialize<AliasJsonModel>(jsonContent);

            if (aliasJsonModel == null)
            {
                m_Logger.LogWarning("Failed to deserialize alias JSON file: {FilePath}", filePath);
                return;
            }

            var gameName = aliasJsonModel.Game;
            var aliases = aliasJsonModel.Aliases
                .SelectMany(x => x.Alias.Select(alias => (alias, x.Name)))
                .ToDictionary(x => x.alias, x => x.Name);

            if (aliases.Count > 0)
            {
                var aliasKeys = aliases.Keys.ToList();
                var existing = await m_CharacterContext.Aliases
                    .Where(x => x.Game == gameName && aliasKeys.Contains(x.Alias))
                    .ToListAsync();

                var existingMap = existing.ToDictionary(x => x.Alias, x => x);
                int updateCount = 0, newCount = 0;

                foreach (var kvp in aliases)
                {
                    var key = kvp.Key;
                    var character = kvp.Value;

                    if (existingMap.TryGetValue(key, out var model))
                    {
                        if (!string.Equals(model.CharacterName, character, StringComparison.OrdinalIgnoreCase))
                        {
                            model.CharacterName = character;
                            m_CharacterContext.Aliases.Update(model);
                            updateCount++;
                        }
                    }
                    else
                    {
                        m_CharacterContext.Aliases.Add(new AliasModel
                        {
                            Game = gameName,
                            Alias = key,
                            CharacterName = character
                        });
                        newCount++;
                    }
                }

                if (updateCount > 0 || newCount > 0)
                {
                    await m_CharacterContext.SaveChangesAsync();
                }
            }

            m_Logger.LogDebug("Finished processing alias JSON file {FilePath}", filePath);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing alias JSON file {FilePath}", filePath);
        }
    }
}
