using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using UmamusumeResponseAnalyzer.TerminalGui;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static partial class DMM
{
    private record FileEntry(string LocalPath, string RemotePath, string Hash, bool CheckHash, bool ForceDelete);
    private const string ProductId = "umamusume";

    /// <summary>
    /// 获取游戏安装信息（调用 install/cl 接口）
    /// </summary>
    public static async Task<(string fileListUrl, string sign, string latestVersion)> GetInstallInfoAsync(DMMAccountInformation account)
    {
        var jsonContent = $$"""{"product_id":"{{ProductId}}","game_type":"GCL","game_os":"win","mac_address":"{{MachineInformation.mac_address}}","hdd_serial":"{{MachineInformation.hdd_serial}}","motherboard":"{{MachineInformation.motherboard}}","user_os":"{{MachineInformation.user_os}}"}""";

        var jo = await PostWithAuthAsync(account, $"{ApiBase}/r2/install/cl", jsonContent);

        if (jo["result_code"]?.ToObject<int>() != 100)
            throw new Exception(string.Format(I18N_Download_InstallInfoFailed, jo["error"]?.ToString() ?? jo["result_code"]?.ToString()));

        var data = jo["data"]!;
        return (
            data["file_list_url"]!.ToString(),
            data["sign"]!.ToString(),
            data["latest_version"]!.ToString()
        );
    }

    public static async Task<(string fileListUrl, string sign, string latestVersion)> GetFileListAsync(DMMAccountInformation account)
    {
        var jsonContent = $$"""{"product_id":"{{ProductId}}","game_type":"GCL","game_os":"win"}""";

        var jo = await PostWithAuthAsync(account, $"{ApiBase}/r2/filelist/cl", jsonContent);

        if (jo["result_code"]?.ToObject<int>() != 100)
            throw new Exception(string.Format(I18N_Download_InstallInfoFailed, jo["error"]?.ToString() ?? jo["result_code"]?.ToString()));

        var data = jo["data"]!;
        return (
            data["file_list_url"]!.ToString(),
            data["sign"]!.ToString(),
            data["latest_version"]!.ToString()
        );
    }

    private static async Task<int> GetFileTotalPagesAsync(DMMAccountInformation account, string fileListUrl)
    {
        using var client = GetFilelistHttpClient(account);
        using var resp = await client.GetAsync($"https://apidgp-gameplayer.games.dmm.com{fileListUrl}/totalpages");
        var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());
        return jo["data"]!["total_pages"]!.ToObject<int>();
    }

    private static async Task<(string domain, List<FileEntry> files)> GetFilePageAsync(DMMAccountInformation account, string fileListUrl, int page)
    {
        using var client = GetFilelistHttpClient(account);
        using var resp = await client.GetAsync($"https://apidgp-gameplayer.games.dmm.com{fileListUrl}?page={page}");
        var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());
        var data = jo["data"]!;
        var domain = data["domain"]!.ToString();
        var files = data["file_list"]!.Select(f => new FileEntry(
            f["local_path"]!.ToString(),
            f["path"]!.ToString(),
            f["hash"]!.ToString(),
            f["check_hash_flg"]!.ToObject<bool>(),
            f["force_delete_flg"]!.ToObject<bool>()
        )).ToList();
        return (domain, files);
    }

    /// <summary>
    /// 下载/更新游戏文件
    /// </summary>
    public static async Task DownloadGameAsync(DMMAccountInformation account, string installDir, string fileListUrl, string sign, string latestVersion)
    {
        if (!await EnsureAccessToken(account))
        {
            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Token_CannotGetValid),
                UiSeverity.Error);
            DMMDisplay.SetStatusText(I18N_Token_CannotGetValid);
            DMMDisplay.Notify(I18N_Token_CannotGetValid, UiSeverity.Error);
            return;
        }

        var filesToDownload = new Dictionary<string, List<FileEntry>>();

        try
        {
            // 步骤1-3：获取文件信息并检查需要更新的文件
            DMMDisplay.SetStatusText(I18N_Download_GettingInstallInfo);
            DMMDisplay.Log(string.Format(
                I18N_Start_Checking_Log,
                string.Format(I18N_Download_LatestVersion, latestVersion)));
            var totalPages = await GetFileTotalPagesAsync(account, fileListUrl);

            var allFiles = new Dictionary<string, List<FileEntry>>();
            for (var page = 1; page <= totalPages; page++)
            {
                var (domain, files) = await GetFilePageAsync(account, fileListUrl, page);
                allFiles.TryAdd(domain, []);
                allFiles[domain].AddRange(files);
            }

            DMMDisplay.SetStatusText(I18N_Download_CheckingFiles);
            foreach (var (domain, files) in allFiles)
            {
                foreach (var file in files)
                {
                    var localPath = Path.Combine(
                        installDir,
                        file.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                    if (file.ForceDelete && File.Exists(localPath))
                    {
                        File.Delete(localPath);
                        continue;
                    }

                    if (file.CheckHash && File.Exists(localPath))
                    {
                        await using var fs = File.OpenRead(localPath);
                        var hash = Convert.ToHexStringLower(await MD5.HashDataAsync(fs));
                        if (hash == file.Hash)
                            continue;
                    }

                    filesToDownload.TryAdd(domain, []);
                    filesToDownload[domain].Add(file);
                }
            }

            if (filesToDownload.Count == 0)
            {
                DMMDisplay.Log(
                    string.Format(I18N_Start_Checking_Log, I18N_Download_FilesUpToDate),
                    UiSeverity.Success);
                DMMDisplay.SetStatusText(I18N_Download_FilesUpToDate);
                return;
            }

            using var cdnClient = GetCdnHttpClient(sign);

            var allEntries = filesToDownload.SelectMany(kv => kv.Value.Select(file => (domain: kv.Key, file))).ToList();
            var completedCount = 0;
            DMMDisplay.SetStatusText(string.Format(I18N_Download_Progress, 0, allEntries.Count));
            await Parallel.ForEachAsync(
                allEntries,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (entry, cancellationToken) =>
                {
                    var (domain, file) = entry;
                    var localPath = Path.Combine(
                        installDir,
                        file.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                    try
                    {
                        var url = $"{domain}/{file.RemotePath}";
                        using var response = await cdnClient.GetAsync(
                            url,
                            HttpCompletionOption.ResponseHeadersRead,
                            cancellationToken);
                        response.EnsureSuccessStatusCode();

                        var tmpPath = localPath + ".tmp";
                        await using (var fileStream = File.Create(tmpPath))
                        await using (var httpStream =
                                     await response.Content.ReadAsStreamAsync(cancellationToken))
                        {
                            await httpStream.CopyToAsync(fileStream, cancellationToken);
                        }

                        File.Move(tmpPath, localPath, true);
                    }
                    finally
                    {
                        var completed = Interlocked.Increment(ref completedCount);
                        DMMDisplay.SetStatusText(
                            string.Format(I18N_Download_Progress, completed, allEntries.Count));
                    }
                });

            DMMDisplay.Log(
                string.Format(I18N_Start_Checking_Log, I18N_Download_Complete),
                UiSeverity.Success);
            DMMDisplay.SetStatusText(I18N_Download_Complete);
        }
        catch (Exception ex)
        {
            var failure = string.Format(I18N_Download_Failed, ex.Message);
            DMMDisplay.Log(
                string.Format(
                    I18N_Start_Checking_Log,
                    failure),
                UiSeverity.Error);
            DMMDisplay.SetStatusText(failure);
            DMMDisplay.Notify(failure, UiSeverity.Error);
        }
    }
}
