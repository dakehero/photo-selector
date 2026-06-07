using System.Diagnostics;

namespace PhotoSelector.Config.Secrets;

public abstract class CommandSecretStore : ISecretStore
{
    private readonly Func<string, (string FileName, string[] Args)> setCommand;
    private readonly Func<string, (string FileName, string[] Args)> getCommand;
    private readonly Func<string, (string FileName, string[] Args)> deleteCommand;

    protected CommandSecretStore(
        Func<string, (string FileName, string[] Args)> setCommand,
        Func<string, (string FileName, string[] Args)> getCommand,
        Func<string, (string FileName, string[] Args)> deleteCommand)
    {
        this.setCommand = setCommand;
        this.getCommand = getCommand;
        this.deleteCommand = deleteCommand;
    }

    public abstract string ProviderName { get; }

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
