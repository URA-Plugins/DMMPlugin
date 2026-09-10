# Repository Guidelines

## Structure

`Class1.cs` is the `IPlugin` entry point. The `DMM.*.cs` partials own DMM authentication, file download, launch, and game-data operations. `DMMConfigDialog.cs` owns the Terminal.Gui configuration flow; `DMMPluginSettings.cs` and `DMMConfig.cs` define persisted settings and runtime state. Localization sources are under `i18n/`, and the smoke executable is under `tests/DMMPlugin.Tests/`.

## Build safety and tests

The `UmamusumeResponseAnalyzer` package reference uses `Version="*"` for the latest stable Host build contract. Release builds generate a package with local deployment disabled:

```powershell
dotnet restore .\DMMPlugin.csproj --force --no-http-cache
dotnet build .\DMMPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"
```

Do not add a Host project reference, Gallop package, or plugin-abstractions package to `DMMPlugin.csproj`; the Host NuGet package supplies the compile-time API.

## Code and integration constraints

- Keep the plugin on the host `IPlugin` lifecycle and route status, logs, and notifications through `DMMDisplay`/`TerminalUi`; do not create or own host workspaces or panels.
- Use modern C# and the existing direct structure. Keep external DMM response validation at the protocol boundary and fail with a specific error instead of guessing missing values.
- Authentication accepts the current DMM page contract: hidden `token`/`path`, `ga-param-service-url`, and an absolute redirect URL with a `code` query parameter.
- Edit `.resx` localization sources together for base English, `en-US`, `zh-CN`, and `ja-JP`; do not hand-edit `DMM.Designer.cs`.
- Treat `PluginData/DMM插件/settings.yaml`, DMM credentials, access tokens, machine identifiers, save data, and launch arguments as sensitive. Never commit or log their values.
- Settings loading is strict: current case-sensitive YAML field names only, with `[E]` DPAPI ciphertext for non-empty passwords. Invalid files fail before runtime state changes and are not rewritten.
- Password encryption is bound to the current Windows user through DPAPI. Do not change persisted YAML names or the encryption marker as incidental cleanup.
