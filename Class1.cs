using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.IO.Compression;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using static DMMPlugin.i18n.DMM;
using static UmamusumeResponseAnalyzer.Plugin.UraEvents;

namespace DMMPlugin;

public class DMMPlugin : IPlugin
{
    public string Name => "DMM插件";
    public string Author => "Lipi";
    public string[] Targets => ["Cygames"];

    [PluginSetting]
    public DMMConfig.DMMLauncherInfomation LauncherInfomation { get; set; } = new();

    [PluginSetting]
    public DMMConfig.DMMMachineInformation MachineInformation { get; set; } = new();

    [PluginSetting]
    public List<DMMConfig.DMMAccountInformation> Accounts { get; set; } = [];

    [PluginSetting]
    public bool Enable { get; set; }

    [PluginSetting]
    public string LastUsedAccountName { get; set; } = string.Empty;

    public void Initialize()
    {
        Directory.CreateDirectory(Path.Combine("PluginData", Name));
        PluginSettingsManager.LoadSettings(this);
        SyncToStatic();
        DMM.PluginInstance = this;
        OnStarted += OnUraStarted;
    }

    private async Task OnUraStarted()
    {
        if (!DMMConfig.Enable || DMMConfig.Accounts.Count == 0) return;

        if (DMMConfig.Accounts.Count == 1)
        {
            await DMM.RunUmamusume(DMMConfig.Accounts[0]);
        }
        else
        {
            var choices = DMMConfig.Accounts.Select(x => x.Name).ToList();
            choices.Add(I18N_Cancel);

            var prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title(I18N_MultipleAccountsFound)
                .WrapAround(true)
                .AddChoices(choices));

            if (prompt == I18N_Cancel) return;

            var account = DMMConfig.Accounts.Find(x => x.Name == prompt);
            if (account != default) await DMM.RunUmamusume(account);
        }
    }

    public void ConfigPrompt()
    {
        PluginSettingsManager.LoadSettings(this);
        SyncToStatic();
        DMMConfig.Prompt();
        SyncFromStatic();
        PluginSettingsManager.SaveSettings(this);
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

    /// <summary>从 DMMConfig 静态类同步回实例属性（用于保存）</summary>
    internal void SyncFromStatic()
    {
        LauncherInfomation = DMMConfig.LauncherInfomation;
        MachineInformation = DMMConfig.MachineInformation;
        Accounts = DMMConfig.Accounts;
        Enable = DMMConfig.Enable;
        LastUsedAccountName = DMMConfig.LastUsedAccountName;
    }

    public async Task UpdatePlugin(ProgressContext ctx)
    {
        var progress = ctx.AddTask($"[[{Name}]] Updating");

        using var client = new HttpClient();
        using var resp = await client.GetAsync($"https://api.github.com/repos/URA-Plugins/{Name}/releases/latest");
        var json = await resp.Content.ReadAsStringAsync();
        var jo = JObject.Parse(json);

        var isLatest = $"v{((IPlugin)this).Version}" == $"v{jo["tag_name"]}";
        if (isLatest)
        {
            progress.Increment(progress.MaxValue);
            progress.StopTask();
            return;
        }
        progress.Increment(25);

        var downloadUrl = jo["assets"]?[0]?["browser_download_url"]?.ToString().AllowMirror();
        using var msg = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        var contentLength = msg.Content.Headers.ContentLength ?? 0;

        // 下载到 MemoryStream，避免 stream 读完后无法被 ZipArchive 使用
        using var memoryStream = new MemoryStream();
        await using (var stream = await msg.Content.ReadAsStreamAsync())
        {
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                memoryStream.Write(buffer, 0, read);
                if (contentLength > 0)
                {
                    progress.Increment((double)read / contentLength * 50);
                }
            }
        }

        memoryStream.Position = 0;
        using var archive = new ZipArchive(memoryStream);
        archive.ExtractToDirectory(Path.Combine("Plugins", Name), true);
        progress.Increment(25);

        progress.StopTask();
    }
}
