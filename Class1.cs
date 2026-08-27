using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

public class DMMPlugin : IPlugin
{
    // Startup and configuration can both refresh tokens and save the same settings file.
    readonly SemaphoreSlim operationGate = new(1, 1);

    public string DataDirectory => Path.Combine("PluginData", "DMM插件");
    public string SettingsFilePath => Path.Combine(DataDirectory, "settings.yaml");

    public DMMConfig.DMMLauncherInfomation LauncherInfomation { get; set; } = new();

    public DMMConfig.DMMMachineInformation MachineInformation { get; set; } = new();

    public List<DMMConfig.DMMAccountInformation> Accounts { get; set; } = [];

    public bool Enable { get; set; }

    public string LastUsedAccountName { get; set; } = string.Empty;

    public void Initialize(IPluginContext context)
    {
        DMMDisplay.SetStatusText("等待 URA 启动。");
        Directory.CreateDirectory(DataDirectory);
        LoadSettings();
        SyncToStatic();
        DMM.PluginInstance = this;
        context.Events.OnStarted(
            cancellationToken => RunOnUraStartedAsync(context.Application, cancellationToken));
    }

    async ValueTask RunOnUraStartedAsync(
        IApplication application,
        CancellationToken cancellationToken)
    {
        try
        {
            await operationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!DMMConfig.Enable)
                {
                    DMMDisplay.SetStatusText("已禁用。");
                    return;
                }

                if (DMMConfig.Accounts.Count == 0)
                {
                    DMMDisplay.SetStatusText(I18N_Download_NoAccount);
                    return;
                }

                var account = DMMConfig.Accounts.Count == 1
                    ? DMMConfig.Accounts[0]
                    : await DMMConfigDialog.SelectLaunchAccountAsync(
                        application,
                        DMMConfig.Accounts,
                        cancellationToken);
                if (account is null)
                {
                    DMMDisplay.SetStatusText(I18N_AppLaunchCanceled);
                    return;
                }

                await DMM.RunUmamusume(account);
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DMMDisplay.SetStatusText(I18N_AppLaunchCanceled);
        }
        catch (Exception ex)
        {
            var failure = string.Format(I18N_Launch_Failed, ex.Message);
            DMMDisplay.SetStatusText(failure);
            DMMDisplay.Log(ex.ToString(), UiSeverity.Error);
            DMMDisplay.Notify(failure, UiSeverity.Error);
        }
    }

    public async Task ConfigPromptAsync(
        IApplication application,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await DMMConfigDialog.EditAsync(
                application,
                DMMPluginSettings.Load(SettingsFilePath),
                cancellationToken);

            ApplySettings(result.Settings);
            SyncToStatic();
            SaveSettings();

            foreach (var association in result.SaveDataAssociations)
                association.Account.HandleFirstTimeSaveDataAssociation(association.IsCurrentAccount);

            if (result.UpdateAccount is not null)
            {
                var installDir = Path.GetDirectoryName(DMMConfig.MachineInformation.umamusume_file_path)
                    ?? throw new InvalidOperationException("赛马娘可执行文件路径没有父目录。");
                var (fileListUrl, sign, latestVersion) = await DMM.GetFileListAsync(result.UpdateAccount);
                await DMM.DownloadGameAsync(
                    result.UpdateAccount,
                    installDir,
                    fileListUrl,
                    sign,
                    latestVersion);
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    public void Dispose()
    {
        DMM.PluginInstance = null;
        operationGate.Dispose();
    }

    internal void LoadSettings()
        => ApplySettings(DMMPluginSettings.Load(SettingsFilePath));

    void ApplySettings(DMMPluginSettings settings)
    {
        LauncherInfomation = settings.LauncherInfomation;
        MachineInformation = settings.MachineInformation;
        Accounts = settings.Accounts;
        Enable = settings.Enable;
        LastUsedAccountName = settings.LastUsedAccountName;
    }

    internal void SaveSettings()
    {
        new DMMPluginSettings
        {
            LauncherInfomation = LauncherInfomation,
            MachineInformation = MachineInformation,
            Accounts = Accounts,
            Enable = Enable,
            LastUsedAccountName = LastUsedAccountName
        }.Save(SettingsFilePath);
    }

    /// <summary>将实例属性同步到 DMMConfig 静态类（供业务逻辑访问）</summary>
    internal void SyncToStatic()
    {
        DMMConfig.LauncherInfomation = LauncherInfomation;
        DMMConfig.MachineInformation = MachineInformation;
        DMMConfig.Accounts = Accounts;
        DMMConfig.Enable = Enable;
        DMMConfig.LastUsedAccountName = LastUsedAccountName;
    }

    internal void SyncFromStatic()
    {
        LauncherInfomation = DMMConfig.LauncherInfomation;
        MachineInformation = DMMConfig.MachineInformation;
        Accounts = DMMConfig.Accounts;
        Enable = DMMConfig.Enable;
        LastUsedAccountName = DMMConfig.LastUsedAccountName;
    }
}
