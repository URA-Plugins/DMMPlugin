using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static DMMPlugin.i18n.DMM;

namespace DMMPlugin;

internal sealed record DMMDeviceInformation(string MacAddress, string HddSerial, string Motherboard)
{
    internal static DMMDeviceInformation Collect()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(I18N_Device_WindowsRequired);

        var machineGuid = new Lazy<string>(() =>
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid") is string { Length: > 0 } value
                    ? value
                    : throw new InvalidDataException(I18N_Device_MachineGuidFailed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw new InvalidDataException(I18N_Device_MachineGuidFailed, ex);
            }
        });

        return new(
            SelectMacAddress(NetworkInterface.GetAllNetworkInterfaces().Select(adapter => (
                adapter.Name,
                adapter.OperationalStatus == OperationalStatus.Up,
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                adapter.GetPhysicalAddress().GetAddressBytes()))),
            ReadHddSerial(DriveInfo.GetDrives, () => machineGuid.Value),
            ReadMotherboard(new ProcessStartInfo("wmic", "bios get serialnumber /VALUE"),
                TimeSpan.FromSeconds(5), () => machineGuid.Value));
    }

    internal static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string ReadHddSerial(Func<DriveInfo[]> enumerateDrives, Func<string> readMachineGuid)
    {
        try
        {
            _ = enumerateDrives();
            return Hash(string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Hash(readMachineGuid());
        }
    }

    internal static string ReadMotherboard(ProcessStartInfo command, TimeSpan timeout, Func<string> readMachineGuid)
    {
        command.CreateNoWindow = true;
        command.UseShellExecute = false;
        command.RedirectStandardOutput = true;
        command.RedirectStandardError = true;
        try
        {
            using var process = Process.Start(command);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var exited = process.WaitForExit((int)timeout.TotalMilliseconds);
                if (!exited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                Task.WhenAll(output, error).GetAwaiter().GetResult();
                if (exited && process.ExitCode == 0)
                {
                    // DMM removes the literal two-character sequence, not actual line breaks.
                    var text = output.GetAwaiter().GetResult().Trim().Replace(@"\n", "", StringComparison.Ordinal);
                    var serial = text[(text.LastIndexOf('=') + 1)..];
                    if (serial.Length > 0)
                        return Hash(serial);
                }
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            // The official Windows implementation uses MachineGuid when wmic fails.
        }
        return Hash(readMachineGuid());
    }

    internal static string SelectMacAddress(IEnumerable<(string Name, bool Up, bool Loopback, byte[] Address)> adapters)
    {
        var selected = (byte[]?)null;
        var highestScore = int.MinValue;
        foreach (var (name, up, loopback, address) in adapters)
        {
            if (address.Length == 0 || address.All(value => value == 0))
                continue;

            var score = (up ? 1 : 0) + (loopback ? 1 : 0);
            if (Regex.IsMatch(name, "^((en|eth)[0-9]+|ethernet)$")) score += 2;
            if (Regex.IsMatch(name, "^(vboxnet[0-9]+)$")) score -= 3;
            if (Regex.IsMatch(name, "^(VirtualBox)+")) score -= 3;
            if (score <= highestScore)
                continue;
            selected = address;
            highestScore = score;
        }

        return selected is null
            ? throw new InvalidDataException(I18N_Device_NoMacAddress)
            : string.Join(':', selected.Select(value => value.ToString("x2")));
    }
}
