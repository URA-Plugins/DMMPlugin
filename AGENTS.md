# Repository Guidelines

## Structure

`Class1.cs` is the `IPlugin` entry point. The `DMM.*.cs` partials own DMM authentication, file download, launch, and game-data operations. `DMMConfigDialog.cs` owns the Terminal.Gui configuration flow; `DMMPluginSettings.cs` and `DMMConfig.cs` define persisted settings and runtime state. Localization sources are under `i18n/`, and the smoke executable is under `tests/DMMPlugin.Tests/`.

## Build safety and tests

The parent `Directory.Build.targets` supplies the host project reference and packaging targets. For ordinary development, always disable manifest generation, packaging, and local deployment, and pass the host project explicitly:

```powershell
dotnet build .\DMMPlugin.slnx -m:1 -p:GenerateUraPluginManifestOnBuild=false -p:PackageUraPluginOnBuild=false -p:DeployUraPluginToLocalAppDataOnBuild=false -p:UraHostProjectPath="<ura-host-project>"
dotnet run --project .\tests\DMMPlugin.Tests\DMMPlugin.Tests.csproj -m:1 -p:GenerateUraPluginManifestOnBuild=false -p:PackageUraPluginOnBuild=false -p:DeployUraPluginToLocalAppDataOnBuild=false -p:UraHostProjectPath="<ura-host-project>"
```

Do not add a separate host, Gallop, or plugin-abstractions reference to `DMMPlugin.csproj`; the shared targets own that reference.

## Code and integration constraints

- Keep the plugin on the host `IPlugin` lifecycle and route status, logs, and notifications through `DMMDisplay`/`TerminalUi`; do not create or own host workspaces or panels.
- Use modern C# and the existing direct structure. Keep external DMM response validation at the protocol boundary and fail with a specific error instead of guessing missing values.
- Edit `.resx` localization sources together for base English, `en-US`, `zh-CN`, and `ja-JP`; do not hand-edit `DMM.Designer.cs`.
- Treat `PluginData/DMM插件/settings.yaml`, DMM credentials, access tokens, machine identifiers, save data, and launch arguments as sensitive. Never commit or log their values.
- Password encryption is bound to the current Windows user through DPAPI. Do not change persisted YAML names or the encryption marker as incidental cleanup.
