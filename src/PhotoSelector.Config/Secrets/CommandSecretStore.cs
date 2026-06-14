using System.Diagnostics;

namespace PhotoSelector.Config.Secrets;

public abstract class CommandSecretStore : ISecretStore
{
    private readonly string toolName;
    private readonly string unavailableMessage;
    private readonly Func<string, (string FileName, string[] Args)> setCommand;
    private readonly Func<string, (string FileName, string[] Args)> getCommand;
    private readonly Func<string, (string FileName, string[] Args)> deleteCommand;

    protected CommandSecretStore(
        string toolName,
        string unavailableMessage,
        Func<string, (string FileName, string[] Args)> setCommand,
        Func<string, (string FileName, string[] Args)> getCommand,
        Func<string, (string FileName, string[] Args)> deleteCommand)
    {
        this.toolName = toolName;
        this.unavailableMessage = unavailableMessage;
        this.setCommand = setCommand;
        this.getCommand = getCommand;
        this.deleteCommand = deleteCommand;
    }

    public abstract string ProviderName { get; }

    public SecretStoreStatus GetStatus()
    {
        return IsCommandAvailable(toolName)
            ? new SecretStoreStatus(true, null)
            : new SecretStoreStatus(false, unavailableMessage);
    }

    public void Set(string keyRef, string secret)
    {
        EnsureAvailable();
        var command = setCommand(keyRef);
        Run(command.FileName, command.Args, secret);
    }

    public string? Get(string keyRef)
    {
        EnsureAvailable();
        var command = getCommand(keyRef);
        var result = Run(command.FileName, command.Args, null, allowFailure: true);
        return result.ExitCode == 0 ? result.Output.TrimEnd('\r', '\n') : null;
    }

    public void Delete(string keyRef)
    {
        EnsureAvailable();
        var command = deleteCommand(keyRef);
        Run(command.FileName, command.Args, null, allowFailure: true);
    }

    private void EnsureAvailable()
    {
        var status = GetStatus();
        if (!status.IsAvailable)
        {
            throw new InvalidOperationException(status.Error ?? $"{ProviderName} is not available.");
        }
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

    private static bool IsCommandAvailable(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, fileName + extension);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
