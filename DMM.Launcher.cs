using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using UmamusumeResponseAnalyzer.TerminalGui;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static partial class DMM
{
    /// <summary>
    /// 获取游戏启动参数。若检测到版本更新，通过 pendingDownload 返回下载信息。
    /// </summary>
    public static async Task<(string executeArgs, string error, (string fileListUrl, string sign, string latestVersion, string installDir)? pendingDownload)> GetExecuteArgsAsync(DMMAccountInformation account)
    {
        var jsonContent = $$"""
            {"product_id":"umamusume","game_type":"GCL","game_os":"win","launch_type":"LIB","mac_address":"{{MachineInformation.mac_address}}","hdd_serial":"{{MachineInformation.hdd_serial}}","motherboard":"{{MachineInformation.motherboard}}","user_os":"{{MachineInformation.user_os}}"}
            """.Trim();

        var launchUrl = $"{ApiBase}/r2/launch/cl";
        var json = await PostWithAuthAsync(account, launchUrl, jsonContent);
        var resultCode = json["result_code"]?.ToObject<int>() ?? 0;

        // 308: 需要同意条款
        if (resultCode == 308)
        {
            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Terms_Required),
                UiSeverity.Warning);
            if (await AgreeToTerms(account))
            {
                json = await PostWithAuthAsync(account, launchUrl, jsonContent);
                resultCode = json["result_code"]?.ToObject<int>() ?? 0;
            }
            else
            {
                return (null!, I18N_Terms_AgreeFailed, null);
            }
        }

        if (resultCode != 100)
            return (null!, string.Format(I18N_Launch_Failed, json["error"]?.ToString() ?? $"Error code {resultCode}"), null);

        (string fileListUrl, string sign, string latestVersion, string installDir)? pendingDownload = null;
        var localVersion = GetGameVersion();
        if (!string.IsNullOrEmpty(localVersion) && json["data"]!["latest_version"]?.ToString() != localVersion)
        {
            var (fileListUrl, sign, latestVersion) = await GetFileListAsync(account);
            pendingDownload = (fileListUrl, sign, latestVersion, Path.GetDirectoryName(MachineInformation.umamusume_file_path)!);
        }

        var executeArgs = json["data"]!["execute_args"]?.ToString();
        return (executeArgs ?? "", null!, pendingDownload);
    }

    /// <summary>
    /// 启动赛马娘游戏
    /// </summary>
    public static async Task RunUmamusume(DMMAccountInformation account)
    {
        var executeArgs = string.Empty;
        (string fileListUrl, string sign, string latestVersion, string installDir)? pendingDownload = null;

        DMMDisplay.SetStatusText(I18N_Start_Checking);
        var processes = Process.GetProcessesByName("umamusume");
        try
        {
            DMMDisplay.Log(string.Format(
                I18N_Start_Checking_Log,
                string.Format(I18N_Start_Checking_Found, processes.Length)));
            if (processes.Length > 0 && !IgnoreExistProcess)
            {
                DMMDisplay.SetStatusText(I18N_Start_Checking_AlreadyRunning);
                return;
            }

            DMMDisplay.SetStatusText(I18N_Start_GetToken);
            SaveLastAccountSaveDataIfSwitched(account);

            if (!await EnsureAccessToken(account))
            {
                DMMDisplay.Log(
                    string.Format(I18N_Start_Checking_Log, I18N_Token_CannotGetValid),
                    UiSeverity.Error);
                DMMDisplay.SetStatusText(I18N_Token_CannotGetValid);
                DMMDisplay.Notify(I18N_Token_CannotGetValid, UiSeverity.Error);
                return;
            }

            var (args, error, update) = await GetExecuteArgsAsync(account);

            if (!string.IsNullOrEmpty(error))
            {
                DMMDisplay.Log(
                    string.Format(I18N_Start_Checking_Log, error),
                    UiSeverity.Error);
                DMMDisplay.SetStatusText(error);
                DMMDisplay.Notify(error, UiSeverity.Error);
                return;
            }

            if (string.IsNullOrEmpty(args))
            {
                DMMDisplay.Log(
                    string.Format(I18N_Start_Checking_Log, I18N_Start_TokenFailed),
                    UiSeverity.Error);
                DMMDisplay.SetStatusText(I18N_Start_TokenFailed);
                DMMDisplay.Notify(I18N_Start_TokenFailed, UiSeverity.Error);
                return;
            }

            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Start_TokenGot),
                UiSeverity.Success);
            executeArgs = args;
            pendingDownload = update;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        if (pendingDownload.HasValue)
        {
            var (fileListUrl, sign, latestVersion, installDir) = pendingDownload.Value;
            await DownloadGameAsync(account, installDir, fileListUrl, sign, latestVersion);
        }

        if (!LoadAccountSaveDataIfSwitched(account))
        {
            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Launch_SaveDataFailed),
                UiSeverity.Error);
            DMMDisplay.SetStatusText(I18N_Launch_SaveDataFailed);
            DMMDisplay.Notify(I18N_Launch_SaveDataFailed, UiSeverity.Error);
            return;
        }

        Launch(executeArgs);
        LastUsedAccountName = account.Account;
        SavePluginConfig();
    }

    /// <summary>
    /// 如果切换了账号，保存上一个账号的存档到其专属路径
    /// </summary>
    private static void SaveLastAccountSaveDataIfSwitched(DMMAccountInformation currentAccount)
    {
        if (string.IsNullOrEmpty(LastUsedAccountName) || LastUsedAccountName == currentAccount.Account)
            return;

        var lastAccount = Accounts.FirstOrDefault(x => x.Account == LastUsedAccountName);
        if (lastAccount is null || !File.Exists(DMMAccountInformation.DefaultSaveDataPath))
            return;

        try
        {
            File.Copy(DMMAccountInformation.DefaultSaveDataPath, lastAccount.SaveDataPath, true);
            DMMDisplay.Log(string.Format(
                I18N_Start_Checking_Log,
                string.Format(I18N_SaveData_SavedForAccount, lastAccount.Name)));
        }
        catch (Exception ex)
        {
            DMMDisplay.Log(
                string.Format(
                    I18N_Start_Checking_Log,
                    string.Format(I18N_SaveData_SaveFailed, ex.Message)),
                UiSeverity.Warning);
        }
    }

    /// <summary>
    /// 如果切换了账号，加载新账号的存档到默认路径
    /// </summary>
    private static bool LoadAccountSaveDataIfSwitched(DMMAccountInformation account)
    {
        if (LastUsedAccountName == account.Account)
        {
            DMMDisplay.Log(string.Format(I18N_Start_Checking_Log, I18N_SaveData_SameAccountSkip));
            return true;
        }

        try
        {
            if (!File.Exists(account.SaveDataPath))
            {
                DMMDisplay.Log(string.Format(
                    I18N_Start_Checking_Log,
                    string.Format(
                        I18N_SaveData_NewArchiveWillBeCreated,
                        Path.GetFileName(account.SaveDataPath))));
                return true;
            }

            if (File.Exists(DMMAccountInformation.DefaultSaveDataPath))
            {
                File.Copy(DMMAccountInformation.DefaultSaveDataPath, DMMAccountInformation.DefaultSaveDataPath + ".backup", true);
                DMMDisplay.Log(string.Format(I18N_Start_Checking_Log, I18N_SaveData_BackupCreated));
            }

            File.Copy(account.SaveDataPath, DMMAccountInformation.DefaultSaveDataPath, true);
            DMMDisplay.Log(string.Format(
                I18N_Start_Checking_Log,
                string.Format(I18N_SaveData_Loaded, account.Name)));
            return true;
        }
        catch (Exception ex)
        {
            DMMDisplay.Log(
                string.Format(
                    I18N_Start_Checking_Log,
                    string.Format(I18N_SaveData_LoadFailed, ex.Message)),
                UiSeverity.Error);
            return false;
        }
    }

    static void Launch(string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = MachineInformation.umamusume_file_path,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            });
            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Start_Started),
                UiSeverity.Success);
            DMMDisplay.SetStatusText(I18N_Start_Started);
        }
        catch (Win32Exception)
        {
            DMMDisplay.Notify(I18N_AppLaunchCanceled, UiSeverity.Warning);
            DMMDisplay.SetStatusText(I18N_AppLaunchCanceled);
        }
    }
}
