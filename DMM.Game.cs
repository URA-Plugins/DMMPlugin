using System.Diagnostics;
using static DMMPlugin.DMMConfig;

namespace DMMPlugin;

internal static partial class DMM
{
    internal static string GetGameVersion()
        => FileVersionInfo.GetVersionInfo(MachineInformation.umamusume_file_path).FileVersion
            ?? throw new InvalidDataException($"未能从 {MachineInformation.umamusume_file_path} 读取游戏版本。");
}
