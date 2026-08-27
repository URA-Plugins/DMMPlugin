using UmamusumeResponseAnalyzer.TerminalGui;

namespace DMMPlugin;

internal static class DMMDisplay
{
    internal static void SetStatusText(string text)
        => TerminalUi.Log("DMMPlugin", text);

    internal static void Log(string text, UiSeverity severity = UiSeverity.Info)
        => TerminalUi.Log("DMMPlugin", text, severity);

    internal static void Notify(string text, UiSeverity severity = UiSeverity.Info, TimeSpan? ttl = null)
        => TerminalUi.Notify("DMMPlugin", text, severity, ttl);
}
