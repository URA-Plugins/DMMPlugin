# DMMPlugin

DMMPlugin is a Windows plugin for UmamusumeResponseAnalyzer that authenticates with DMM, obtains launch arguments, updates the installed game files, launches Umamusume, and switches `SaveData.db` per account.

## Behavior

- When enabled, the plugin starts after the host is ready. A single configured account is used directly; multiple accounts are selected in a Terminal.Gui dialog.
- The configuration dialog edits launcher headers, machine information, accounts, the game executable path, and the enabled state. It can also update the installed game from the DMM file manifest.
- Expired access tokens are refreshed through DMM authentication. The game process is started on Windows with elevation.
- Per-account save archives are stored beside the game's `SaveData.db` using an account-derived suffix.
- Status, logs, and notifications use the host `TerminalUi` boundary; the plugin does not create a workspace or panel.

## Configuration and data

Settings are stored at `PluginData/DMM插件/settings.yaml` relative to the host working directory. The file contains account and machine information plus DMM access tokens. Newly entered or changed passwords are protected with Windows DPAPI for the current user; passwords loaded from the legacy plaintext format remain plaintext until edited. The settings file and its tokens remain sensitive and must not be committed or shared.

The plugin requires Windows, a valid DMM account, network access to DMM services, and the path to an installed Umamusume executable.

## Build and smoke test

The repository pins the Host source with a Git submodule. From the repository root after cloning:

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\DMMPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
dotnet run --project .\tests\DMMPlugin.Tests\DMMPlugin.Tests.csproj -c Release -p:GenerateUraPluginManifestOnBuild=false -p:PackageUraPluginOnBuild=false -p:DeployUraPluginToLocalAppDataOnBuild=false
```
