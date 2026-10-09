﻿﻿using System;
using System.Security.Cryptography;
using System.Text;

namespace DeepSeekBalance.Helpers;

/// <summary>
/// 基于 Windows DPAPI（数据保护 API）的本地密钥加解密封装。
/// 加密密钥由当前 Windows 用户登录凭据派生并由操作系统保管：
/// 只有同一台计算机上的同一用户才能解密，本插件无需生成、存储或分发任何加密密钥。
/// 参考：https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata
/// </summary>
public static class SecretProtector
{
    /// <summary>
    /// 应用专属附加熵（optionalEntropy）。它不是加密密钥，仅用于让本插件的密文
    /// 与机器上其它 DPAPI 使用相互隔离；即使泄露该熵，没有当前用户凭据仍无法解密。
    /// </summary>
    private static readonly byte[] Entropy =
    {
        0x44, 0x65, 0x65, 0x70, 0x53, 0x65, 0x65, 0x6B,
        0x42, 0x61, 0x6C, 0x61, 0x6E, 0x63, 0x65, 0x21
    };

    /// <summary>
    /// 加密明文，返回 Base64 编码的密文。
    /// fail-closed：非 Windows 平台无法使用 DPAPI 时抛出异常，绝不返回明文，
    /// 避免密文在异常环境下被当作"已加密"内容写回。
    /// </summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("API 密钥加密依赖 Windows DPAPI，当前平台不支持。");
        }

        var data = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// 解密 <see cref="Protect"/> 产生的 Base64 密文。
    /// fail-closed：非 Windows 平台抛出异常。
    /// 密文损坏、用户或机器不匹配时也会抛出异常，由调用方处理。
    /// </summary>
    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return protectedBase64;
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("API 密钥解密依赖 Windows DPAPI，当前平台不支持。");
        }

        var data = Convert.FromBase64String(protectedBase64);
        var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
