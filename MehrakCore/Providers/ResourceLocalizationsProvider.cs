#region

using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Providers;

public class ResourceLocalizationsProvider : ILocalizationsProvider
{
    private readonly ResourceLocalizationsProviderConfiguration m_Configuration;
    private Dictionary<string, ResourceManager>? m_ResourceManagers;

    public ResourceLocalizationsProvider(
        ResourceLocalizationsProviderConfiguration? configuration = null)
    {
        m_Configuration = configuration ?? new ResourceLocalizationsProviderConfiguration();
    }

    private Dictionary<string, ResourceManager> InitializeResourceManagers()
    {
        var result = new Dictionary<string, ResourceManager>();
        // Add the base resource manager
        var baseResourceManager = new ResourceManager(
            m_Configuration.ResourceBaseName,
            m_Configuration.ResourceAssembly ?? typeof(ResourceLocalizationsProvider).Assembly);

        // Add resource managers for each culture
        foreach (var culture in m_Configuration.SupportedCultures)
            try
            {
                result.Add(culture, baseResourceManager);
            }
            catch
            {
                // no-op
            }

        return result;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>?> GetLocalizationsAsync(
        IReadOnlyList<LocalizationPathSegment> path,
        CancellationToken cancellationToken = default)
    {
        m_ResourceManagers ??= InitializeResourceManagers();

        // Build the resource key from the path
        var resourceKey = BuildResourceKey(path);

        // Get the localization for each supported culture
        var result = new Dictionary<string, string>();

        foreach (var entry in m_ResourceManagers)
        {
            var culture = entry.Key;
            var resourceManager = entry.Value;

            if (culture == "default" || culture == m_Configuration.DefaultCulture) continue;

            try
            {
                CultureInfo cultureInfo = culture == "default"
                    ? CultureInfo.InvariantCulture
                    : CultureInfo.GetCultureInfo(culture);

                var value = resourceManager.GetString(resourceKey, cultureInfo);

                if (!string.IsNullOrEmpty(value))
                    result[culture] = value;
            }
            catch (Exception ex)
            {
                // no-op
            }
        }

        return result;
    }

    private string BuildResourceKey(IReadOnlyList<LocalizationPathSegment> path)
    {
        StringBuilder keyBuilder = new();

        for (int i = 0; i < path.Count; i++)
        {
            var segment = path[i];

            if (i > 0)
                keyBuilder.Append('_');

            switch (segment)
            {
                case ApplicationCommandLocalizationPathSegment appCommand:
                    keyBuilder.Append($"Commands_{appCommand.Name}");
                    break;

                case SlashCommandGroupLocalizationPathSegment commandGroup:
                    keyBuilder.Append($"Group_{commandGroup.Name}");
                    break;

                case SubSlashCommandLocalizationPathSegment subCommand:
                    keyBuilder.Append($"SubCommand_{subCommand.Name}");
                    break;

                case SubSlashCommandGroupLocalizationPathSegment subCommandGroup:
                    keyBuilder.Append($"SubGroup_{subCommandGroup.Name}");
                    break;

                case SlashCommandParameterLocalizationPathSegment parameter:
                    keyBuilder.Append($"Parameter_{parameter.Name}");
                    break;

                case EnumLocalizationPathSegment enumSegment:
                    keyBuilder.Append($"Enum_{enumSegment.Type.Name}");
                    break;

                case EnumFieldLocalizationPathSegment enumField:
                    keyBuilder.Append($"Field_{enumField.Field.Name}");
                    break;

                case NameLocalizationPathSegment:
                    keyBuilder.Append("Name");
                    break;

                case DescriptionLocalizationPathSegment:
                    keyBuilder.Append("Description");
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported path segment type: {segment.GetType()}");
            }
        }

        return keyBuilder.ToString();
    }
}

public class ResourceLocalizationsProviderConfiguration
{
    /// <summary>
    /// The base name of the resource file (e.g., "MehrakCore.Resources.Commands.Strings")
    /// </summary>
    public string ResourceBaseName { get; init; } = "MehrakCore.Resources.Commands.Strings";

    /// <summary>
    /// The assembly containing the resource files
    /// </summary>
    public Assembly? ResourceAssembly { get; init; }

    /// <summary>
    /// List of supported cultures (e.g., "en-US", "zh-CN", "zh-TW", "ja-JP", "ko-KR")
    /// </summary>
    public List<string> SupportedCultures { get; init; } = ["en-US"];

    /// <summary>
    /// Default culture to use when no specific culture is requested
    /// </summary>
    public string DefaultCulture { get; init; } = "en-US";
}
