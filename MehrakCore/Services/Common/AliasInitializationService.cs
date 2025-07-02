#region

using System.Text.Json;
using MehrakCore.Models;
using MehrakCore.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Common;

public class AliasInitializationService : IHostedService
{
    private readonly IAliasRepository m_AliasRepository;
    private readonly ILogger<AliasInitializationService> m_Logger;
    private readonly string m_AssetsPath;

    public AliasInitializationService(IAliasRepository aliasRepository, ILogger<AliasInitializationService> logger,
        string? assetsPath = null)
    {
        m_AliasRepository = aliasRepository;
        m_Logger = logger;
        m_AssetsPath = assetsPath ?? Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        m_Logger.LogInformation("Starting alias initialization from JSON files");

        try
        {
            await InitializeAliasesFromJsonFiles();
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

    private async Task InitializeAliasesFromJsonFiles()
    {
        if (!Directory.Exists(m_AssetsPath))
        {
            m_Logger.LogWarning("Assets directory not found at {AssetsPath}, skipping character initialization",
                m_AssetsPath);
            return;
        }

        // Find all character JSON files in the Assets directory and subdirectories
        var aliasJsonFiles = Directory.GetFiles(m_AssetsPath, "*aliases*.json", SearchOption.AllDirectories);

        if (aliasJsonFiles.Length == 0)
        {
            m_Logger.LogInformation("No character JSON files found in Assets directory");
            return;
        }

        m_Logger.LogInformation("Found {Count} character JSON files", aliasJsonFiles.Length);

        foreach (var file in aliasJsonFiles) await ProcessAliasJsonFileAsync(file);
    }

    private async Task ProcessAliasJsonFileAsync(string filePath)
    {
        try
        {
            m_Logger.LogInformation("Processing alias JSON file {FilePath}", filePath);

            // Read the JSON file
            var jsonContent = await File.ReadAllTextAsync(filePath);
            var aliasJsonModel = JsonSerializer.Deserialize<AliasJsonModel>(jsonContent);

            if (aliasJsonModel == null)
            {
                m_Logger.LogWarning("Failed to deserialize alias JSON file: {FilePath}", filePath);
                return;
            }

            var gameName = aliasJsonModel.Game;
            var aliases = aliasJsonModel.Aliases.SelectMany(x => x.Alias.Select(alias => (alias, x.Name)))
                .ToDictionary(x => x.alias, x => x.Name);

            var existingAlias = await m_AliasRepository.GetAliasesAsync(gameName);

            var newAliases = aliases.Where(x => !existingAlias.ContainsKey(x.Key)).ToList();

            if (newAliases.Count > 0)
            {
                m_Logger.LogInformation("Found {Count} new alias for game {GameName}, {Alias}", newAliases.Count,
                    gameName, string.Join(',', newAliases.Select(x => x.Key)));

                Dictionary<string, string> merged = [];

                foreach (var alias in newAliases.Concat(existingAlias))
                    if (!merged.ContainsKey(alias.Key))
                        merged.Add(alias.Key, alias.Value);

                var updatedModel = new AliasModel
                {
                    Game = gameName,
                    Alias = merged
                };

                await m_AliasRepository.UpsertCharacterAliasesAsync(updatedModel);
            }
            else
            {
                m_Logger.LogInformation("No missing aliases found for {GameName}, database is up to date", gameName);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing alias JSON file {FilePath}", filePath);
        }
    }
}
