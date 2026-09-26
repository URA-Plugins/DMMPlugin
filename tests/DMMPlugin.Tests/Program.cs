using DMMPlugin;

using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Resources = DMMPlugin.i18n.DMM;

if (args is ["--bios", var scenario])
{
    if (scenario == "timeout") Thread.Sleep(30_000);
    Console.Write(scenario == "empty" ? "\r\nSerialNumber= \r\n" : " \r\nSerialNumber=discard= AbC\\n123\r\n \t");
    Environment.Exit(scenario == "failure" ? 1 : 0);
}

var tests = new (string Name, Action Run)[]
{
    ("DMM notification uses Host boundary", NotificationUsesHostBoundaryWithoutPluginLifecycleState),
    ("DMM password storage requires DPAPI ciphertext", PasswordStorageRequiresDpapiCiphertext),
    ("DMM settings reject removed formats without rewriting", SettingsRejectRemovedFormatsWithoutRewriting),
    ("DMM authentication parser accepts only the current contract", AuthenticationParserAcceptsOnlyCurrentContract),
    ("DMM device hashes and wmic failure paths", DeviceHashesAndBiosFailures),
    ("DMM MAC selection follows adapter scoring", MacSelectionFollowsScoring),
    ("DMM game paths use Host discovery or manual overrides", GamePathsUseHostDiscovery),
    ("DMM settings ignore only retired device keys", SettingsIgnoreOnlyRetiredDeviceKeys),
    ("DMM invalid paths fail before launch or install side effects", InvalidPathsFailBeforeSideEffects),
    ("DMM game requests share a lazy device snapshot", GameRequestsShareDeviceSnapshot),
    ("DMM configuration saves drafts and discards canceled edits", ConfigurationDraftBehavior),
};

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}");
        Console.Error.WriteLine(ex);
        Environment.Exit(1);
    }
}

