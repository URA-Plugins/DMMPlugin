using Spectre.Console;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

    public static async Task Prompt()
    {
        if (!Enable)
        {
            var enablePrompt = new ConfirmationPrompt(Tabs_DMM_Caution)
            {
                DefaultValue = false
            };
            if (AnsiConsole.Prompt(enablePrompt))
            {
                Enable = true;
                UmamusumeResponseAnalyzer.Config.Save();
                await Prompt();
            }
            else
            {
                AnsiConsole.Clear();
            }
            return;
        }
        var selected = string.Empty;
        do
        {
            var selectionPrompt = new SelectionPrompt<string>()
                .Title(Tabs_DMM_Title)
                .WrapAround(true)
                .AddChoices([Tabs_DMM_ViewLauncherInformation, Tabs_DMM_EditMachineInformation, Tabs_DMM_EditAccounts, Tabs_DMM_UpdateGame, Tabs_DMM_Enable])
                .AddChoices(Return);
            selected = AnsiConsole.Prompt(selectionPrompt).Split(':')[0];
            if (selected == Tabs_DMM_Enable)
            {
                Enable = false;
                selected = Return;
            }
            else if (selected == Tabs_DMM_ViewLauncherInformation)
            {
                LauncherInfomation.Prompt();
            }
            else if (selected == Tabs_DMM_EditMachineInformation)
            {
                MachineInformation.Prompt();
            }
            else if (selected == Tabs_DMM_UpdateGame)
            {
                if (Accounts.Count == 0)
                {
                    AnsiConsole.MarkupLine(I18N_Download_NoAccount);
                }
                else
                {
                    DMMAccountInformation account;
                    if (Accounts.Count == 1)
                    {
                        account = Accounts[0];
                    }
                    else
                    {
                        var accountName = AnsiConsole.Prompt(new SelectionPrompt<string>()
                            .Title(I18N_MultipleAccountsFound)
                            .WrapAround(true)
                            .AddChoices(Accounts.Select(x => x.Name)));
                        account = Accounts.First(x => x.Name == accountName);
                    }

                    var installDir = Path.GetDirectoryName(MachineInformation.umamusume_file_path)!;
                    var (fileListUrl, sign, latestVersion) = await DMM.GetFileListAsync(account);
                    await DMM.DownloadGameAsync(account, installDir, fileListUrl, sign, latestVersion);
                }
            }
            else if (selected == Tabs_DMM_EditAccounts)
            {
                var eaSelected = string.Empty;
                do
                {
                    var eaPrompt = new SelectionPrompt<string>()
                        .Title(Tabs_DMM_EditAccounts)
                        .WrapAround(true)
                        .AddChoices(Accounts.Select(x => x.Name))
                        .AddChoices([Tabs_DMM_EditAccounts_Add, Return]);
                    eaSelected = AnsiConsole.Prompt(eaPrompt);

                    if (eaSelected == Tabs_DMM_EditAccounts_Add)
                    {
                        var account = new DMMAccountInformation();
                        Accounts.Add(account);
                        account.EditPrompt();
                        account.HandleFirstTimeSaveDataAssociation();
                    }
                    else if (eaSelected == Return)
                    {
                    }
                    else
                    {
                        var account = Accounts.First(x => x.Name == eaSelected);
                        account.EditPrompt();
                    }
                } while (eaSelected != Return);
            }
            UmamusumeResponseAnalyzer.Config.Save();
        } while (selected != Return);
    }

    public class DMMLauncherInfomation
    {
        public string AcceptEncoding { get; set; } = "gzip, deflate, br, zstd";
        public string AcceptLanguage { get; set; } = "zh-CN";
        public string UserAgent { get; set; } = "DMMGamePlayer5-Win/5.4.2 Electron/34.3.0";
        public string ClientApp { get; set; } = "DMMGamePlayer5";
        public string ClientVersion { get; set; } = "5.4.2";

        public void Prompt()
        {
            var liSelectionPrompt = new SelectionPrompt<string>()
                .Title(Tabs_DMM_ViewLauncherInformation_Caution)
                .WrapAround(true)
                .AddChoices(GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => $"{x.Name}: {x.GetValue(this)}"));
            AnsiConsole.Prompt(liSelectionPrompt);
            UmamusumeResponseAnalyzer.Config.Save();
        }
    }

    public class DMMMachineInformation
    {
        public string mac_address { get; set; } = string.Empty;
        public string hdd_serial { get; set; } = string.Empty;
        public string motherboard { get; set; } = string.Empty;
        public string user_os { get; set; } = string.Empty;
        public string umamusume_file_path { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Umamusume", "umamusume.exe");

        private static readonly Dictionary<string, string> PropertyPrompts = new()
        {
            ["mac_address"] = Tabs_DMM_EditMachineInformation_InputMA,
            ["hdd_serial"] = Tabs_DMM_EditMachineInformation_InputHS,
            ["motherboard"] = Tabs_DMM_EditMachineInformation_InputMB,
            ["user_os"] = Tabs_DMM_EditMachineInformation_InputOS,
            ["umamusume_file_path"] = Tabs_DMM_EditMachineInformation_InputUmamusumePath,
        };

        public void Prompt()
        {
            var miProperties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var miSelected = string.Empty;
            do
            {
                var miSelectionPrompt = new SelectionPrompt<string>()
                    .Title(Tabs_DMM_EditMachineInformation)
                    .WrapAround(true)
                    .AddChoices(miProperties.Select(x => $"{x.Name}: {x.GetValue(this)}"))
                    .AddChoices(Return);
                miSelected = AnsiConsole.Prompt(miSelectionPrompt);

                var propName = miSelected.Split(':')[0];
                if (PropertyPrompts.TryGetValue(propName, out var promptText))
                {
                    var prop = miProperties.First(x => x.Name == propName);
                    prop.SetValue(this, AnsiConsole.Prompt(new TextPrompt<string>(promptText)));
                }

                AnsiConsole.Clear();
                UmamusumeResponseAnalyzer.Config.Save();
            } while (miSelected != Return);
        }
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

        public void EditPrompt()
        {
            var aiSelected = string.Empty;
            do
            {
                var tokenStatusText = IsTokenValid() && access_token_expires_at.HasValue
                    ? string.Format(I18N_AccountEdit_TokenValid, DateTimeOffset.FromUnixTimeSeconds(access_token_expires_at.Value).ToLocalTime())
                    : I18N_AccountEdit_TokenInvalid;

                var aiPrompt = new SelectionPrompt<string>()
                    .WrapAround(true)
                    .AddChoices([
                        $"{I18N_AccountEdit_Name}: {Name}",
                        $"{I18N_AccountEdit_Account}: {(string.IsNullOrEmpty(Account) ? I18N_AccountEdit_NameNotSet : Account)}",
                        $"{I18N_AccountEdit_Password}: {(string.IsNullOrEmpty(Password) ? I18N_AccountEdit_NameNotSet : I18N_AccountEdit_PasswordHidden)}",
                        $"{I18N_AccountEdit_TokenStatus}: {tokenStatusText}"
                    ])
                    .AddChoices(I18N_AccountEdit_Delete, Return);
                aiSelected = AnsiConsole.Prompt(aiPrompt).Split(':')[0];
                if (aiSelected == I18N_AccountEdit_Name)
                {
                    while (true)
                    {
                        var name = AnsiConsole.Prompt(new TextPrompt<string>(I18N_AccountEdit_EnterName));
                        if (Accounts.Any(x => x.Name == name && x != this))
                        {
                            AnsiConsole.WriteLine(I18N_AccountEdit_NameDuplicate);
                        }
                        else
                        {
                            Name = name;
                            break;
                        }
                    }
                }
                else if (aiSelected == I18N_AccountEdit_Account)
                {
                    Account = AnsiConsole.Prompt(new TextPrompt<string>(I18N_AccountEdit_EnterAccount));
                }
                else if (aiSelected == I18N_AccountEdit_Password)
                {
                    Password = AnsiConsole.Prompt(new TextPrompt<string>(I18N_AccountEdit_EnterPassword).Secret());
                }
                else if (aiSelected == I18N_AccountEdit_Delete)
                {
                    aiSelected = Return;
                    Accounts.Remove(this);
                }
                AnsiConsole.Clear();
                UmamusumeResponseAnalyzer.Config.Save();
            } while (aiSelected != Return);
            // 如果备注为空，自动设置为账号
            if (string.IsNullOrEmpty(Name))
            {
                Name = Account;
            }
        }

        /// <summary>
        /// 处理首次添加账号时的存档逻辑：询问用户是否将当前存档关联到此账号
        /// </summary>
        public void HandleFirstTimeSaveDataAssociation()
        {
            if (!File.Exists(DefaultSaveDataPath))
                return;

            var isThisAccount = AnsiConsole.Confirm(I18N_SaveData_AskIsLastLoginAccount, false);

            if (isThisAccount)
            {
                if (!File.Exists(SaveDataPath))
                {
                    try
                    {
                        File.Copy(DefaultSaveDataPath, SaveDataPath, true);
                        AnsiConsole.MarkupLine(string.Format(I18N_SaveData_Associated, Name));
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(string.Format(I18N_SaveData_AssociateFailed, ex.Message));
                    }
                }
            }
            else
            {
                try
                {
                    var oldPath = DefaultSaveDataPath + ".old";
                    File.Move(DefaultSaveDataPath, oldPath, true);
                    AnsiConsole.MarkupLine(string.Format(I18N_SaveData_RenamedToOld, oldPath));
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(string.Format(I18N_SaveData_RenameFailed, ex.Message));
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
