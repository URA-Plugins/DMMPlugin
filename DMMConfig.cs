using System.Security.Cryptography;
using System.Text;
using UmamusumeResponseAnalyzer.TerminalGui;
using YamlDotNet.Serialization;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

public static class DMMConfig
{
    public static DMMLauncherInfomation LauncherInfomation { get; set; } = new();
    public static DMMMachineInformation MachineInformation { get; set; } = new();
    public static List<DMMAccountInformation> Accounts { get; set; } = [];
    public static bool Enable { get; set; }
    public static string LastUsedAccountName { get; set; } = string.Empty;

    public class DMMLauncherInfomation
    {
        public string AcceptEncoding { get; set; } = "gzip, deflate, br, zstd";
        public string AcceptLanguage { get; set; } = "zh-CN";
        public string UserAgent { get; set; } = "DMMGamePlayer5-Win/5.4.2 Electron/34.3.0";
        public string ClientApp { get; set; } = "DMMGamePlayer5";
        public string ClientVersion { get; set; } = "5.4.2";

    }

    public class DMMMachineInformation
    {
        public string mac_address { get; set; } = string.Empty;
        public string hdd_serial { get; set; } = string.Empty;
        public string motherboard { get; set; } = string.Empty;
        public string user_os { get; set; } = string.Empty;
        public string umamusume_file_path { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Umamusume", "umamusume.exe");

    }

    public class DMMAccountInformation
    {
        internal static string SaveDataDirectory =>
            Path.Combine(Path.GetDirectoryName(MachineInformation.umamusume_file_path) ?? throw new FileNotFoundException(), "umamusume_Data", "Persistent", "d");
        [YamlIgnore]
        internal static string DefaultSaveDataPath => Path.Combine(SaveDataDirectory, "SaveData.db");

        [YamlMember(Alias = "name", ApplyNamingConventions = false)]
        public string Name { get; set; } = string.Empty;
        [YamlMember(Alias = "account", ApplyNamingConventions = false)]
        public string Account { get; set; } = string.Empty;
        [YamlMember(Alias = "password", ApplyNamingConventions = false)]
        public string PasswordEncrypted { get; set; } = string.Empty;

        [YamlIgnore]
        public string Password
        {
            get => SecurePassword.Decrypt(PasswordEncrypted);
            set => PasswordEncrypted = SecurePassword.Encrypt(value);
        }

        [YamlMember(Alias = "access-token", ApplyNamingConventions = false)]
        public string access_token { get; set; } = string.Empty;
        [YamlMember(Alias = "access-token-expires-at", ApplyNamingConventions = false)]
        public long? access_token_expires_at { get; set; }

        internal void HandleFirstTimeSaveDataAssociation(bool isCurrentAccount)
        {
            if (!File.Exists(DefaultSaveDataPath))
                return;

            if (isCurrentAccount)
            {
                if (!File.Exists(SaveDataPath))
                {
                    try
                    {
                        File.Copy(DefaultSaveDataPath, SaveDataPath, true);
                        DMMDisplay.Log(
                            string.Format(I18N_SaveData_Associated, Name),
                            UiSeverity.Success);
                    }
                    catch (Exception ex)
                    {
                        DMMDisplay.Log(
                            string.Format(I18N_SaveData_AssociateFailed, ex.Message),
                            UiSeverity.Error);
                    }
                }
            }
            else
            {
                try
                {
                    var oldPath = DefaultSaveDataPath + ".old";
                    File.Move(DefaultSaveDataPath, oldPath, true);
                    DMMDisplay.Log(
                        string.Format(I18N_SaveData_RenamedToOld, oldPath),
                        UiSeverity.Success);
                }
                catch (Exception ex)
                {
                    DMMDisplay.Log(
                        string.Format(I18N_SaveData_RenameFailed, ex.Message),
                        UiSeverity.Error);
                }
            }
        }

        /// <summary>
        /// 检查 token 是否有效（未过期）
        /// </summary>
        public bool IsTokenValid()
        {
            if (string.IsNullOrEmpty(access_token) || !access_token_expires_at.HasValue)
                return false;
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < access_token_expires_at.Value;
        }

        [YamlIgnore]
        public string SaveDataPath => Path.Combine(SaveDataDirectory, $"SaveData.db.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Account)))[..8]}");
    }
}
