using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

[assembly: SupportedOSPlatform("windows")]

namespace DMMPlugin;

/// <summary>
/// Windows DPAPI-NG 密码加密 - 绑定当前用户 SID
/// </summary>
public static class SecurePassword
{
    private const string Marker = "[E]";
    // SHA256.HashData(Encoding.UTF8.GetBytes("DMMPlugin"))
    private static readonly byte[] Entropy = [0x28, 0xA0, 0x03, 0xFF, 0x48, 0xD9, 0x83, 0x25, 0x63, 0x58, 0xF8, 0xAC, 0xA1, 0x1F, 0x51, 0x28, 0x31, 0x67, 0x86, 0xB0, 0x20, 0x82, 0xD8, 0xAB, 0x72, 0x1B, 0x04, 0xAC, 0x4A, 0xA7, 0x00, 0xC9];

    public static string Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.StartsWith(Marker)) return plain ?? "";
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Marker + Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        if (!encrypted.StartsWith(Marker)) return encrypted; // 旧数据直接返回，下次保存时会加密
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted[Marker.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("DMM 密码解密失败。可能是 Windows 用户、机器或 DPAPI 状态变更，请重新配置账号密码。", ex);
        }
    }
}
