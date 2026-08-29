using DMMPlugin;

var tests = new (string Name, Action Run)[]
{
    ("DMM notification uses Host boundary", NotificationUsesHostBoundaryWithoutPluginLifecycleState),
    ("DMM password storage requires DPAPI ciphertext", PasswordStorageRequiresDpapiCiphertext),
    ("DMM settings reject removed formats without rewriting", SettingsRejectRemovedFormatsWithoutRewriting),
    ("DMM authentication parser accepts only the current contract", AuthenticationParserAcceptsOnlyCurrentContract),
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

    Throws<InvalidDataException>(() => DMM.ParseServiceUrl(
        """<input type="hidden" id="js-app-url" data-url = "dmmgameplayer://view/page?code=removed"/>"""));

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
