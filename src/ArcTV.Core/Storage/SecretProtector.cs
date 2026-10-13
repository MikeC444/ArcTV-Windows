using System.Security.Cryptography;

namespace ArcTV.Core.Storage;

/// <summary>Scrambles bytes so only this Windows user can read them back. Injectable so tests need no Windows.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>Windows DPAPI, tied to the signed-in Windows user.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}

/// <summary>For tests: leaves bytes as they are.</summary>
public sealed class PlainProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data;

    public byte[] Unprotect(byte[] data) => data;
}
