using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.RepresentationModel;

namespace DMMPlugin;

internal sealed class DMMPluginSettings
{
    static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    static readonly IDeserializer LegacyDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public DMMConfig.DMMLauncherInfomation LauncherInfomation { get; set; } = new();
    public DMMConfig.DMMMachineInformation MachineInformation { get; set; } = new();
    public List<DMMConfig.DMMAccountInformation> Accounts { get; set; } = [];
    public bool Enable { get; set; }
    public string LastUsedAccountName { get; set; } = string.Empty;

    public static DMMPluginSettings Load(string path)
    {
        if (!File.Exists(path))
            return new DMMPluginSettings();

        var yaml = File.ReadAllText(path);
        var settings = SelectDeserializer(yaml).Deserialize<DMMPluginSettings>(yaml);
        return settings ?? throw new InvalidDataException($"DMMPlugin 配置文件为空或格式无效: {path}");
    }

    static IDeserializer SelectDeserializer(string yaml)
    {
        using var reader = new StringReader(yaml);
        var stream = new YamlStream();
        stream.Load(reader);

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return Deserializer;

        var keys = root.Children.Keys
            .OfType<YamlScalarNode>()
            .Select(x => x.Value)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);

        return keys.Contains(nameof(LauncherInfomation))
            || keys.Contains(nameof(MachineInformation))
            || keys.Contains(nameof(Accounts))
            || keys.Contains(nameof(Enable))
            || keys.Contains(nameof(LastUsedAccountName))
            ? LegacyDeserializer
            : Deserializer;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serializer.Serialize(this));
    }
}
