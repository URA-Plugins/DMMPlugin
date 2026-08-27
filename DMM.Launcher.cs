using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static partial class DMM
{
    /// <summary>
    /// 获取游戏启动参数。若检测到版本更新，通过 pendingDownload 返回下载信息，由调用方在 Spectre.Console 交互上下文之外执行下载。
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
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Terms_Required));
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
        string executeArgs = string.Empty;
        (string fileListUrl, string sign, string latestVersion, string installDir)? pendingDownload = null;
        bool abort = false;

        await AnsiConsole.Status().StartAsync(I18N_Start_Checking, async ctx =>
        {
            var processes = Process.GetProcessesByName("umamusume");
            try
            {
                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Start_Checking_Found, processes.Length)));
                if (processes.Length == 0 || IgnoreExistProcess)
                {
                    ctx.Spinner(Spinner.Known.BouncingBar);
                    ctx.Status(I18N_Start_GetToken);

                    SaveLastAccountSaveDataIfSwitched(account);

                    if (!await EnsureAccessToken(account))
                    {
                        AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Token_CannotGetValid));
                        abort = true;
                        return;
                    }

                    var (args, error, update) = await GetExecuteArgsAsync(account);

                    if (!string.IsNullOrEmpty(error))
                    {
                        AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, error));
                        abort = true;
                        return;
                    }

                    if (string.IsNullOrEmpty(args))
                    {
                        AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_TokenFailed));
                        abort = true;
                        return;
                    }

                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_TokenGot));
                    executeArgs = args;
                    pendingDownload = update;
                }
                else
                {
                    ctx.Status(I18N_Start_Checking_AlreadyRunning);
                    abort = true;
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        });

        if (abort) return;

        // 下载必须在 AnsiConsole.Status 上下文之外执行，避免并发交互式显示冲突
        if (pendingDownload.HasValue)
        {
            var (fileListUrl, sign, latestVersion, installDir) = pendingDownload.Value;
            await DownloadGameAsync(account, installDir, fileListUrl, sign, latestVersion);
        }

        if (!LoadAccountSaveDataIfSwitched(account))
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Launch_SaveDataFailed));
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
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_SaveData_SavedForAccount, lastAccount.Name)));
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_SaveData_SaveFailed, ex.Message)));
        }
    }

    /// <summary>
    /// 如果切换了账号，加载新账号的存档到默认路径
    /// </summary>
    private static bool LoadAccountSaveDataIfSwitched(DMMAccountInformation account)
    {
        if (LastUsedAccountName == account.Account)
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_SaveData_SameAccountSkip));
            return true;
        }

        try
        {
            if (!File.Exists(account.SaveDataPath))
            {
                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_SaveData_NewArchiveWillBeCreated, Path.GetFileName(account.SaveDataPath))));
                return true;
            }

            if (File.Exists(DMMAccountInformation.DefaultSaveDataPath))
            {
                File.Copy(DMMAccountInformation.DefaultSaveDataPath, DMMAccountInformation.DefaultSaveDataPath + ".backup", true);
                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_SaveData_BackupCreated));
            }

            File.Copy(account.SaveDataPath, DMMAccountInformation.DefaultSaveDataPath, true);
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_SaveData_Loaded, account.Name).EscapeMarkup()));
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_SaveData_LoadFailed, ex.Message)));
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
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_Started));
        }
        catch (Win32Exception)
        {
            AnsiConsole.WriteLine(I18N_AppLaunchCanceled);
        }
    }
}
