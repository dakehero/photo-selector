# CLI Framework Selection

## Decision

Use `ConsoleAppFramework` as the preferred CLI framework candidate for the next CLI migration spike.

Do not migrate production CLI code yet. The next implementation step should be a small branch that ports one or two commands, keeps `help --json`, and verifies NativeAOT before adding the dependency to the main projects.

## Why

Photo Selector prioritizes fast short-lived CLI commands, NativeAOT compatibility, and agent-readable command discovery. The framework must not reintroduce reflection-heavy parsing or AOT warnings.

`ConsoleAppFramework` best matches those constraints:

- It published as `win-arm64` NativeAOT with no AOT or trim warnings in the spike.
- It produced the smallest spike executable: 1,890,816 bytes.
- It successfully parsed representative commands: `pick`, `results --photo --audit`, and a schema-style help command.
- Its upstream project explicitly targets source-generated, zero-reflection, AOT-safe CLI parsing.

`System.CommandLine` remains the fallback candidate:

- It published as `win-arm64` NativeAOT with no AOT or trim warnings in the spike.
- Its help and validation output are polished.
- Its spike executable was larger at 3,172,352 bytes.
- It is a safer ecosystem choice if ConsoleAppFramework becomes awkward during a real migration.

`Spectre.Console.Cli` is not suitable for this project’s CLI parser:

- NativeAOT publish emitted IL3050/IL3053 warnings.
- The warning stated that `Spectre.Console.Cli` relies on reflection and is not supported for trimming/AOT.
- The AOT executable failed at runtime while resolving command settings.

The project can continue using `Spectre.Console` for progress display, but should not use `Spectre.Console.Cli` for command parsing.

## Spike Environment

- .NET SDK: 10.0.301
- Runtime identifier: `win-arm64`
- Candidate package versions:
  - `ConsoleAppFramework` 5.7.13
  - `System.CommandLine` 2.0.9
  - `Spectre.Console.Cli` 0.55.0

## Commands Run

```powershell
dotnet publish artifacts\cli-framework-spike\Spike.ConsoleAppFramework\Spike.ConsoleAppFramework.csproj -c Release -r win-arm64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
dotnet publish artifacts\cli-framework-spike\Spike.SystemCommandLine\Spike.SystemCommandLine.csproj -c Release -r win-arm64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
dotnet publish artifacts\cli-framework-spike\Spike.SpectreCli\Spike.SpectreCli.csproj -c Release -r win-arm64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
```

Representative runtime checks:

```powershell
artifacts\cli-framework-spike\out\consoleappframework\Spike.ConsoleAppFramework.exe help-json
artifacts\cli-framework-spike\out\consoleappframework\Spike.ConsoleAppFramework.exe pick C:\Photos --json --concurrency 2
artifacts\cli-framework-spike\out\consoleappframework\Spike.ConsoleAppFramework.exe results C:\Photos --photo DSC_0001 --audit --json

artifacts\cli-framework-spike\out\system-commandline\Spike.SystemCommandLine.exe help-json
artifacts\cli-framework-spike\out\system-commandline\Spike.SystemCommandLine.exe pick C:\Photos --json --concurrency 2
artifacts\cli-framework-spike\out\system-commandline\Spike.SystemCommandLine.exe results C:\Photos --photo DSC_0001 --audit --json
```

## Startup And Size Notes

Ten `help-json` runs through `Measure-Command`:

| Candidate | Native exe size | Min ms | Avg ms | Max ms |
| --- | ---: | ---: | ---: | ---: |
| ConsoleAppFramework | 1,890,816 | 10.66 | 13.76 | 24.63 |
| System.CommandLine | 3,172,352 | 10.89 | 12.02 | 16.70 |

The timing sample is too small to treat as a benchmark. It is enough to show that both viable candidates are in the same cold-start class for this spike. Size, AOT posture, and source-generation design make ConsoleAppFramework the better first migration candidate.

## Next Migration Slice

Port only one small command group first:

1. Keep current hand-written CLI as the baseline.
2. Add ConsoleAppFramework to `PhotoSelector.Cli`.
3. Port `help` and `results` command parsing only.
4. Preserve the existing custom `help --json` schema and System.Text.Json source-generation contexts.
5. Run `dotnet test`.
6. Publish `win-arm64` NativeAOT and require zero AOT/trim warnings.

If this slice increases complexity, breaks custom help schema, or introduces AOT warnings, stop and evaluate `System.CommandLine` before continuing.
