using System.Runtime.InteropServices;
using System.Text;

namespace PhotoSelector.Config.Secrets;

public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    public string ProviderName => "windows-credential-manager";

    public SecretStoreStatus GetStatus()
    {
        return OperatingSystem.IsWindows()
            ? new SecretStoreStatus(true, null)
            : new SecretStoreStatus(false, "Windows Credential Manager is only available on Windows.");
    }

    public void Set(string keyRef, string secret)
    {
        var secretBytes = Encoding.Unicode.GetBytes(secret);
        var credential = new NativeCredential
        {
            Type = CredentialTypeGeneric,
            TargetName = ToTargetName(keyRef),
            CredentialBlobSize = secretBytes.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(secretBytes.Length),
            Persist = CredentialPersistLocalMachine,
            UserName = "PhotoSelector",
        };

        try
        {
            Marshal.Copy(secretBytes, 0, credential.CredentialBlob, secretBytes.Length);
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Could not store secret: {Marshal.GetLastPInvokeError()}");
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(credential.CredentialBlob);
        }
    }

    public string? Get(string keyRef)
    {
        if (!CredRead(ToTargetName(keyRef), CredentialTypeGeneric, 0, out var credentialPointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Delete(string keyRef)
    {
        CredDelete(ToTargetName(keyRef), CredentialTypeGeneric, 0);
    }

    private static string ToTargetName(string keyRef)
    {
        return $"PhotoSelector:{keyRef}";
    }

    [DllImport("advapi32", SetLastError = true, EntryPoint = "CredWriteW", CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32", SetLastError = true, EntryPoint = "CredReadW", CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32", SetLastError = true, EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32", SetLastError = false)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
