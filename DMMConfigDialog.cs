using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using static DMMPlugin.DMMConfig;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal static class DMMConfigDialog
{
    internal sealed record SaveDataAssociation(
        DMMAccountInformation Account,
        bool IsCurrentAccount);

    internal sealed record EditResult(
        DMMPluginSettings Settings,
        DMMAccountInformation? UpdateAccount,
        IReadOnlyList<SaveDataAssociation> SaveDataAssociations);

    internal static async Task<EditResult> EditAsync(
        IApplication application,
        DMMPluginSettings draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (application.TopRunnable is null &&
            Environment.CurrentManagedThreadId != application.MainThreadId)
        {
            throw new InvalidOperationException(
                "DMMPlugin 无法从非 UI thread 启动配置：Terminal.Gui 当前没有正在运行的 session。");
        }

        if (Environment.CurrentManagedThreadId == application.MainThreadId)
            return Run(application, draft, cancellationToken);

        var completion = new TaskCompletionSource<EditResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.Invoke(() =>
        {
            try
            {
                completion.SetResult(Run(application, draft, cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return await completion.Task;
    }

    internal static async Task<DMMAccountInformation?> SelectLaunchAccountAsync(
        IApplication application,
        IReadOnlyList<DMMAccountInformation> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        cancellationToken.ThrowIfCancellationRequested();
        if (application.TopRunnable is null &&
            Environment.CurrentManagedThreadId != application.MainThreadId)
            throw new InvalidOperationException(
                "DMMPlugin 无法从非 UI thread 选择启动账号：Terminal.Gui 当前没有正在运行的 session。");

        if (Environment.CurrentManagedThreadId == application.MainThreadId)
            return SelectAccount(application, accounts, cancellationToken);

        var completion = new TaskCompletionSource<DMMAccountInformation?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.Invoke(() =>
        {
            try
            {
                completion.SetResult(SelectAccount(application, accounts, cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return await completion.Task;
    }

    static EditResult Run(
        IApplication application,
        DMMPluginSettings draft,
        CancellationToken cancellationToken)
    {
        var associations = new Dictionary<DMMAccountInformation, bool>(
            ReferenceEqualityComparer.Instance);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (RunMainMenu(application, draft, cancellationToken))
            {
                case MainAction.Launcher:
                    ShowLauncherInformation(application, draft.LauncherInfomation, cancellationToken);
                    break;
                case MainAction.Machine:
                    EditMachineInformation(application, draft.MachineInformation, cancellationToken);
                    break;
                case MainAction.Accounts:
                    EditAccounts(application, draft, associations, cancellationToken);
                    break;
                case MainAction.Save:
                    return new(
                        draft,
                        null,
                        associations
                            .Where(x => draft.Accounts.Contains(x.Key))
                            .Select(x => new SaveDataAssociation(x.Key, x.Value))
                            .ToArray());
                case MainAction.SaveAndUpdate:
                    return new(
                        draft,
                        SelectUpdateAccount(application, draft.Accounts, cancellationToken),
                        associations
                            .Where(x => draft.Accounts.Contains(x.Key))
                            .Select(x => new SaveDataAssociation(x.Key, x.Value))
                            .ToArray());
                case MainAction.Cancel:
                    throw new OperationCanceledException("DMMPlugin 配置已取消。");
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    static MainAction RunMainMenu(
        IApplication application,
        DMMPluginSettings draft,
        CancellationToken cancellationToken)
    {
        using var dialog = new Dialog
        {
            Title = Tabs_DMM_Title,
            Width = 92,
            Height = 13,
        };
        var action = MainAction.Cancel;
        var disabled = new CheckBox
        {
            Text = Tabs_DMM_Enable,
            Value = draft.Enable ? CheckState.UnChecked : CheckState.Checked,
            CanFocus = false,
        };
        disabled.ValueChanged += (_, _) =>
            draft.Enable = disabled.Value != CheckState.Checked;

        MenuItem Action(string title, MainAction value, string? help = null)
            => new()
            {
                Title = title,
                HelpText = help ?? string.Empty,
                Action = () =>
                {
                    action = value;
                    application.RequestStop(dialog);
                },
            };

        var items = new[]
        {
            new MenuItem { CommandView = disabled },
            Action(Tabs_DMM_ViewLauncherInformation, MainAction.Launcher),
            Action(Tabs_DMM_EditMachineInformation, MainAction.Machine),
            Action(
                Tabs_DMM_EditAccounts,
                MainAction.Accounts,
                $"{draft.Accounts.Count} account(s)"),
            Action(
                $"{Tabs_DMM_UpdateGame}（保存配置后执行）",
                MainAction.SaveAndUpdate,
                draft.Accounts.Count == 0 ? I18N_Download_NoAccount : null),
            Action("保存", MainAction.Save),
            Action(I18N_Cancel, MainAction.Cancel),
        };
        items[4].Enabled = draft.Accounts.Count > 0;
        var menu = new Menu(items)
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        dialog.Add(menu);
        items[0].SetFocus();
        RunDialog(application, dialog, cancellationToken);
        return action;
    }

    static void ShowLauncherInformation(
        IApplication application,
        DMMLauncherInfomation launcher,
        CancellationToken cancellationToken)
    {
        using var dialog = new Dialog
        {
            Title = Tabs_DMM_ViewLauncherInformation_Caution,
            Width = 92,
            Height = 13,
        };
        dialog.Add(new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 7,
            Text = $"""
                Accept-Encoding: {launcher.AcceptEncoding}
                Accept-Language: {launcher.AcceptLanguage}
                User-Agent: {launcher.UserAgent}
                Client-App: {launcher.ClientApp}
                Client-Version: {launcher.ClientVersion}
                """,
        });
        var close = new Button { Text = "关闭", IsDefault = true };
        close.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        dialog.AddButton(close);
        close.SetFocus();
        RunDialog(application, dialog, cancellationToken);
    }

    static void EditMachineInformation(
        IApplication application,
        DMMMachineInformation machine,
        CancellationToken cancellationToken)
    {
        using var dialog = new Dialog
        {
            Title = Tabs_DMM_EditMachineInformation,
            Width = 96,
            Height = 18,
        };
        var fields = new[]
        {
            AddField(dialog, 1, Tabs_DMM_EditMachineInformation_InputMA, machine.mac_address),
            AddField(dialog, 3, Tabs_DMM_EditMachineInformation_InputHS, machine.hdd_serial),
            AddField(dialog, 5, Tabs_DMM_EditMachineInformation_InputMB, machine.motherboard),
            AddField(dialog, 7, Tabs_DMM_EditMachineInformation_InputOS, machine.user_os),
            AddField(
                dialog,
                9,
                Tabs_DMM_EditMachineInformation_InputUmamusumePath,
                machine.umamusume_file_path),
        };

        var accepted = false;
        var save = new Button { Text = "保存", IsDefault = true };
        save.Accepting += (_, e) =>
        {
            accepted = true;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var cancel = new Button { Text = I18N_Cancel };
        cancel.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        dialog.AddButton(cancel);
        dialog.AddButton(save);
        fields[0].SetFocus();
        RunDialog(application, dialog, cancellationToken);
        if (!accepted)
            return;

        machine.mac_address = fields[0].Text;
        machine.hdd_serial = fields[1].Text;
        machine.motherboard = fields[2].Text;
        machine.user_os = fields[3].Text;
        machine.umamusume_file_path = fields[4].Text;
    }

    static TextField AddField(
        Dialog dialog,
        int y,
        string label,
        string value,
        bool secret = false)
    {
        dialog.Add(new Label
        {
            X = 1,
            Y = y,
            Width = 31,
            Text = label,
        });
        var field = new TextField
        {
            X = 33,
            Y = y,
            Width = Dim.Fill(1),
            Text = value,
            Secret = secret,
        };
        dialog.Add(field);
        return field;
    }

    static void EditAccounts(
        IApplication application,
        DMMPluginSettings draft,
        Dictionary<DMMAccountInformation, bool> associations,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var selection = RunAccountMenu(application, draft.Accounts, cancellationToken);
            if (selection.Action == AccountAction.Back)
                return;

            if (selection.Action == AccountAction.Add)
            {
                var edit = EditAccount(application, new(), draft.Accounts, null, cancellationToken);
                if (edit.Action != AccountEditAction.Save)
                    continue;

                draft.Accounts.Add(edit.Account);
                var defaultSaveDataPath = DefaultSaveDataPath(draft.MachineInformation);
                if (File.Exists(defaultSaveDataPath))
                {
                    var isCurrentAccount = SelectSaveDataOwner(
                        application,
                        edit.Account,
                        defaultSaveDataPath,
                        cancellationToken);
                    if (isCurrentAccount is null)
                    {
                        draft.Accounts.Remove(edit.Account);
                        continue;
                    }
                    associations[edit.Account] = isCurrentAccount.Value;
                }
                continue;
            }

            var account = selection.Account
                ?? throw new InvalidOperationException("DMM account menu did not return an account.");
            var edited = EditAccount(application, Clone(account), draft.Accounts, account, cancellationToken);
            if (edited.Action == AccountEditAction.Delete)
            {
                draft.Accounts.Remove(account);
                associations.Remove(account);
            }
            else if (edited.Action == AccountEditAction.Save)
            {
                var index = draft.Accounts.IndexOf(account);
                draft.Accounts[index] = edited.Account;
                if (associations.Remove(account, out var association))
                    associations[edited.Account] = association;
            }
        }
    }

    static AccountSelection RunAccountMenu(
        IApplication application,
        IReadOnlyList<DMMAccountInformation> accounts,
        CancellationToken cancellationToken)
    {
        using var dialog = new Dialog
        {
            Title = Tabs_DMM_EditAccounts,
            Width = 92,
            Height = Math.Clamp(accounts.Count + 7, 10, 24),
        };
        var selection = new AccountSelection(AccountAction.Back, null);
        var items = new List<MenuItem>();
        foreach (var account in accounts)
        {
            var selectedAccount = account;
            items.Add(new()
            {
                Title = string.IsNullOrWhiteSpace(account.Name) ? account.Account : account.Name,
                HelpText = account.Account,
                Action = () =>
                {
                    selection = new(AccountAction.Edit, selectedAccount);
                    application.RequestStop(dialog);
                },
            });
        }
        items.Add(new(Tabs_DMM_EditAccounts_Add, action: () =>
        {
            selection = new(AccountAction.Add, null);
            application.RequestStop(dialog);
        }));
        items.Add(new(Return, action: () => application.RequestStop(dialog)));

        var menu = new Menu(items)
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        dialog.Add(menu);
        items[0].SetFocus();
        RunDialog(application, dialog, cancellationToken);
        return selection;
    }

    static AccountEditResult EditAccount(
        IApplication application,
        DMMAccountInformation account,
        IReadOnlyList<DMMAccountInformation> existingAccounts,
        DMMAccountInformation? original,
        CancellationToken cancellationToken)
    {
        var allowDelete = original is not null;
        using var dialog = new Dialog
        {
            Title = allowDelete ? Tabs_DMM_EditAccounts : Tabs_DMM_EditAccounts_Add,
            Width = 92,
            Height = 15,
        };
        var name = AddField(dialog, 1, I18N_AccountEdit_Name, account.Name);
        var login = AddField(dialog, 3, I18N_AccountEdit_Account, account.Account);
        var password = AddField(dialog, 5, I18N_AccountEdit_Password, account.Password, secret: true);
        var tokenStatus = account.IsTokenValid() && account.access_token_expires_at.HasValue
            ? string.Format(
                I18N_AccountEdit_TokenValid,
                DateTimeOffset.FromUnixTimeSeconds(account.access_token_expires_at.Value).ToLocalTime())
            : I18N_AccountEdit_TokenInvalid;
        dialog.Add(new Label
        {
            X = 1,
            Y = 7,
            Width = Dim.Fill(1),
            Text = $"{I18N_AccountEdit_TokenStatus}: {tokenStatus}",
        });
        var error = new Label
        {
            X = 1,
            Y = 9,
            Width = Dim.Fill(1),
        };
        dialog.Add(error);

        var action = AccountEditAction.Cancel;
        var save = new Button { Text = "保存", IsDefault = true };
        save.Accepting += (_, e) =>
        {
            var effectiveName = string.IsNullOrWhiteSpace(name.Text) ? login.Text : name.Text;
            if (string.IsNullOrWhiteSpace(effectiveName))
            {
                error.Text = I18N_AccountEdit_EnterName;
                e.Handled = true;
                return;
            }
            if (string.IsNullOrWhiteSpace(login.Text))
            {
                error.Text = I18N_AccountEdit_EnterAccount;
                e.Handled = true;
                return;
            }
            if (string.IsNullOrEmpty(password.Text))
            {
                error.Text = I18N_AccountEdit_EnterPassword;
                e.Handled = true;
                return;
            }
            if (existingAccounts.Any(x =>
                    !ReferenceEquals(x, original) &&
                    string.Equals(x.Name, effectiveName, StringComparison.Ordinal)))
            {
                error.Text = I18N_AccountEdit_NameDuplicate;
                e.Handled = true;
                return;
            }

            account.Name = effectiveName;
            account.Account = login.Text;
            if (password.Text != account.Password)
                account.Password = password.Text;
            action = AccountEditAction.Save;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var cancel = new Button { Text = I18N_Cancel };
        cancel.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        if (allowDelete)
        {
            var delete = new Button { Text = I18N_AccountEdit_Delete };
            delete.Accepting += (_, e) =>
            {
                action = AccountEditAction.Delete;
                application.RequestStop(dialog);
                e.Handled = true;
            };
            dialog.AddButton(delete);
        }
        dialog.AddButton(cancel);
        dialog.AddButton(save);
        name.SetFocus();
        RunDialog(application, dialog, cancellationToken);
        return new(action, account);
    }

    static bool? SelectSaveDataOwner(
        IApplication application,
        DMMAccountInformation account,
        string defaultSaveDataPath,
        CancellationToken cancellationToken)
    {
        using var dialog = new Dialog
        {
            Title = I18N_SaveData_AskIsLastLoginAccount,
            Width = 92,
            Height = 11,
        };
        dialog.Add(new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 4,
            Text = $"{account.Name}\n{defaultSaveDataPath}",
        });
        bool? result = null;
        var yes = new Button { Text = "属于此账号", IsDefault = true };
        yes.Accepting += (_, e) =>
        {
            result = true;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var no = new Button { Text = "不属于此账号" };
        no.Accepting += (_, e) =>
        {
            result = false;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var cancel = new Button { Text = I18N_Cancel };
        cancel.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        dialog.AddButton(cancel);
        dialog.AddButton(no);
        dialog.AddButton(yes);
        yes.SetFocus();
        RunDialog(application, dialog, cancellationToken);
        return result;
    }

    static DMMAccountInformation SelectUpdateAccount(
        IApplication application,
        IReadOnlyList<DMMAccountInformation> accounts,
        CancellationToken cancellationToken)
        => SelectAccount(application, accounts, cancellationToken)
           ?? throw new OperationCanceledException("DMM 游戏更新已取消。");

    static DMMAccountInformation? SelectAccount(
        IApplication application,
        IReadOnlyList<DMMAccountInformation> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 1)
            return accounts[0];

        using var dialog = new Dialog
        {
            Title = I18N_MultipleAccountsFound,
            Width = 92,
            Height = Math.Clamp(accounts.Count + 6, 9, 24),
        };
        DMMAccountInformation? selected = null;
        var items = accounts.Select(account =>
            new MenuItem(
                string.IsNullOrWhiteSpace(account.Name) ? account.Account : account.Name,
                action: () =>
                {
                    selected = account;
                    application.RequestStop(dialog);
                })).ToList();
        items.Add(new(I18N_Cancel, action: () => application.RequestStop(dialog)));
        var menu = new Menu(items)
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        dialog.Add(menu);
        items[0].SetFocus();
        RunDialog(application, dialog, cancellationToken);
        return selected;
    }

    static DMMAccountInformation Clone(DMMAccountInformation account)
        => new()
        {
            Name = account.Name,
            Account = account.Account,
            PasswordEncrypted = account.PasswordEncrypted,
            access_token = account.access_token,
            access_token_expires_at = account.access_token_expires_at,
        };

    static string DefaultSaveDataPath(DMMMachineInformation machine)
    {
        var gameDirectory = Path.GetDirectoryName(machine.umamusume_file_path);
        return gameDirectory is null
            ? string.Empty
            : Path.Combine(
                gameDirectory,
                "umamusume_Data",
                "Persistent",
                "d",
                "SaveData.db");
    }

    static void RunDialog(
        IApplication application,
        Dialog dialog,
        CancellationToken cancellationToken)
    {
        using (cancellationToken.Register(
                   () => application.Invoke(() => application.RequestStop(dialog))))
        {
            application.Run(dialog);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    enum MainAction
    {
        Cancel,
        Launcher,
        Machine,
        Accounts,
        Save,
        SaveAndUpdate,
    }

    enum AccountAction
    {
        Back,
        Add,
        Edit,
    }

    enum AccountEditAction
    {
        Cancel,
        Save,
        Delete,
    }

    sealed record AccountSelection(
        AccountAction Action,
        DMMAccountInformation? Account);

    sealed record AccountEditResult(
        AccountEditAction Action,
        DMMAccountInformation Account);
}
