#region

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Compliance.Classification;

#endregion

namespace Mehrak.GameApi.Shared;

/// <summary>
/// Renders a bounded, sanitized JSON representation of a known response model for failure diagnostics.
/// Only public properties of concrete model types are rendered. Opaque or unmodeled shapes (object, JsonNode,
/// JsonElement, dictionaries, streams, byte arrays, delegates, exceptions and framework types) are replaced by an
/// omission marker so a raw upstream payload can never bypass member classification.
/// </summary>
internal static class GameApiLogBodyRenderer
{
    public const string OmittedMarker = "<omitted>";
    public const string TruncatedMarker = "<truncated>";
    public const int MaxRenderedChars = 4096;

    private const int MaxDepth = 6;
    private const int MaxArrayItems = 10;
    private const int MaxNodes = 200;
    private const int MaxStringLength = 256;
    private const int MaxPropertyNameLength = 128;

    /// <summary>
    /// Renders <paramref name="model"/>. Never throws: an unrenderable model is reported as an omission marker.
    /// </summary>
    public static string Render(object? model)
    {
        try
        {
            if (model is null)
                return OmittedMarker;

            var modelType = model.GetType();
            if (IsOpaqueType(modelType) || IsFrameworkType(modelType))
                return OmittedMarker;

            var builder = new StringBuilder();
            var budget = new RenderBudget();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            RenderValue(builder, model, modelType, 0, budget, visited);

            if (builder.Length == 0)
                return OmittedMarker;

            var rendered = builder.ToString();
            return rendered.Length <= MaxRenderedChars
                ? rendered
                : string.Concat(rendered.AsSpan(0, MaxRenderedChars - TruncatedMarker.Length), TruncatedMarker);
        }
        catch
        {
            return OmittedMarker;
        }
    }

    private static void RenderValue(StringBuilder builder, object? value, Type declaredType, int depth,
        RenderBudget budget, HashSet<object> visited)
    {
        if (!budget.TryConsume())
        {
            WriteString(builder, TruncatedMarker);
            return;
        }

        if (value is null)
        {
            builder.Append("null");
            return;
        }

        declaredType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (declaredType == typeof(object) || IsOpaqueType(declaredType))
        {
            WriteString(builder, OmittedMarker);
            return;
        }

        if (depth > MaxDepth)
        {
            WriteString(builder, TruncatedMarker);
            return;
        }

        switch (value)
        {
            case string text:
                WriteString(builder, GameApiLog.Text(text, MaxStringLength));
                return;
            case char character:
                WriteString(builder, GameApiLog.Text(character.ToString(), MaxStringLength));
                return;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case float single:
                builder.Append(float.IsFinite(single)
                    ? single.ToString("R", CultureInfo.InvariantCulture)
                    : JsonSerializer.Serialize(single.ToString(CultureInfo.InvariantCulture)));
                return;
            case double number:
                builder.Append(double.IsFinite(number)
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : JsonSerializer.Serialize(number.ToString(CultureInfo.InvariantCulture)));
                return;
            case decimal decimalNumber:
                builder.Append(decimalNumber.ToString(CultureInfo.InvariantCulture));
                return;
            case DateTime dateTime:
                WriteString(builder, dateTime.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dateTimeOffset:
                WriteString(builder, dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateOnly dateOnly:
                WriteString(builder, dateOnly.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeOnly timeOnly:
                WriteString(builder, timeOnly.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeSpan timeSpan:
                WriteString(builder, timeSpan.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Guid guid:
                WriteString(builder, guid.ToString("D", CultureInfo.InvariantCulture));
                return;
            case Enum enumValue:
                WriteString(builder, enumValue.ToString());
                return;
            case JsonNode or JsonElement or JsonDocument:
                WriteString(builder, OmittedMarker);
                return;
            case IDictionary:
                WriteString(builder, OmittedMarker);
                return;
            case Stream or byte[] or Memory<byte> or ReadOnlyMemory<byte> or ArraySegment<byte>:
                WriteString(builder, OmittedMarker);
                return;
            case Exception or Delegate or Type:
                WriteString(builder, OmittedMarker);
                return;
        }

        if (typeof(IEnumerable).IsAssignableFrom(declaredType))
        {
            RenderEnumerable(builder, value, declaredType, depth, budget, visited);
            return;
        }

        if (IsFrameworkType(declaredType))
        {
            WriteString(builder, OmittedMarker);
            return;
        }

        RenderObject(builder, value, declaredType, depth, budget, visited);
    }

    private static void RenderEnumerable(StringBuilder builder, object value, Type declaredType, int depth,
        RenderBudget budget, HashSet<object> visited)
    {
        var elementType = GetElementType(declaredType);
        if (elementType is null || elementType == typeof(object) || IsOpaqueType(elementType))
        {
            WriteString(builder, OmittedMarker);
            return;
        }

        if (value is not IEnumerable enumerable)
        {
            WriteString(builder, OmittedMarker);
            return;
        }

        builder.Append('[');
        var written = 0;
        var truncated = false;

        foreach (var item in enumerable)
        {
            if (written >= MaxArrayItems || budget.Exhausted)
            {
                truncated = true;
                break;
            }

            if (written > 0)
                builder.Append(',');

            RenderValue(builder, item, elementType, depth + 1, budget, visited);
            written++;
        }

        if (truncated)
        {
            if (written > 0)
                builder.Append(',');
            WriteString(builder, TruncatedMarker);
        }

        builder.Append(']');
    }

    private static void RenderObject(StringBuilder builder, object value, Type declaredType, int depth,
        RenderBudget budget, HashSet<object> visited)
    {
        var trackReference = !declaredType.IsValueType;
        if (trackReference && !visited.Add(value))
        {
            WriteString(builder, OmittedMarker);
            return;
        }

        try
        {
            var properties = GetRenderableProperties(declaredType);
            if (properties.Count == 0)
            {
                WriteString(builder, OmittedMarker);
                return;
            }

            builder.Append('{');
            var written = 0;

            foreach (var property in properties)
            {
                if (budget.Exhausted)
                {
                    if (written > 0)
                        builder.Append(',');
                    WriteString(builder, TruncatedMarker);
                    builder.Append(':');
                    WriteString(builder, TruncatedMarker);
                    written++;
                    break;
                }

                if (written > 0)
                    builder.Append(',');

                WriteString(builder, GameApiLog.Text(GetPropertyName(property), MaxPropertyNameLength));
                builder.Append(':');

                var getterFailed = false;
                object? propertyValue = null;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch
                {
                    getterFailed = true;
                }

                if (getterFailed)
                {
                    WriteString(builder, OmittedMarker);
                }
                else if (IsSensitive(property))
                {
                    if (propertyValue is null)
                        builder.Append("null");
                    else
                        WriteString(builder, GameApiLog.Erase(
                            Convert.ToString(propertyValue, CultureInfo.InvariantCulture)));
                }
                else
                {
                    RenderValue(builder, propertyValue, property.PropertyType, depth + 1, budget, visited);
                }

                written++;
            }

            builder.Append('}');
        }
        finally
        {
            if (trackReference)
                visited.Remove(value);
        }
    }

    private static List<PropertyInfo> GetRenderableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => property.GetMethod is { IsPublic: true })
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true) is null)
            .ToList();
    }

    private static string GetPropertyName(PropertyInfo property)
    {
        return property.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true)?.Name ?? property.Name;
    }