static void NotificationUsesHostBoundaryWithoutPluginLifecycleState()
{
    try
    {
        DMMDisplay.Notify("notification");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("TerminalUi", StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException("Expected the uninitialized Host boundary to reject the notification.");
}

static void PasswordStorageRequiresDpapiCiphertext()
{
    const string password = "[E] is valid plaintext";
    var encrypted = SecurePassword.Encrypt(password);
    if (encrypted == password || SecurePassword.Decrypt(encrypted) != password)
        throw new InvalidOperationException("DPAPI password round-trip failed.");

    Throws<InvalidDataException>(() => SecurePassword.Decrypt("plaintext"));
}

static void SettingsRejectRemovedFormatsWithoutRewriting()
{
    var directory = Path.Combine(Path.GetTempPath(), $"dmm-plugin-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "settings.yaml");
        var encrypted = SecurePassword.Encrypt("secret");
        File.WriteAllText(path, $$"""
            accounts:
            - name: current
              account: current
              password: '{{encrypted}}'
              access-token: ''
            enable: false
            """);
        if (DMMPluginSettings.Load(path).Accounts.Single().Password != "secret")
            throw new InvalidOperationException("Current settings schema did not load.");

        const string oldYaml = "Accounts: []\nEnable: false\n";
        File.WriteAllText(path, oldYaml);
        Throws<InvalidDataException>(() => DMMPluginSettings.Load(path));
        if (File.ReadAllText(path) != oldYaml)
            throw new InvalidOperationException("Rejected YAML was rewritten.");

        const string plaintextYaml = "accounts:\n- name: plaintext\n  account: plaintext\n  password: plaintext\nenable: false\n";
        File.WriteAllText(path, plaintextYaml);
        Throws<InvalidDataException>(() => DMMPluginSettings.Load(path));
        if (File.ReadAllText(path) != plaintextYaml)
            throw new InvalidOperationException("Rejected plaintext password was rewritten.");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void AuthenticationParserAcceptsOnlyCurrentContract()
{
    var (token, path) = DMM.ParseLoginForm(
        """<input type="hidden" name="token" value="token"/><input type="hidden" name="path" value="path"/>""");
    if (token != "token" || path != "path")
        throw new InvalidOperationException("Current login form was not parsed.");

    Throws<InvalidDataException>(() => DMM.ParseLoginForm(
        """<input type="hidden" name="token" value="token"/>"""));

    var serviceUrl = DMM.ParseServiceUrl(
        """<input type="hidden" id="ga-param-service-url" value="https://example.test/oauth?state=a&amp;value=b"/>""");
    if (serviceUrl != "https://example.test/oauth?state=a&value=b")
        throw new InvalidOperationException("Current service URL was not parsed.");

    var appUrl = DMM.ParseServiceUrl(
        """<input type="hidden" id="js-app-url" data-url = "dmmgameplayer://view/page?code=restored"/>""");
    if (DMM.ParseOAuthCode(appUrl) != "restored")
        throw new InvalidOperationException("DMM Game Player OAuth URL was not parsed.");

    if (DMM.ParseOAuthCode("https://example.test/callback?code=current&state=state") != "current")
        throw new InvalidOperationException("Current OAuth code was not parsed.");

    Throws<InvalidDataException>(() => DMM.ParseOAuthCode(
        "https://example.test/callback?state=state#code=removed"));
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void DeviceHashesAndBiosFailures()
{
    const string guid = " AbC-123 \r\n";
    const string guidHash = "56bd403f548b7e03d76a3ab492a8db46e4f9c2ca48f19a25f878e49b966b1c52";
    Require(DMMDeviceInformation.Hash(guid) == guidHash, "MachineGuid must be hashed verbatim.");
    Require(DMMDeviceInformation.ReadHddSerial(() => [], () => throw new Exception("Unexpected MachineGuid read"))
        == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "Normal HDD hash differs.");
    Require(DMMDeviceInformation.ReadHddSerial(() => throw new IOException("disk enumeration failed"), () => guid)
        == guidHash, "Failed disk enumeration must use MachineGuid.");

    Require(DMMDeviceInformation.ReadMotherboard(BiosCommand("success"), TimeSpan.FromSeconds(5),
        () => throw new Exception("Unexpected MachineGuid read"))
        == "35190014c813118f5aece592f3b9236cc4447cd30111cbcb93c6c6f961dea955", "BIOS parsing differs from DMM.");
    foreach (var scenario in new[] { "empty", "failure", "timeout", "missing" })
    {
        var command = scenario == "missing"
            ? new ProcessStartInfo(Path.Combine(Path.GetTempPath(), $"missing-wmic-{Guid.NewGuid():N}.exe"))
            : BiosCommand(scenario);
        var timer = Stopwatch.StartNew();
        var actual = DMMDeviceInformation.ReadMotherboard(command,
            TimeSpan.FromMilliseconds(scenario == "timeout" ? 200 : 5000), () => guid);
        Require(actual == guidHash, $"{scenario} must use MachineGuid.");
        Require(timer.Elapsed < TimeSpan.FromSeconds(10), $"{scenario} did not finish promptly.");
    }
    Throws<InvalidDataException>(() => DMMDeviceInformation.ReadMotherboard(
        BiosCommand("empty"), TimeSpan.FromSeconds(5), () => throw new InvalidDataException("MachineGuid unavailable")));
}

static ProcessStartInfo BiosCommand(string scenario)
{
    var command = new ProcessStartInfo(Environment.ProcessPath!);
    command.ArgumentList.Add("--bios");
    command.ArgumentList.Add(scenario);
    return command;
}

static void MacSelectionFollowsScoring()
{
    var first = new byte[] { 0xAB, 0xCD, 0xEF, 1, 2, 3 };
    var second = new byte[] { 2, 3, 4, 5, 6, 7 };
    var selected = DMMDeviceInformation.SelectMacAddress([
        ("eth0", true, false, []), ("eth1", true, false, new byte[6]),
        ("vboxnet0", true, false, second), ("VirtualBox Host", true, false, second),
        ("Ethernet", true, false, second), ("ethernet", true, false, first),
        ("eth2", true, false, second),
    ]);
    Require(selected == "ab:cd:ef:01:02:03", "MAC scoring, case sensitivity, tie order or formatting differs.");
    Require(DMMDeviceInformation.SelectMacAddress([
        ("offline", false, false, second), ("online", true, false, first)]) == selected, "Up flag was ignored.");
    Require(DMMDeviceInformation.SelectMacAddress([
        ("normal", true, false, second), ("loop", true, true, first)]) == selected, "Loopback flag was ignored.");
    foreach (var preferred in new[] { "en0", "eth0", "ethernet" })
        Require(DMMDeviceInformation.SelectMacAddress([
            ("normal", true, false, second), (preferred, false, false, first)]) == selected, "Ethernet name bonus differs.");
    foreach (var virtualAdapter in new[] { "vboxnet0", "VirtualBox Host" })
        Require(DMMDeviceInformation.SelectMacAddress([
            (virtualAdapter, true, false, second), ("normal", false, false, first)]) == selected, "Virtual adapter penalty differs.");
    Throws<InvalidDataException>(() => DMMDeviceInformation.SelectMacAddress([
        ("empty", true, false, []), ("zero", true, false, new byte[6]) ]));
}

static void GamePathsUseHostDiscovery() => InTemporaryDirectory(directory =>
{
    var installed = Path.Combine(directory, "installed");
    Directory.CreateDirectory(installed);
    var executable = Path.Combine(installed, "umamusume.exe");
    File.Copy(Environment.ProcessPath!, executable);
    var record = new JObject
    {
        ["productId"] = "umamusume", ["gameType"] = "GCL",
        ["detail"] = new JObject { ["installed"] = true, ["path"] = installed },
    };
    var config = new JObject { ["contents"] = new JArray(record) };
    var configPath = Path.Combine(directory, "dmmgame.cnf");
    File.WriteAllText(configPath, config.ToString());
    var machine = new DMMConfig.DMMMachineInformation();
    var resolved = DMM.ResolveGamePath(machine, configPath);
    Require(resolved == UmamusumeResponseAnalyzer.UraCoreHelper.FindDmmGameExecutable(configPath),
        "Automatic mode must use the Host discovery contract.");
    Require(resolved == executable && machine.umamusume_file_path == "", "Discovery changed the explicit path setting.");
    Require(File.ReadAllText(configPath) == config.ToString(), "Discovery changed official installation records.");
    Require(DMM.GetGameVersion(resolved) == FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion,
        "Version lookup did not use the resolved executable.");
    var account = new DMMConfig.DMMAccountInformation { Account = "synthetic-account" };
    Require(DMMConfig.DMMAccountInformation.DefaultSaveDataPath(resolved)
        == Path.Combine(installed, "umamusume_Data", "Persistent", "d", "SaveData.db"), "Save data used a different installation.");
    Require(account.GetSaveDataPath(resolved).StartsWith(DMMConfig.DMMAccountInformation.DefaultSaveDataPath(resolved) + "."),
        "Account archive used a different installation.");

    var moved = Path.Combine(directory, "moved");
    Directory.CreateDirectory(moved);
    File.Copy(executable, Path.Combine(moved, "umamusume.exe"));
    record["detail"]!["path"] = moved;
    File.WriteAllText(configPath, config.ToString());
    Require(DMM.ResolveGamePath(machine, configPath) == Path.Combine(moved, "umamusume.exe"), "Automatic discovery was persisted or cached.");
    Require(Path.GetDirectoryName(resolved) == installed, "An operation's resolved path changed.");

    File.WriteAllText(configPath, "broken json");
    try
    {
        DMM.ResolveGamePath(machine, configPath);
        throw new InvalidOperationException("Host discovery failure was ignored.");
    }
    catch (InvalidDataException ex)
    {
        Require(ex.InnerException is InvalidDataException && ex.InnerException.Message.Contains(configPath)
            && ex.Message == string.Format(Resources.I18N_Path_DiscoveryFailed, ex.InnerException.Message),
            "The Host error must retain its source and include plugin configuration guidance.");
    }
    Require(DMM.ResolveGamePath(new() { umamusume_file_path = executable }, configPath) == executable,
        "An explicit path must bypass Host discovery.");
    Require(UmamusumeResponseAnalyzer.UraCoreHelper.FindDmmGameExecutable(Path.Combine(directory, "absent.cnf")) is null,
        "Host discovery must return null when no installation record exists.");
    try
    {
        DMM.ResolveGamePath(machine, Path.Combine(directory, "absent.cnf"));
        throw new InvalidOperationException("Automatic mode accepted a missing installation.");
    }
    catch (InvalidDataException ex)
    {
        Require(ex.Message == Resources.I18N_Path_NotFound, "Missing installations must show manual path guidance.");
    }
    Throws<InvalidDataException>(() => DMM.ResolveGamePath(new() { umamusume_file_path = "umamusume.exe" }, configPath));
});

static void SettingsIgnoreOnlyRetiredDeviceKeys() => InTemporaryDirectory(directory =>
{
    var path = Path.Combine(directory, "settings.yaml");
    var password = SecurePassword.Encrypt("synthetic-password");
    var yaml = $$"""
        machine_information:
          mac_address: retired-mac
          hdd_serial: retired-disk
          motherboard: retired-board
          user_os: retired-os
          umamusume_file_path: ''
        accounts:
        - name: test
          account: test
          password: '{{password}}'
        enable: true
        """;
    File.WriteAllText(path, yaml);
    var settings = DMMPluginSettings.Load(path);
    Require(File.ReadAllText(path) == yaml, "Loading retired fields rewrote the file.");
    Require(settings.Enable && settings.Accounts.Single().PasswordEncrypted == password, "Unrelated settings changed.");
    settings.Save(path);
    var saved = File.ReadAllText(path);
    Require(!saved.Contains("retired-") && !saved.Contains("mac_address") && !saved.Contains("hdd_serial")
        && !saved.Contains("motherboard") && !saved.Contains("user_os"), "Retired device fields were persisted.");
    Require(DMMPluginSettings.Load(path).MachineInformation.umamusume_file_path == "", "Automatic mode did not survive save.");
    foreach (var invalid in new[]
    {
        yaml.Replace("mac_address:", "Mac_address:"),
        yaml.Replace("hdd_serial:", "unknown_device_key:"),
        yaml + "\nmac_address: not-under-machine-information\n",
    })
    {
        File.WriteAllText(path, invalid);
        Throws<InvalidDataException>(() => DMMPluginSettings.Load(path));
        Require(File.ReadAllText(path) == invalid, "Rejected settings were rewritten.");
    }
});

static void InvalidPathsFailBeforeSideEffects() => InTemporaryDirectory(directory =>
{
    var original = DMMConfig.MachineInformation;
    try
    {
        DMMConfig.MachineInformation = new() { umamusume_file_path = Path.Combine(directory, "not-installed.exe") };
        var account = new DMMConfig.DMMAccountInformation();
        Throws<InvalidDataException>(() => DMM.RunUmamusume(account).GetAwaiter().GetResult());
        Throws<InvalidDataException>(() => DMM.GetInstallInfoAsync(account).GetAwaiter().GetResult());
        Require(DMMConfigDialog.FindInstalledGameForAssociation(DMMConfig.MachineInformation) is null,
            "Account editing must skip unavailable save data.");
        Require(!Directory.EnumerateFileSystemEntries(directory).Any(), "Failed path validation wrote files.");
    }
    finally { DMMConfig.MachineInformation = original; }
});

static void GameRequestsShareDeviceSnapshot()
{
    var plugin = new DMMPlugin.DMMPlugin();
    var nextPlugin = new DMMPlugin.DMMPlugin();
    try
    {
        Require(!plugin.DeviceInformation.IsValueCreated, "Device information was collected eagerly.");
        DMM.PluginInstance = plugin;
        var first = DMM.CreateGameRequest();
        var snapshot = plugin.DeviceInformation.Value;
        var second = DMM.CreateGameRequest();
        Require(ReferenceEquals(snapshot, plugin.DeviceInformation.Value) && JToken.DeepEquals(first, second),
            "Launch and install must share the device snapshot.");
        Require((string?)first["user_os"] == "win" && snapshot.HddSerial.Length == 64 && snapshot.Motherboard.Length == 64
            && !string.IsNullOrEmpty(snapshot.MacAddress), "Collected device information has an invalid shape.");
        Require(!nextPlugin.DeviceInformation.IsValueCreated, "A new plugin instance reused an old snapshot.");
    }
    finally
    {
        plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        nextPlugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

static void ConfigurationDraftBehavior() => InTemporaryDirectory(directory =>
{
    var originalDirectory = Environment.CurrentDirectory;
    var originalMachine = DMMConfig.MachineInformation;
    var originalAccounts = DMMConfig.Accounts;
    var originalCulture = CultureInfo.CurrentUICulture;
    Environment.CurrentDirectory = directory;
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    try
    {
        foreach (var finish in new[] { "Cancel", "Esc", "Close", "Token", "InvalidUpdate", "Save" })
        {
            var plugin = new DMMPlugin.DMMPlugin();
            new DMMPluginSettings
            {
                Accounts = [new() { Name = "synthetic", Account = "synthetic" }],
            }.Save(plugin.SettingsFilePath);
            plugin.LoadSettings();
            plugin.SyncToStatic();
            var original = File.ReadAllBytes(plugin.SettingsFilePath);
            using var application = Application.Create(new VirtualTimeProvider()).Init(DriverRegistry.Names.ANSI);
            application.Driver!.SetScreenSize(120, 30);
            using var cancellation = new CancellationTokenSource();
            var stage = 0;
            var failure = (Exception?)null;
            application.SessionBegun += (_, _) => application.AddTimeout(TimeSpan.Zero, () =>
            {
                var dialog = (Dialog)application.TopRunnable!;
                try
                {
                    if (stage++ == 0)
                        Descendants(dialog).OfType<MenuItem>().Single(item => item.Title == Resources.Tabs_DMM_EditMachineInformation).Action!();
                    else if (stage == 2)
                    {
                        var field = Descendants(dialog).OfType<TextField>().Single();
                        Require(Descendants(dialog).OfType<Label>().Any(label => label.Text == Resources.I18N_Path_AutoDiscover),
                            "Automatic discovery guidance is missing.");
                        field.Text = Path.Combine(directory, "not-installed.exe");
                        dialog.Buttons.Single(button => button.Text == "保存").SetFocus();
                        application.Keyboard.RaiseKeyDownEvent(Key.Enter);
                    }
                    else
                    {
                        switch (finish)
                        {
                            case "Cancel": Descendants(dialog).OfType<MenuItem>().Single(item => item.Title == Resources.I18N_Cancel).Action!(); break;
                            case "Esc": application.Keyboard.RaiseKeyDownEvent(Key.Esc); break;
                            case "Close": application.RequestStop(dialog); break;
                            case "Token": cancellation.Cancel(); break;
                            case "InvalidUpdate": Descendants(dialog).OfType<MenuItem>().Single(item => item.Title.StartsWith(Resources.Tabs_DMM_UpdateGame)).Action!(); break;
                            case "Save": Descendants(dialog).OfType<MenuItem>().Single(item => item.Title == "保存").Action!(); break;
                        }
                        return false;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    failure = new InvalidOperationException($"Configuration {finish}, stage {stage}, views: " +
                        string.Join(", ", Descendants(dialog).Select(view => view.GetType().Name + ":" + view.Title)), ex);
                    cancellation.Cancel();
                    application.RequestStop(dialog);
                    return false;
                }
            });
            if (finish == "Save") plugin.ConfigPromptAsync(application, cancellation.Token).GetAwaiter().GetResult();
            else if (finish == "InvalidUpdate") Throws<InvalidDataException>(() => plugin.ConfigPromptAsync(application, cancellation.Token).GetAwaiter().GetResult());
            else Throws<OperationCanceledException>(() => plugin.ConfigPromptAsync(application, cancellation.Token).GetAwaiter().GetResult());
            if (failure is not null) throw failure;
            Require(stage == 3, "Configuration flow did not traverse the path editor and main menu.");
            if (finish == "Save")
                Require(DMMPluginSettings.Load(plugin.SettingsFilePath).MachineInformation.umamusume_file_path
                    == Path.Combine(directory, "not-installed.exe") && DMMConfig.MachineInformation.umamusume_file_path
                    == Path.Combine(directory, "not-installed.exe"), "Save did not apply and persist the draft.");
            else
                Require(File.ReadAllBytes(plugin.SettingsFilePath).SequenceEqual(original)
                    && DMMConfig.MachineInformation.umamusume_file_path == "", "Canceled configuration changed disk or runtime state.");
            plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
    finally
    {
        Environment.CurrentDirectory = originalDirectory;
        DMMConfig.MachineInformation = originalMachine;
        DMMConfig.Accounts = originalAccounts;
        CultureInfo.CurrentUICulture = originalCulture;
    }
});

static IEnumerable<View> Descendants(View root)
{
    yield return root;
    foreach (var child in root.SubViews)
    foreach (var descendant in Descendants(child)) yield return descendant;
}

static void InTemporaryDirectory(Action<string> run)
{
    var directory = Path.Combine(Path.GetTempPath(), $"dmm-plugin-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try { run(directory); }
    finally { Directory.Delete(directory, true); }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
