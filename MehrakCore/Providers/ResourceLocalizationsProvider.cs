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
    private readonly ResourceManager m_ResourceManager;

    public ResourceLocalizationsProvider(
        ResourceLocalizationsProviderConfiguration? configuration = null)
    {
        m_Configuration = configuration ?? new ResourceLocalizationsProviderConfiguration();
        m_ResourceManager = new ResourceManager(m_Configuration.ResourceBaseName,
            m_Configuration.ResourceAssembly ?? typeof(ResourceLocalizationsProvider).Assembly);
    }

    public ValueTask<IReadOnlyDictionary<string, string>?> GetLocalizationsAsync(
        IReadOnlyList<LocalizationPathSegment> path,
        CancellationToken cancellationToken = default)
    {
        // Build the resource key from the path
        var resourceKey = BuildResourceKey(path);

        // Get the localization for each supported culture
        var result = new Dictionary<string, string>();

        foreach (var culture in m_Configuration.SupportedCultures)
            try
            {
                var cultureInfo = new CultureInfo(culture);

                var value = m_ResourceManager.GetString(resourceKey, cultureInfo);

                if (!string.IsNullOrEmpty(value))
                    result[culture] = value;
            }
            catch (Exception)
            {
                // no-op
            }

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(result);
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
    /// The base name of the resource file (e.g., "MehrakCore.Resources.Modules.CommandStrings")
    /// </summary>
    public string ResourceBaseName { get; init; } = "MehrakCore.Resources.Modules.CommandStrings";

    /// <summary>
    /// The assembly containing the resource files
    /// </summary>
    public Assembly? ResourceAssembly { get; init; } = null;

    /// <summary>
    /// List of supported cultures (e.g., "en-US", "zh-CN", "zh-TW", "ja-JP", "ko-KR")
    /// </summary>
    public List<string> SupportedCultures { get; init; } = ["en-US"];

    /// <summary>
    /// Default culture to use when no specific culture is requested
    /// </summary>
    public string DefaultCulture { get; init; } = "en-US";
}
