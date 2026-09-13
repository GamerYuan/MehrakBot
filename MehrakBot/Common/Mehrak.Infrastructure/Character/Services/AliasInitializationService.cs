#region

using System.Text.Json;
using Mehrak.Domain.Character;
using Mehrak.Infrastructure.Character.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Character.Services;

internal class AliasInitializationService : IHostedService
{
    private readonly ILogger<AliasInitializationService> m_Logger;
    private readonly IAliasService m_AliasService;
    private readonly string m_AssetsPath;

    public AliasInitializationService(
        ILogger<AliasInitializationService> logger,
        IAliasService aliasService,
        string? assetsPath = null)
    {
        m_Logger = logger;
        m_AliasService = aliasService;
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
            var aliasGroups = aliasJsonModel.Aliases
                .SelectMany(x => x.Alias.Select(alias =>
                    (Alias: AliasModel.NormalizeAlias(alias), CharacterName: x.Name.ReplaceLineEndings("").Trim())))
                .Where(x => x.Alias.Length > 0 && x.CharacterName.Length > 0)
                .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var conflictingGroup = aliasGroups.FirstOrDefault(group =>
                group.Select(entry => entry.CharacterName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
            if (conflictingGroup is not null)
                throw new InvalidOperationException(
                    $"Alias JSON file contains different targets for alias '{conflictingGroup.Key}'.");

            var aliases = aliasGroups.ToDictionary(
                group => group.Key,
                group => group.First().CharacterName,
                StringComparer.OrdinalIgnoreCase);

            if (aliases.Count > 0)
            {
                await m_AliasService.UpsertAliases(gameName, aliases);
            }

            m_Logger.LogDebug("Finished processing alias JSON file {FilePath}", filePath);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing alias JSON file {FilePath}", filePath);
        }
    }
}
