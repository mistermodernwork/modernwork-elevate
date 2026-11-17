using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AppForge.Core.Profiles;

/// <summary>YAML/JSON-Serialisierung und Vererbung (globale Standards → Vorlage → Profil).</summary>
public static class ProfileSerializer
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ISerializer YamlOut = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly ISerializer YamlTreeOut = new SerializerBuilder().Build();

    private static readonly IDeserializer YamlIn = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly IDeserializer YamlTreeIn = new DeserializerBuilder().Build();

    public static string ToYaml(SoftwareProfile profile) => YamlOut.Serialize(profile);

    public static SoftwareProfile FromYaml(string yaml) =>
        string.IsNullOrWhiteSpace(yaml) ? new SoftwareProfile() : YamlIn.Deserialize<SoftwareProfile>(yaml) ?? new SoftwareProfile();

    public static string ToJson(SoftwareProfile profile) => JsonSerializer.Serialize(profile, Json);

    public static SoftwareProfile FromJson(string json) => JsonSerializer.Deserialize<SoftwareProfile>(json, Json) ?? new SoftwareProfile();

    /// <summary>
    /// Legt Schichten übereinander. Spätere Schichten überschreiben frühere; Objekte werden tief
    /// zusammengeführt, Listen und Skalare ersetzt. Eine Schicht ist ein partielles YAML.
    /// </summary>
    public static SoftwareProfile Resolve(params string?[] yamlLayers)
    {
        var tree = ToTree(ToYaml(new SoftwareProfile()));
        foreach (var layer in yamlLayers)
        {
            if (!string.IsNullOrWhiteSpace(layer) && ToTree(layer) is { } overlay)
            {
                Merge(tree, overlay);
            }
        }

        return FromYaml(YamlTreeOut.Serialize(tree));
    }

    /// <summary>Nur die Abweichungen eines Profils gegenüber seiner Basis (Vorlage) als YAML.</summary>
    public static string Diff(SoftwareProfile basis, SoftwareProfile profile)
    {
        var diff = DiffTrees(ToTree(ToYaml(basis)), ToTree(ToYaml(profile)));
        return diff is null ? "{}\n" : YamlTreeOut.Serialize(diff);
    }

    private static Dictionary<string, object?> ToTree(string yaml)
    {
        var raw = YamlTreeIn.Deserialize<object?>(yaml);
        return Normalize(raw) as Dictionary<string, object?> ?? [];
    }

    private static object? Normalize(object? value) => value switch
    {
        IDictionary<object, object?> map => map.ToDictionary(
            kv => Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? string.Empty,
            kv => Normalize(kv.Value),
            StringComparer.Ordinal),
        IList<object?> list => list.Select(Normalize).ToList(),
        _ => value,
    };

    private static void Merge(Dictionary<string, object?> target, Dictionary<string, object?> overlay)
    {
        foreach (var (key, value) in overlay)
        {
            var existingKey = target.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            if (value is Dictionary<string, object?> childOverlay
                && target.TryGetValue(existingKey, out var current) && current is Dictionary<string, object?> childTarget)
            {
                Merge(childTarget, childOverlay);
            }
            else
            {
                target[existingKey] = value;
            }
        }
    }

    private static Dictionary<string, object?>? DiffTrees(Dictionary<string, object?> a, Dictionary<string, object?> b)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, bv) in b)
        {
            a.TryGetValue(key, out var av);
            if (av is Dictionary<string, object?> ao && bv is Dictionary<string, object?> bo)
            {
                if (DiffTrees(ao, bo) is { } child)
                {
                    result[key] = child;
                }
            }
            else if (YamlTreeOut.Serialize(av ?? string.Empty) != YamlTreeOut.Serialize(bv ?? string.Empty))
            {
                result[key] = bv;
            }
        }

        return result.Count == 0 ? null : result;
    }
}
