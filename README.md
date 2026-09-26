# DMMPlugin

DMMPlugin is a Windows plugin for UmamusumeResponseAnalyzer that authenticates with DMM, obtains launch arguments, updates the installed game files, launches Umamusume, and switches `SaveData.db` per account.

## Behavior

- When enabled, the plugin starts after the host is ready. A single configured account is used directly; multiple accounts are selected in a Terminal.Gui dialog.
- The configuration dialog displays launcher headers and edits accounts, the game executable path, and the enabled state. It can also update the installed game from the DMM file manifest.
- Device identifiers are collected from Windows when first needed and reused for the plugin lifetime. MAC selection follows DMM's adapter scoring. HDD and motherboard hashes follow the DMM Game Player Windows algorithm; motherboard detection uses `wmic` with a five-second timeout, then the original 64-bit registry `MachineGuid` if that command fails or returns no serial.
- Expired access tokens are refreshed through DMM authentication. The game process is started on Windows with elevation.
- Per-account save archives are stored beside the game's `SaveData.db` using an account-derived suffix.
- Status, logs, and notifications use the host `TerminalUi` boundary; the plugin does not create a workspace or panel.

## Configuration and data

Settings are stored at `PluginData/DMM插件/settings.yaml` relative to the host working directory. The file uses case-sensitive YAML field names and contains account information, DMM access tokens, launcher headers, and the optional game executable path. Passwords are protected with Windows DPAPI for the current user. Differently cased or unknown fields, plaintext passwords, and unreadable DPAPI values fail to load without rewriting the file. The settings file and its tokens remain sensitive and must not be committed or shared.

`machine_information.umamusume_file_path` defaults to an empty string for automatic discovery through Host's `UraCoreHelper.FindDmmGameExecutable`, which reads `%APPDATA%\dmmgameplayer5\dmmgame.cnf`. Discovery requires one installed `umamusume` / `GCL` entry and an existing `umamusume.exe` under its `detail.path`. A non-empty setting specifies an absolute executable path and takes precedence over installation records. Invalid records or paths produce an error asking for a valid executable path. Detected paths stay in memory; launch, version checks, updates, and account saves use the resolved installation for each operation.

Account settings can be saved before the game is installed; save-data association is skipped when the game location cannot be resolved. Only Save applies configuration edits. Cancel, Esc, closing the dialog, and cancellation leave the settings and runtime configuration unchanged.

The plugin requires Windows, a valid DMM account, network access to DMM services, and an installed Umamusume executable.

### Configuration compatibility

The `mac_address`, `hdd_serial`, `motherboard`, and `user_os` keys under `machine_information` from released configurations are accepted but ignored. Loading does not rewrite the file; the next normal save omits those four keys. They are not manual overrides, and accepting them has no scheduled end version. Other unknown keys remain errors.

## Build and smoke test

The repository references Host API and build targets through NuGet with `Version="*"`. Both the Host runtime and reference package must expose `UraCoreHelper.FindDmmGameExecutable`; release them before releasing this plugin. Initialize the plugin source dependency before building:

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\DMMPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"
```

`DMMPlugin.slnx` builds only the plugin. Run the smoke project separately with `UraTestHostRoot` set to the Host source checkout matching the resolved Host NuGet package:

```powershell
dotnet run --project .\tests\DMMPlugin.Tests\DMMPlugin.Tests.csproj -c Release -p:UraTestHostRoot=K:\repos\UmamusumeResponseAnalyzer -p:GenerateUraPluginManifestOnBuild=false -p:PackageUraPluginOnBuild=false -p:DeployUraPluginToLocalAppDataOnBuild=false
```

The smoke checks authentication parsing, DPAPI settings, device hashing and command failures, adapter selection, shared Host discovery, manual path overrides, retired settings keys, and configuration save/cancel behavior. Installation record parsing is tested in Host's `GameDiscoveryTests`. The smoke uses synthetic account data and does not authenticate with DMM.

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
