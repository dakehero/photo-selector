# CLI Framework Selection

## Decision

Use `System.CommandLine` as the CLI framework for the production migration.

Migrate the full CLI surface because Photo Selector has not shipped publicly and has no compatibility burden. Keep the migration incremental in commits, but the target state is no hand-written top-level parser.

## Why

Photo Selector prioritizes fast short-lived CLI commands, NativeAOT compatibility, and agent-readable command discovery. The framework must not reintroduce reflection-heavy parsing or AOT warnings.

`System.CommandLine` is the best fit for the production CLI:

- It is the Microsoft-supported CLI parser.
- It published as `win-arm64` NativeAOT with no AOT or trim warnings in the spike.
- It did not show reflection warnings in the measured scenario.
- Its help and validation output are more mature than the other viable candidate.
- Its spike executable was larger at 3,172,352 bytes, but binary size is not a primary constraint for this project.
- Its `help-json`, `pick`, and `results --photo --audit` representative commands worked after NativeAOT publish.

`ConsoleAppFramework` remains a viable fallback:

- It published as `win-arm64` NativeAOT with no AOT or trim warnings in the spike.
- It produced the smallest spike executable: 1,890,816 bytes.
- It successfully parsed representative commands: `pick`, `results --photo --audit`, and a schema-style help command.
- Its upstream project explicitly targets source-generated, zero-reflection, AOT-safe CLI parsing.
- Its smaller binary size is attractive, but official support and built-in CLI behavior matter more here.

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
| System.CommandLine | 3,172,352 | 10.89 | 12.02 | 16.70 |
| ConsoleAppFramework | 1,890,816 | 10.66 | 13.76 | 24.63 |

The timing sample is too small to treat as a benchmark. It is enough to show that both viable candidates are in the same cold-start class for this spike. Because binary size is not a primary concern, System.CommandLine's official support and polished parsing/help behavior make it the production choice.

## Migration Plan

Migrate in small commits, but complete the full command surface:

1. Keep current hand-written CLI as the baseline.
2. Add `System.CommandLine` to `PhotoSelector.Cli`.
3. Introduce a command-tree builder that routes to the existing command handlers first.
4. Port command parsing one group at a time: `help`, `config/auth`, `results/status/reset/export`, `projects/open/photos`, `pick/rate/coach/scan`, and `arena`.
5. Preserve the custom `help --json` schema and System.Text.Json source-generation contexts.
6. Remove the hand-written top-level switch and ad hoc per-command argument parsing once equivalent tests pass.
7. Run `dotnet test`.
8. Publish `win-arm64` NativeAOT and require zero AOT/trim warnings.

Stop if System.CommandLine introduces AOT/trim warnings in the real production project or blocks the custom `help --json` schema.
