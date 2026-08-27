using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using UmamusumeResponseAnalyzer.TerminalGui;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static partial class DMM
{
    [GeneratedRegex("""<input type="hidden" name="token" value="([^"]+)"/>""")]
    private static partial Regex TokenRegex();
    [GeneratedRegex("""<input type="hidden" name="path" value="([^"]+)"/>""")]
    private static partial Regex PathRegex();
    [GeneratedRegex("""<input type="hidden" id="js-app-url" data-url = "([^"]+)"/>""")]
    private static partial Regex OAuthTokenRegex();
    [GeneratedRegex("""<input type="hidden" id="ga-param-service-url" value="([^"]+)"/>""")]
    private static partial Regex ServiceUrlRegex();

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
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() });
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

        // 1. 获取登录 URL
        var loginUrl = await LoginUrl();

        // 2. 获取登录表单 token 与 path（path 已是 URL-encoded，原样回传）
        var loginPage = await client.GetStringAsync(loginUrl);
        var token = TokenRegex().Match(loginPage).Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception(I18N_Auth_TokenNotFound);
        }

        var path = PathRegex().Match(loginPage).Groups[1].Value;
        if (string.IsNullOrEmpty(path))
            path = Regex.Match(loginUrl, "[?&]path=([^&]+)").Groups[1].Value;
        if (string.IsNullOrEmpty(path))
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
        var finalUrl = HttpUtility.HtmlDecode(ServiceUrlRegex().Match(authContent).Groups[1].Value);
        if (string.IsNullOrEmpty(finalUrl))
        {
            // 尝试旧的正则作为备选
            finalUrl = HttpUtility.HtmlDecode(OAuthTokenRegex().Match(authContent).Groups[1].Value);
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
    public static async Task<bool> AgreeToTerms(DMMAccountInformation account)
    {
        try
        {
            using var client = GetHttpClient();
            client.DefaultRequestHeaders.Add("actauth", account.access_token);
            using var resp = await client.PostAsync($"{ApiBase}/agreement/confirm/client",
                new StringContent("""{"product_id":"umamusume","is_notification":false,"is_myapp":false}""", Encoding.UTF8, "application/json"));
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            return json["result_code"]?.ToObject<int>() == 100;
        }
        catch (Exception ex)
        {
            DMMDisplay.Log(
                string.Format(I18N_Auth_TermsAgreeFailed, ex.Message),
                UiSeverity.Error);
            return false;
        }
    }

    /// <summary>
    /// 向 DMM API 发送带 actauth 的 POST 请求。
    /// 若服务端返回 203（token 已被拒绝），自动强制刷新 token 并重试一次。
    /// </summary>
    internal static async Task<JObject> PostWithAuthAsync(DMMAccountInformation account, string url, string jsonContent)
    {
        using var client = GetHttpClient();
        client.DefaultRequestHeaders.Add("actauth", account.access_token);

        using var resp = await client.PostAsync(url, new StringContent(jsonContent, Encoding.UTF8, "application/json"));
        var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());

        if (jo["result_code"]?.ToObject<int>() != 203)
            return jo;

        // Token 已被服务端拒绝，强制刷新后重试一次
        DMMDisplay.Log(
            string.Format(I18N_Start_Checking_Log, I18N_DMMTokenExpired + I18N_Token_RetryAuth),
            UiSeverity.Warning);
        if (!await EnsureAccessToken(account, forceRefresh: true))
            return jo; // 刷新失败，返回原 203 响应由调用方处理

        using var retryClient = GetHttpClient();
        retryClient.DefaultRequestHeaders.Add("actauth", account.access_token);
        using var retryResp = await retryClient.PostAsync(url, new StringContent(jsonContent, Encoding.UTF8, "application/json"));
        return JObject.Parse(await retryResp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 确保账号有有效的 access token，如果没有则自动重新认证
    /// </summary>
    public static async Task<bool> EnsureAccessToken(DMMAccountInformation account, bool forceRefresh = false)
    {
        if (!forceRefresh && account.IsTokenValid())
            return true;

        if (string.IsNullOrEmpty(account.Account) || string.IsNullOrEmpty(account.Password))
        {
            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Token_ExpiredNoCredentials),
                UiSeverity.Warning);
            return false;
        }

        DMMDisplay.Log(string.Format(
            I18N_Start_Checking_Log,
            string.Format(I18N_Token_Refreshing, account.Name)));

        try
        {
            var oauthCode = await Authenticate(account.Account, account.Password);
            var (accessToken, expiresIn) = await IssueAccessToken(oauthCode);

            account.access_token = accessToken;
            account.access_token_expires_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn - 60;
            SavePluginConfig();

            DMMDisplay.Log(
                string.Format(
                    I18N_Start_Checking_Log,
                    string.Format(
                        I18N_Token_RefreshSuccess,
                        DateTimeOffset.FromUnixTimeSeconds(account.access_token_expires_at.Value).ToLocalTime())),
                UiSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            DMMDisplay.Log(
                string.Format(
                    I18N_Start_Checking_Log,
                    string.Format(I18N_Token_RefreshFailed, ex.Message)),
                UiSeverity.Error);
            return false;
        }
    }
}
