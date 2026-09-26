# Repository Guidelines

## Structure

`Class1.cs` is the `IPlugin` entry point. The `DMM.*.cs` partials own DMM authentication, file download, launch, and game-data operations. `DMMConfigDialog.cs` owns the Terminal.Gui configuration flow; `DMMPluginSettings.cs` and `DMMConfig.cs` define persisted settings and runtime state. Localization sources are under `i18n/`, and the smoke executable is under `tests/DMMPlugin.Tests/`.

## Build safety and tests

Keep `DMMPlugin.slnx` limited to the plugin project. Run `tests/DMMPlugin.Tests/DMMPlugin.Tests.csproj` separately with an explicit `UraTestHostRoot`; the smoke requires Host runtime source and must not participate in the default Visual Studio solution restore.

The `UmamusumeResponseAnalyzer` package reference uses `Version="*"` for the latest stable Host build contract. Release builds generate a package with local deployment disabled:

```powershell
dotnet restore .\DMMPlugin.csproj --force --no-http-cache
dotnet build .\DMMPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"
```

Do not add a Host project reference, Gallop package, or plugin-abstractions package to `DMMPlugin.csproj`; the Host NuGet package supplies the compile-time API. The Host runtime and reference package must expose `UraCoreHelper.FindDmmGameExecutable` and be released before this plugin. For local validation, pack the Host into a temporary NuGet source and restore with an isolated package cache; retain `Version="*"` in the production project.

## Code and integration constraints

- Keep the plugin on the host `IPlugin` lifecycle and route status, logs, and notifications through `DMMDisplay`/`TerminalUi`; do not create or own host workspaces or panels.
- Use modern C# and the existing direct structure. Keep external DMM response validation at the protocol boundary and fail with a specific error instead of guessing missing values.
- Authentication accepts hidden `token`/`path`, `ga-param-service-url`, `js-app-url` with a `dmmgameplayer://view/page?code=...` URL, and absolute redirect URLs carrying a `code` query parameter.
- Edit `.resx` localization sources together for base English, `en-US`, `zh-CN`, and `ja-JP`; do not hand-edit `DMM.Designer.cs`.
- Treat `PluginData/DMM插件/settings.yaml`, DMM credentials, access tokens, machine identifiers, save data, and launch arguments as sensitive. Never commit or log their values.
- Settings loading is strict: current case-sensitive YAML field names only, with `[E]` DPAPI ciphertext for non-empty passwords. Invalid files fail before runtime state changes and are not rewritten.
- Password encryption is bound to the current Windows user through DPAPI. Do not change persisted YAML names or the encryption marker as incidental cleanup.
- Device information is an internal lazy snapshot owned by the plugin instance. Launch and install requests share it; never persist or log its values. Follow the Windows algorithm in the DMMGamePlayerReverseEngineering report: SHA-256 of the empty string after successful drive enumeration, SHA-256 of the raw MachineGuid from the 64-bit registry view on enumeration failure, and hidden `wmic bios get serialnumber /VALUE` with a five-second timeout before the MachineGuid motherboard path. Do not substitute WMI when `wmic` is unavailable.
- Only `machine_information.umamusume_file_path` is persisted for machine configuration. Empty calls Host's `UraCoreHelper.FindDmmGameExecutable`; a non-empty absolute path is an explicit override. Keep installation record parsing and its localized errors in Host, and configuration guidance in the plugin. Do not add an older Host adapter or a second parser. Resolve once per operation and pass that path through version, update, launch, and save-data operations. Do not write a discovered path back into settings or guess missing installation records.
- Ignore only the exact `mac_address`, `hdd_serial`, `motherboard`, and `user_os` keys under `machine_information` when reading released settings. Loading never rewrites the file; normal saves omit those keys. This acceptance has no scheduled removal version; all other unknown fields remain errors.
- Configuration remains available before installation. Skip save-data association when the game path cannot be resolved; validate a launch/update path before authentication or save-data writes. Cancel, Esc, window close, and cancellation discard the draft. Edit all four `.resx` files together and regenerate the resource designer with ResGen.
