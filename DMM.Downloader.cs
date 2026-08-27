using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Security.Cryptography;
using System.Text;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static partial class DMM
{
    private record FileEntry(string LocalPath, string RemotePath, long Size, string Hash, bool CheckHash, bool ForceDelete);
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
            f["size"]!.ToObject<long>(),
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
            AnsiConsole.MarkupLine(I18N_Token_CannotGetValid);
            return;
        }

        var filesToDownload = new Dictionary<string, List<FileEntry>>();
        long downloadTotalSize = 0;

        try
        {
            // 步骤1-3：获取文件信息并检查需要更新的文件
            await AnsiConsole.Status().StartAsync(I18N_Download_GettingInstallInfo, async ctx =>
            {
                ctx.Spinner(Spinner.Known.BouncingBar);

                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Download_LatestVersion, latestVersion)));
                // 步骤2：获取文件列表总页数
                var totalPages = await GetFileTotalPagesAsync(account, fileListUrl);

                // 步骤3：获取所有页的文件列表
                var allFiles = new Dictionary<string, List<FileEntry>>();
                for (int page = 1; page <= totalPages; page++)
                {
                    var (d, files) = await GetFilePageAsync(account, fileListUrl, page);
                    allFiles.TryAdd(d, []);
                    allFiles[d].AddRange(files);
                }

                // 检查哪些文件需要更新
                ctx.Status(I18N_Download_CheckingFiles);
                foreach (var (domain, files) in allFiles)
                {
                    foreach (var file in files)
                    {
                        var localPath = Path.Combine(installDir, file.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                        if (file.ForceDelete && File.Exists(localPath))
                        {
                            File.Delete(localPath);
                            continue;
                        }

                        if (file.CheckHash && File.Exists(localPath))
                        {
                            await using var fs = File.OpenRead(localPath);
                            var hash = Convert.ToHexStringLower(await MD5.HashDataAsync(fs));
                            if (hash == file.Hash) continue;
                        }

                        filesToDownload.TryAdd(domain, []);
                        filesToDownload[domain].Add(file);
                    }
                }

                downloadTotalSize = filesToDownload.Values.SelectMany(x => x).Sum(x => x.Size);
            });

            if (filesToDownload.Count == 0)
            {
                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Download_FilesUpToDate));
                return;
            }

            // 步骤4：使用进度条并行下载文件
            using var cdnClient = GetCdnHttpClient(sign);

            var allEntries = filesToDownload.SelectMany(kv => kv.Value.Select(file => (domain: kv.Key, file))).ToList();
            int completedCount = 0;

            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new DownloadedColumn(), new TransferSpeedColumn(), new RemainingTimeColumn())
                .HideCompleted(true)
                .StartAsync(async ctx =>
                {
                    var totalFiles = allEntries.Count;
                    var overallTask = ctx.AddTask(string.Format(I18N_Download_Progress, 0, totalFiles), maxValue: downloadTotalSize);

                    await Parallel.ForEachAsync(allEntries, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (entry, ct) =>
                    {
                        var (domain, file) = entry;
                        var localPath = Path.Combine(installDir, file.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                        var fileName = Path.GetFileName(file.LocalPath);
                        var fileTask = ctx.AddTask(fileName, maxValue: Math.Max(1, file.Size));

                        try
                        {
                            var url = $"{domain}/{file.RemotePath}";
                            using var response = await cdnClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                            response.EnsureSuccessStatusCode();

                            var tmpPath = localPath + ".tmp";
                            await using (var fileStream = File.Create(tmpPath))
                            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
                            {
                                var buffer = new byte[65536];
                                int read;
                                while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                                    fileTask.Increment(read);
                                    overallTask.Increment(read);
                                }
                            }

                            File.Move(tmpPath, localPath, true);
                        }
                        finally
                        {
                            fileTask.StopTask();
                            var completed = Interlocked.Increment(ref completedCount);
                            overallTask.Description = string.Format(I18N_Download_Progress, completed, totalFiles);
                        }
                    });
                });

            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Download_Complete));
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Download_Failed, ex.Message)));
        }
    }
}
