using System.Runtime.InteropServices;
using System.Text;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace FFXIVSimpleLauncher.Services.Platform.Windows;

/// <summary>
/// Windows implementation of credential service using Windows Credential Manager.
/// </summary>
public class WindowsCredentialService : ICredentialService
{
    private const string CredentialTarget = "FFXIVSimpleLauncher";

    public void SavePassword(string username, string password)
    {
        var target = $"{CredentialTarget}-{username}";
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var passwordPtr = Marshal.AllocHGlobal(passwordBytes.Length);

        try
        {
            Marshal.Copy(passwordBytes, 0, passwordPtr, passwordBytes.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE.GENERIC,
                TargetName = target,
                UserName = username,
                CredentialBlob = passwordPtr,
                CredentialBlobSize = (uint)passwordBytes.Length,
                Persist = CRED_PERSIST.LOCAL_MACHINE
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Exception("Failed to save credential");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(passwordPtr);
        }
    }

    public string? GetPassword(string username)
    {
        var target = $"{CredentialTarget}-{username}";

        if (CredRead(target, CRED_TYPE.GENERIC, 0, out var credentialPtr))
        {
            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
                {
                    var passwordBytes = new byte[credential.CredentialBlobSize];
                    Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, (int)credential.CredentialBlobSize);
                    return Encoding.Unicode.GetString(passwordBytes);
                }
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        return null;
    }

    public void DeletePassword(string username)
    {
        var target = $"{CredentialTarget}-{username}";
        CredDelete(target, CRED_TYPE.GENERIC, 0);
    }

    #region Windows Credential API

    private enum CRED_TYPE : uint
    {
        GENERIC = 1,
    }

    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    #endregion
}
