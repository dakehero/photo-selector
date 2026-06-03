using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PhotoSelector.Config.Secrets;

public static class SecretStoreFactory
{
    public static ISecretStore CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialSecretStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new CommandSecretStore(
                keyRef => ("security", ["add-generic-password", "-U", "-s", ToServiceName(keyRef), "-a", Environment.UserName, "-w"]),
                keyRef => ("security", ["find-generic-password", "-s", ToServiceName(keyRef), "-a", Environment.UserName, "-w"]),
                keyRef => ("security", ["delete-generic-password", "-s", ToServiceName(keyRef), "-a", Environment.UserName]));
        }

        if (OperatingSystem.IsLinux())
        {
            return new CommandSecretStore(
                keyRef => ("secret-tool", ["store", "--label", $"Photo Selector {keyRef}", "app", "photo-selector", "key_ref", keyRef]),
                keyRef => ("secret-tool", ["lookup", "app", "photo-selector", "key_ref", keyRef]),
                keyRef => ("secret-tool", ["clear", "app", "photo-selector", "key_ref", keyRef]));
        }

        return new UnsupportedSecretStore();
    }

    private static string ToServiceName(string keyRef)
    {
        return $"PhotoSelector:{keyRef}";
    }
}

internal sealed class UnsupportedSecretStore : ISecretStore
{
    public void Set(string keyRef, string secret)
    {
        throw new NotSupportedException("System secret storage is not supported on this platform.");
    }

    public string? Get(string keyRef)
    {
        return null;
    }

    public void Delete(string keyRef)
    {
    }
}

internal sealed class CommandSecretStore : ISecretStore
{
    private readonly Func<string, (string FileName, string[] Args)> setCommand;
    private readonly Func<string, (string FileName, string[] Args)> getCommand;
    private readonly Func<string, (string FileName, string[] Args)> deleteCommand;

    public CommandSecretStore(
        Func<string, (string FileName, string[] Args)> setCommand,
        Func<string, (string FileName, string[] Args)> getCommand,
        Func<string, (string FileName, string[] Args)> deleteCommand)
    {
        this.setCommand = setCommand;
        this.getCommand = getCommand;
        this.deleteCommand = deleteCommand;
    }

    public void Set(string keyRef, string secret)
    {
        var command = setCommand(keyRef);
        Run(command.FileName, command.Args, secret);
    }

    public string? Get(string keyRef)
    {
        var command = getCommand(keyRef);
        var result = Run(command.FileName, command.Args, null, allowFailure: true);
        return result.ExitCode == 0 ? result.Output.TrimEnd('\r', '\n') : null;
    }

    public void Delete(string keyRef)
    {
        var command = deleteCommand(keyRef);
        Run(command.FileName, command.Args, null, allowFailure: true);
    }

    private static CommandResult Run(string fileName, string[] args, string? stdin, bool allowFailure = false)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed: {error.Trim()}");
        }

        return new CommandResult(process.ExitCode, output, error);
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}

internal sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

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
