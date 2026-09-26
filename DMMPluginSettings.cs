using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization.NamingConventions;

namespace DMMPlugin;

internal sealed class DMMPluginSettings
{
    static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
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
        DMMPluginSettings settings;
        try
        {
            var document = new YamlStream();
            document.Load(new StringReader(yaml));
            if (document.Documents.Count == 1 && document.Documents[0].RootNode is YamlMappingNode root &&
                root.Children.TryGetValue("machine_information", out var node) && node is YamlMappingNode machine)
            {
                // Released settings may contain these keys; device identity is always collected locally.
                foreach (var key in new[] { "mac_address", "hdd_serial", "motherboard", "user_os" })
                    machine.Children.Remove(key);
                using var writer = new StringWriter();
                document.Save(writer, assignAnchors: false);
                yaml = writer.ToString();
            }
            settings = Deserializer.Deserialize<DMMPluginSettings>(yaml)
                ?? throw new InvalidDataException($"DMMPlugin 配置文件为空或格式无效: {path}");
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"DMMPlugin 配置文件格式无效，文件未修改: {path}", ex);
        }

        try
        {
            foreach (var account in settings.Accounts)
                _ = account.Password;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            throw new InvalidDataException($"DMMPlugin 配置中的 password 无效，文件未修改: {path}", ex);
        }

        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serializer.Serialize(this));
    }
}
