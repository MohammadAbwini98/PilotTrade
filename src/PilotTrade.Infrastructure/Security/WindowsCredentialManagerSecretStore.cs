namespace PilotTrade.Infrastructure.Security;

using System.Runtime.InteropServices;
using PilotTrade.Domain.Security;

public sealed class WindowsCredentialManagerSecretStore : ISecretStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    private static string TargetFor(string key) => key switch
    {
        "capital:Demo:api_key" => "CapitalTrading/Demo/ApiKey",
        "capital:Demo:password" => "CapitalTrading/Demo/ApiPassword",
        "capital:Demo:identifier" => "CapitalTrading/Demo/Identifier",
        _ => key
    };

    public Task SetAsync(string key, string value, CancellationToken ct)
    {
        var target = TargetFor(key);
        var blob = System.Text.Encoding.Unicode.GetBytes(value);
        var ptr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, ptr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Marshal.StringToHGlobalUni(target),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = ptr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Marshal.StringToHGlobalUni(Environment.UserName)
            };
            if (!CredWrite(ref cred, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return Task.CompletedTask;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var target = TargetFor(key);
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == 1168) return Task.FromResult<string?>(null); // ERROR_NOT_FOUND
            throw new System.ComponentModel.Win32Exception(err);
        }
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0) return Task.FromResult<string?>(null);
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
        }
        finally { CredFree(ptr); }
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var target = TargetFor(key);
        if (!CredDelete(target, CRED_TYPE_GENERIC, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1168) throw new System.ComponentModel.Win32Exception(err);
        }
        return Task.CompletedTask;
    }
}