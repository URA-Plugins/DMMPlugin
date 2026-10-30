using Terminal.Gui.Text;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

static class DmmRuntimeSmoke
{
    public static void Run(WorkspaceSmokeSession ui)
    {
        using var directory = new TempCurrentDirectory("DMM-start-failure");
        PrepareDmmFixture(directory.Path);
        using var context = new RuntimePluginContext(ui.Application);
        ui.Bootstrap.SwitchTo();
        var startup = TerminalUi.RequireHost().Bootstrap;
        startup.SetPhase(BootstrapPhaseId.Host, BootstrapPhaseState.Succeeded, "smoke");
        startup.ShowInformation();
        ui.Flush();
        ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!)
            .OfType<Button>()
            .Single(button => button.Text.Contains(UmamusumeResponseAnalyzer.Localization.LaunchMenu.I18N_StartupDetails, StringComparison.Ordinal))
            .SetFocus());
        ui.SendKey(Key.Space);
        IPlugin plugin = new DMMPlugin.DMMPlugin();
        try
        {
            plugin.Initialize(context);
            ui.Flush();
            var initialized = ui.CaptureScreen();
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap)
                || !initialized.Contains("等待 URA 启动。", StringComparison.Ordinal))
                throw new InvalidOperationException($"DMM initialization did not preserve Bootstrap and publish its waiting log. Workspace={Workspace.Current?.Title}\n{initialized}");
            VerifyDmmRuntime(plugin, ui, directory.Path);
        }
        finally
        {
            plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        Console.WriteLine("PASS DMM StartAsync empty token: one error log, one info log, one card; settings unchanged");
    }

    static void PrepareDmmFixture(string currentDirectory)
    {
        var settingsPath = Path.Combine(currentDirectory, "PluginData", "DMM插件", "settings.yaml");
        var executable = Path.Combine(currentDirectory, "umamusume.exe");
        File.WriteAllText(executable, "fixture; authentication must fail before execution");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            settingsPath,
            $$"""
            enable: true
            machine_information:
              umamusume_file_path: {{System.Text.Json.JsonSerializer.Serialize(executable)}}
            accounts:
            - name: runtime-smoke
              account: ''
              password: ''
              access-token: ''
            """);
    }

    static void VerifyDmmRuntime(IPlugin plugin, WorkspaceSmokeSession ui, string currentDirectory)
    {
        var tokenSemantic = new System.Resources.ResourceManager("DMMPlugin.i18n.DMM", typeof(DMMPlugin.DMMPlugin).Assembly)
            .GetString("I18N_Token_CannotGetValid", System.Globalization.CultureInfo.CurrentUICulture)!;
        var settingsPath = Path.Combine(currentDirectory, "PluginData", "DMM插件", "settings.yaml");
        var settingsBefore = File.ReadAllText(settingsPath);
        var linesBefore = ui.CaptureScreen().ReplaceLineEndings("\n").Split('\n');
        var errorLogsBefore = CountDmmAccessTokenLogs(linesBefore, UiSeverity.Error, tokenSemantic);
        var infoLogsBefore = CountDmmAccessTokenLogs(linesBefore, UiSeverity.Info, tokenSemantic);
        var cardsBefore = CountDmmErrorCards(linesBefore);
        var ignoreExistingProcess = typeof(DMMPlugin.DMMPlugin).Assembly
            .GetType("DMMPlugin.DMM", throwOnError: true)!
            .GetField("IgnoreExistProcess")
            ?? throw new InvalidOperationException("DMMPlugin IgnoreExistProcess test hook is unavailable.");
        var previousIgnoreExistingProcess = (bool)ignoreExistingProcess.GetValue(null)!;
        try
        {
            ignoreExistingProcess.SetValue(null, true);
            plugin.StartAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            ignoreExistingProcess.SetValue(null, previousIgnoreExistingProcess);
        }

        string[] lines = [];
        for (var attempt = 0; attempt < 50; attempt++)
        {
            ui.Flush();
            lines = ui.CaptureScreen().ReplaceLineEndings("\n").Split('\n');
            if (CountDmmAccessTokenLogs(lines, UiSeverity.Error, tokenSemantic) == errorLogsBefore + 1
                && CountDmmAccessTokenLogs(lines, UiSeverity.Info, tokenSemantic) == infoLogsBefore + 1
                && CountDmmErrorCards(lines) == cardsBefore + 1)
            {
                break;
            }
            Thread.Sleep(20);
        }

        var errorLogsAfter = CountDmmAccessTokenLogs(lines, UiSeverity.Error, tokenSemantic);
        var infoLogsAfter = CountDmmAccessTokenLogs(lines, UiSeverity.Info, tokenSemantic);
        var cardsAfter = CountDmmErrorCards(lines);
        if (errorLogsAfter != errorLogsBefore + 1
            || infoLogsAfter != infoLogsBefore + 1
            || cardsAfter != cardsBefore + 1)
        {
            var visibleDiagnostics = string.Join(
                " | ",
                lines.Where(line => line.Contains("DMMPlugin", StringComparison.Ordinal)
                    || line.Contains(tokenSemantic, StringComparison.OrdinalIgnoreCase)
                    || line.Contains(UiText.Severity_Error, StringComparison.Ordinal)));
            throw new InvalidOperationException(
                $"DMMPlugin: StartAsync auth failure did not add exactly one access-token Error log, "
                + $"one access-token Info log, and one DMM Error card "
                + $"(Error {errorLogsBefore}->{errorLogsAfter}, Info {infoLogsBefore}->{infoLogsAfter}, "
                + $"cards {cardsBefore}->{cardsAfter}). Visible: {visibleDiagnostics}");
        }
        if (!string.Equals(settingsBefore, File.ReadAllText(settingsPath), StringComparison.Ordinal))
            throw new InvalidOperationException($"DMMPlugin: StartAsync failure changed settings.yaml.");
    }

    // A notification card's top border reads "┌─ ● <severity> <source> ─── <seconds> ─┐".
    static string ErrorCardHeader => $"┌─ ● {UiText.Severity_Error}";

    static int CountDmmAccessTokenLogs(string[] lines, UiSeverity severity, string tokenSemantic)
    {
        var overlayColumn = lines
            .Select(line => (Line: line, Index: line.IndexOf(ErrorCardHeader, StringComparison.Ordinal)))
            .Where(item => item.Index >= 0)
            .Select(item => item.Line[..item.Index].GetColumns())
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        return lines.Count(line =>
        {
            var logArea = TextFormatter.ClipOrPad(line, Math.Min(line.GetColumns(), overlayColumn));
            return logArea.Contains($"{BootstrapWorkspace.SeverityLabel(severity)} [DMMPlugin]", StringComparison.Ordinal)
                && logArea.Contains(tokenSemantic, StringComparison.OrdinalIgnoreCase);
        });
    }

    // The host moves a "[DMMPlugin] " message prefix into the card's top border as its source.
    static int CountDmmErrorCards(string[] lines)
        => lines.Count(line => line.Contains($"{ErrorCardHeader} DMMPlugin ", StringComparison.Ordinal));

}
