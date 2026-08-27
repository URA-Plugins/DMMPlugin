using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Net;
using System.Text;
using UmamusumeResponseAnalyzer.Plugin;
using static DMMPlugin.DMMConfig;

namespace DMMPlugin;

internal static partial class DMM
{
    private static string GlobalGameManagersPath => Path.Combine(Path.GetDirectoryName(MachineInformation.umamusume_file_path)!, "umamusume_Data", "globalgamemanagers");
    // https://github.com/AssetRipper/Tpk/blob/master/README.md
    private static string ClassPackagePath => Path.Combine("PluginData", PluginInstance!.Name, "lzma.tpk");
    internal static string GetGameVersion()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassPackagePath);
        try
        {
            var inst = manager.LoadAssetsFile(GlobalGameManagersPath, true);
            manager.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);

            foreach (var asset in inst.file.GetAssetsOfType(AssetClassID.PlayerSettings))
            {
                var baseField = manager.GetBaseField(inst, asset);

                var versionField = baseField["bundleVersion"];

                if (!versionField.IsDummy)
                {
                    System.Diagnostics.Trace.WriteLine(sw.ElapsedMilliseconds);
                    return versionField.AsString;
                }
                else
                {
                    Console.WriteLine("未能找到 bundleVersion 字段。");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
        }
        finally
        {
            manager.UnloadAllAssetsFiles();
        }
        return string.Empty;
    }
}