    private static bool IsSensitive(PropertyInfo property)
    {
        return property.GetCustomAttribute<DataClassificationAttribute>(inherit: true) is not null;
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return candidate.GetGenericArguments()[0];
        }

        return typeof(object);
    }

    private static bool IsOpaqueType(Type type)
    {
        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument) ||
            typeof(JsonNode).IsAssignableFrom(type))
            return true;

        if (typeof(IDictionary).IsAssignableFrom(type) ||
            ImplementsGenericInterface(type, typeof(IDictionary<,>)) ||
            ImplementsGenericInterface(type, typeof(IReadOnlyDictionary<,>)))
            return true;

        if (typeof(Stream).IsAssignableFrom(type))
            return true;

        if (type == typeof(byte[]) || type == typeof(Memory<byte>) || type == typeof(ReadOnlyMemory<byte>) ||
            type == typeof(ArraySegment<byte>))
            return true;

        if (typeof(Exception).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
            typeof(Type).IsAssignableFrom(type))
            return true;

        if (typeof(System.Net.Http.HttpContent).IsAssignableFrom(type) ||
            typeof(System.Net.Http.Headers.HttpHeaders).IsAssignableFrom(type))
            return true;

        return type == typeof(Uri) || type == typeof(CancellationToken) || typeof(Task).IsAssignableFrom(type);
    }

    private static bool ImplementsGenericInterface(Type type, Type interfaceType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == interfaceType)
            return true;

        return type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == interfaceType);
    }

    private static bool IsFrameworkType(Type type)
    {
        var ns = type.Namespace;
        return ns is not null && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
                                   ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal));
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
    }

    private sealed class RenderBudget
    {
        private int m_Nodes;

        public bool Exhausted => m_Nodes > MaxNodes;

        public bool TryConsume()
        {
            m_Nodes++;
            return m_Nodes <= MaxNodes;
        }
    }
}
