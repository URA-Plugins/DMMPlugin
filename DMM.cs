using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using UmamusumeResponseAnalyzer.Plugin;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

/// <summary>
/// 直接抓的包，然后重放
/// </summary>
internal static partial class DMM
{
    private const string ApiBase = "https://apidgp-gameplayer.games.dmm.com/v5";

    internal static DMMPlugin? PluginInstance { get; set; }

    [GeneratedRegex("""<input type="hidden" name="token" value="([^"]+)"/>""")]
    private static partial Regex TokenRegex();
    [GeneratedRegex("""<input type="hidden" id="js-app-url" data-url = "([^"]+)"/>""")]
    private static partial Regex OAuthTokenRegex();
    [GeneratedRegex("""<input type="hidden" id="ga-param-service-url" value="([^"]+)"/>""")]
    private static partial Regex ServiceUrlRegex();

    public static bool IgnoreExistProcess = false;

    /// <summary>
    /// 获取 DMM 登录 URL
    /// </summary>
    public static async Task<string> LoginUrl()
    {
        using var client = GetHttpClient();
        using var resp = await client.PostAsync($"{ApiBase}/auth/login/url",
            new StringContent("""{"prompt": "choose"}""", Encoding.UTF8, "application/json"));
        var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());
        return jo["data"]!["url"]!.ToString();
    }

    /// <summary>
    /// 使用账号密码认证，获取 OAuth code
    /// </summary>
    public static async Task<string> Authenticate(string account, string password)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

        // 1. 获取登录 URL
        var loginUrl = await LoginUrl();
        var path = HttpUtility.UrlEncode(loginUrl
            .Replace("https://accounts.dmm.com/service/oauth/select/=/path=", string.Empty)
            .Replace("oauth/select/", "oauth/")
            .Replace("accounts?prompt=choose", "accounts"));

        // 2. 获取登录表单 token
        var loginPage = await client.GetStringAsync(loginUrl);
        var token = TokenRegex().Match(loginPage).Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception(I18N_Auth_TokenNotFound);
        }

        // 3. 提交登录凭证
        using var oauthTokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.dmm.com/service/oauth/authenticate");
        oauthTokenRequest.Headers.Add("check_done_login", "true");
        oauthTokenRequest.Content = new StringContent(
            $"token={token}&login_id={HttpUtility.UrlEncode(account)}&password={HttpUtility.UrlEncode(password)}&use_auto_login=1&path={path}&recaptchaToken=",
            Encoding.UTF8, "application/x-www-form-urlencoded");

        using var authResponse = await client.SendAsync(oauthTokenRequest);
        var authContent = await authResponse.Content.ReadAsStringAsync();

        // 4. 获取最终跳转 URL（使用 ga-param-service-url）
        var finalUrl = ServiceUrlRegex().Match(authContent).Groups[1].Value;
        if (string.IsNullOrEmpty(finalUrl))
        {
            // 尝试旧的正则作为备选
            finalUrl = OAuthTokenRegex().Match(authContent).Groups[1].Value;
            if (!string.IsNullOrEmpty(finalUrl))
            {
                return finalUrl.Replace("dmmgameplayer://view/page?code=", string.Empty);
            }
            throw new Exception(I18N_Auth_OAuthUrlNotFound);
        }

        // 5. 访问 final URL 获取 OAuth token
        using var oauthResponse = await client.GetAsync(finalUrl);
        var redirectUrl = oauthResponse.RequestMessage?.RequestUri?.ToString()
            ?? oauthResponse.Headers.Location?.ToString();

        if (string.IsNullOrEmpty(redirectUrl))
        {
            throw new Exception(I18N_Auth_RedirectUrlNotFound);
        }

        // 6. 从 URL 中提取 code
        var oauth_code = HttpUtility.ParseQueryString(new Uri(redirectUrl).Query)["code"];
        if (string.IsNullOrEmpty(oauth_code))
            oauth_code = redirectUrl.Split("code=").LastOrDefault()?.Split('&').FirstOrDefault();

        if (string.IsNullOrEmpty(oauth_code))
        {
            throw new Exception(I18N_Auth_OAuthCodeNotFound);
        }

        return oauth_code;
    }

    /// <summary>
    /// 交换 OAuth code 获取 access token
    /// </summary>
    public static async Task<(string accessToken, long expiresInSeconds)> IssueAccessToken(string code)
    {
        using var client = GetHttpClient();
        using var resp = await client.PostAsync($"{ApiBase}/auth/accesstoken/issue",
            new StringContent($$"""{"code": "{{code}}"}""", Encoding.UTF8, "application/json"));
        var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());

        if (jo["result_code"]?.ToObject<int>() != 100)
        {
            throw new Exception(string.Format(I18N_Auth_AccessTokenFailed, jo["error"]?.ToString()));
        }

        var token = jo["data"]!["access_token"]!.ToString();
        var expiresIn = jo["data"]!["expires_in_seconds"]!.ToObject<long>();
        return (token, expiresIn);
    }

    /// <summary>
    /// 同意游戏条款（处理 result_code 308）
    /// </summary>
    public static async Task<bool> AgreeToTerms()
    {
        try
        {
            using var client = GetHttpClient();
            using var resp = await client.PostAsync($"{ApiBase}/agreement/confirm/client",
                new StringContent("""{"product_id":"umamusume","is_notification":false,"is_myapp":false}""", Encoding.UTF8, "application/json"));
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            return json["result_code"]?.ToObject<int>() == 100;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine(string.Format(I18N_Auth_TermsAgreeFailed, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// 获取游戏启动参数
    /// </summary>
    public static async Task<(string executeArgs, bool needsReauth, string error)> GetExecuteArgsAsync(DMMAccountInformation account)
    {
        using var client = GetHttpClient();
        client.DefaultRequestHeaders.Add("actauth", account.access_token);

        var jsonContent = $$"""
            {"product_id":"umamusume","game_type":"GCL","game_os":"win","launch_type":"LIB","mac_address":"{{MachineInformation.mac_address}}","hdd_serial":"{{MachineInformation.hdd_serial}}","motherboard":"{{MachineInformation.motherboard}}","user_os":"{{MachineInformation.user_os}}"}
            """.Trim();

        var launchUrl = $"{ApiBase}/r2/launch/cl";
        using var response = await client.PostAsync(launchUrl,
            new StringContent(jsonContent, Encoding.UTF8, "application/json"));
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());

        var resultCode = json["result_code"]?.ToObject<int>() ?? 0;

        // 203: Token 过期
        if (resultCode == 203)
        {
            return (null!, true, I18N_DMMTokenExpired);
        }

        // 308: 需要同意条款
        if (resultCode == 308)
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Terms_Required));
            if (await AgreeToTerms())
            {
                using var retryResponse = await client.PostAsync(launchUrl,
                    new StringContent(jsonContent, Encoding.UTF8, "application/json"));
                json = JObject.Parse(await retryResponse.Content.ReadAsStringAsync());
                resultCode = json["result_code"]?.ToObject<int>() ?? 0;
            }
            else
            {
                return (null!, false, I18N_Terms_AgreeFailed);
            }
        }

        if (resultCode != 100)
            return (null!, false, string.Format(I18N_Launch_Failed, json["error"]?.ToString() ?? $"Error code {resultCode}"));

        var executeArgs = json["data"]!["execute_args"]?.ToString();
        return (executeArgs ?? "", false, null!);
    }

    /// <summary>
    /// 确保账号有有效的 access token，如果没有则自动重新认证
    /// </summary>
    public static async Task<bool> EnsureAccessToken(DMMAccountInformation account)
    {
        if (account.IsTokenValid())
            return true;

        if (string.IsNullOrEmpty(account.Account) || string.IsNullOrEmpty(account.Password))
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Token_ExpiredNoCredentials));
            return false;
        }

        AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Token_Refreshing, account.Account)));

        try
        {
            var oauthCode = await Authenticate(account.Account, account.Password);
            var (accessToken, expiresIn) = await IssueAccessToken(oauthCode);

            account.access_token = accessToken;
            account.access_token_expires_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn - 60;
            SavePluginConfig();

            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Token_RefreshSuccess, DateTimeOffset.FromUnixTimeSeconds(account.access_token_expires_at.Value).ToLocalTime())));
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Token_RefreshFailed, ex.Message)));
            return false;
        }
    }

    /// <summary>
    /// 启动赛马娘游戏
    /// </summary>
    public static async Task RunUmamusume(DMMAccountInformation account)
    {
        await AnsiConsole.Status().StartAsync(I18N_Start_Checking, async ctx =>
        {
            var processes = Process.GetProcessesByName("umamusume");
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Start_Checking_Found, processes.Length)));
            if (processes.Length == 0 || IgnoreExistProcess)
            {
                ctx.Spinner(Spinner.Known.BouncingBar);
                ctx.Status(I18N_Start_GetToken);

                SaveLastAccountSaveDataIfSwitched(account);

                if (!await EnsureAccessToken(account))
                {
                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Token_CannotGetValid));
                    return;
                }

                var (executeArgs, needsReauth, error) = await GetExecuteArgsAsync(account);

                if (needsReauth)
                {
                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_DMMTokenExpired + I18N_Token_RetryAuth));

                    // 强制清除 token 以触发重新认证
                    account.access_token = string.Empty;
                    account.access_token_expires_at = null;

                    if (!await EnsureAccessToken(account))
                    {
                        AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Token_ReauthFailed));
                        return;
                    }

                    (executeArgs, _, error) = await GetExecuteArgsAsync(account);
                }

                if (!string.IsNullOrEmpty(error))
                {
                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, error));
                    return;
                }

                if (string.IsNullOrEmpty(executeArgs))
                {
                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_TokenFailed));
                    return;
                }

                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_TokenGot));

                if (!LoadAccountSaveDataIfSwitched(account))
                {
                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Launch_SaveDataFailed));
                    return;
                }

                ctx.Status(I18N_Start_Launching);
                Launch(executeArgs);

                LastUsedAccountName = account.Account;
                SavePluginConfig();
            }
            else
            {
                ctx.Status(I18N_Start_Checking_AlreadyRunning);
                foreach (var process in processes) process.Dispose();
            }
        });
    }

    /// <summary>
    /// 如果切换了账号，保存上一个账号的存档到其专属路径
    /// </summary>
    private static void SaveLastAccountSaveDataIfSwitched(DMMAccountInformation currentAccount)
    {
        if (string.IsNullOrEmpty(LastUsedAccountName) || LastUsedAccountName == currentAccount.Account)
            return;

        var lastAccount = Accounts.First(x => x.Account == LastUsedAccountName);
        if (!File.Exists(DMMAccountInformation.DefaultSaveDataPath))
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
            return true;
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
}
