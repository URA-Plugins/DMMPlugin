using System.Diagnostics;
using static DMMPlugin.DMMConfig;
using UmamusumeResponseAnalyzer;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static partial class DMM
{
    internal static string ResolveGamePath(DMMMachineInformation machine, string? installationFile = null)
    {
        var gamePath = machine.umamusume_file_path;
        if (string.IsNullOrEmpty(gamePath))
        {
            try
            {
                gamePath = UraCoreHelper.FindDmmGameExecutable(installationFile);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException(string.Format(I18N_Path_DiscoveryFailed, ex.Message), ex);
            }
            return gamePath ?? throw new InvalidDataException(I18N_Path_NotFound);
        }

        if (!Path.IsPathFullyQualified(gamePath) || !File.Exists(gamePath))
            throw new InvalidDataException(string.Format(I18N_Path_ExecutableMissing, gamePath));
        return Path.GetFullPath(gamePath);
    }

    internal static string GetGameVersion(string gamePath)
        => FileVersionInfo.GetVersionInfo(gamePath).FileVersion
            ?? throw new InvalidDataException($"未能从 {gamePath} 读取游戏版本。");
}
