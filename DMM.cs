using System.Net;
using System.Text;
using UmamusumeResponseAnalyzer.Plugin;
using static DMMPlugin.DMMConfig;

namespace DMMPlugin;

/// <summary>
/// 直接抓的包，然后重放
/// </summary>
internal static partial class DMM
{
    private const string ApiBase = "https://apidgp-gameplayer.games.dmm.com/v5";

    internal static DMMPlugin PluginInstance { get; set; } = null!;

    public static bool IgnoreExistProcess = false;

    private static void SavePluginConfig()
    {
        if (PluginInstance == null) return;
        PluginInstance.SyncFromStatic();
        PluginSettingsManager.SaveSettings(PluginInstance);
    }

    private static HttpClient GetHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer()
        });
        client.DefaultRequestHeaders.Add("Accept-Encoding", LauncherInfomation.AcceptEncoding);
        client.DefaultRequestHeaders.Add("Accept-Language", LauncherInfomation.AcceptLanguage);
        client.DefaultRequestHeaders.Add("User-Agent", LauncherInfomation.UserAgent);
        client.DefaultRequestHeaders.Add("Client-App", LauncherInfomation.ClientApp);
        client.DefaultRequestHeaders.Add("Client-version", LauncherInfomation.ClientVersion);

        return client;
    }

    private static HttpClient GetFilelistHttpClient(DMMAccountInformation account)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            CookieContainer = new CookieContainer()
        };
        handler.CookieContainer.Add(new Uri("https://apidgp-gameplayer.games.dmm.com"), new Cookie("age_check_done", "0"));
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", LauncherInfomation.UserAgent);
        client.DefaultRequestHeaders.Add("Client-App", LauncherInfomation.ClientApp);
        client.DefaultRequestHeaders.Add("Client-version", LauncherInfomation.ClientVersion);
        client.DefaultRequestHeaders.Add("Accept", "application/json; charset=utf-8");
        client.DefaultRequestHeaders.Add("actauth", account.access_token);
        return client;
    }

    private static HttpClient GetCdnHttpClient(string sign)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", LauncherInfomation.UserAgent);
        // 将 sign 字段（格式: Key=Value;Key=Value;...）解析为 Cookie 请求头
        var cookieValue = string.Join("; ", sign.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)));
        client.DefaultRequestHeaders.Add("Cookie", cookieValue);
        return client;
    }
}
