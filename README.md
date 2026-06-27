# Photo Selector

Photo Selector is a local-first CLI for photo culling, critique, and shoot review. It scans a shoot directory, pairs JPG/JPEG files with matching RAW files, asks an OpenAI-compatible vision provider for ratings and critique, and stores results, manual marks, exports, and audit logs in a local SQLite catalog.

The current repository focuses on the CLI and shared engine. A GUI is not implemented yet; future GUI, agent, MCP, or other surfaces should reuse the same core projects instead of duplicating business logic.

## Current Capabilities

- Scan photo directories and pair JPG/JPEG files with matching RAW files.
- Rate one photo, critique one photo, or cull a whole shoot.
- Export JPG+RAW pairs by `keep`, `maybe`, or `reject` without modifying the source directory.
- Build sequence groups, group reviews, shoot reviews, and a local HTML visual workbench.
- Store projects, ratings, manual marks, reviews, and audit logs in a local SQLite catalog.
- Keep API keys out of config files by using system credential stores or environment variables.
- Emit JSON output for scripts, evaluation harnesses, and future agent integrations.

See [docs/ROADMAP.md](docs/ROADMAP.md) for the product roadmap.

## Repository Layout

- `src/PhotoSelector.Core`: file classification, directory scanning, project records, SQLite storage, and export behavior.
- `src/PhotoSelector.Ai`: rating and review prompts, OpenAI-compatible provider clients, and response parsing.
- `src/PhotoSelector.Config`: shared configuration, config paths, and secret store abstractions.
- `src/PhotoSelector.Agent`: shared workflows and worker orchestration above core/config/AI.
- `src/PhotoSelector.Cli`: command-line entry point.
- `tests/PhotoSelector.Tests`: regression tests for core, AI, config, CLI, and storage behavior.

## Requirements

- .NET 10 SDK
- Git
- A vision-capable OpenAI-compatible provider, such as OpenRouter, an OpenAI-compatible endpoint, LM Studio, or Ollama-compatible local service

Check that the SDK is installed:

```pwsh
dotnet --info
```

If `dotnet` is missing, install the .NET 10 SDK. On Windows, you can use winget:

```pwsh
winget install --id Microsoft.DotNet.SDK.10 -e
```

## Run From Source

From the repository root:

```pwsh
dotnet restore
dotnet build
dotnet run --project src\PhotoSelector.Cli -- help
```

Everything after `dotnet run --project ... --` is passed to the Photo Selector CLI. For example:

```pwsh
dotnet run --project src\PhotoSelector.Cli -- help --json
dotnet run --project src\PhotoSelector.Cli -- config list
```

Linux/macOS path style:

```bash
dotnet restore
dotnet build
dotnet run --project src/PhotoSelector.Cli -- help
```

## Build

Development build:

```pwsh
dotnet build
```

Release build:

```pwsh
dotnet build --configuration Release
```

Build only the CLI project:

```pwsh
dotnet build src\PhotoSelector.Cli\PhotoSelector.Cli.csproj --configuration Release
```

The normal build output is usually under:

```text
src\PhotoSelector.Cli\bin\Release\net10.0\
```

Note: `dotnet build` creates a normal framework-dependent build that expects a compatible .NET runtime on the machine. To create a self-contained executable that is easier to distribute, use `dotnet publish`.

## Test

Run all tests:

```pwsh
dotnet test
```

Run tests with the Release configuration, matching CI more closely:

```pwsh
dotnet test --configuration Release
```

## Publish Locally

Publish a self-contained NativeAOT CLI for Windows ARM64:

```pwsh
dotnet publish src\PhotoSelector.Cli\PhotoSelector.Cli.csproj `
  --configuration Release `
  --runtime win-arm64 `
  --self-contained true `
  -p:PublishAot=true `
  -p:StripSymbols=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o artifacts\photo-selector-win-arm64
```

After publishing, the executable is here:

```text
artifacts\photo-selector-win-arm64\PhotoSelector.Cli.exe
```

Run it directly:

```pwsh
.\artifacts\photo-selector-win-arm64\PhotoSelector.Cli.exe help
```

Rename it to the final CLI name:

```pwsh
Rename-Item `
  -LiteralPath artifacts\photo-selector-win-arm64\PhotoSelector.Cli.exe `
  -NewName photo-selector.exe

.\artifacts\photo-selector-win-arm64\photo-selector.exe help
```

Common runtime identifiers:

| Platform | RID |
| --- | --- |
| Windows x64 | `win-x64` |
| Windows ARM64 | `win-arm64` |
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS Intel | `osx-x64` |
| macOS Apple Silicon | `osx-arm64` |

Change `--runtime win-arm64` to the target RID when publishing for another platform. Cross-platform NativeAOT publishing can require target-specific tooling, so the most reliable option is to publish on the target OS or use the GitHub Actions release workflow.

## GitHub Release Publishing

The repository includes [.github/workflows/release.yml](.github/workflows/release.yml). When a tag matching `v*.*.*` is pushed, GitHub Actions will:

1. Run `dotnet test --configuration Release`.
2. Publish NativeAOT CLI builds for Windows, Linux, and macOS on x64 and ARM64.
3. Rename `PhotoSelector.Cli` to `photo-selector`, or `photo-selector.exe` on Windows.
4. Package the release assets as zip or tar.gz files.
5. Create or update the GitHub Release and upload the packages.

Create a release:

```pwsh
git tag v0.1.0
git push origin v0.1.0
```

To rebuild a release for an existing tag, open GitHub Actions, run the `Release` workflow manually, and provide the tag:

```text
v0.1.0
```

## Install A Local Publish

Assuming you published to `artifacts\photo-selector-win-arm64`:

```pwsh
New-Item -ItemType Directory -Force "$HOME\bin" | Out-Null
Copy-Item artifacts\photo-selector-win-arm64\photo-selector.exe "$HOME\bin\photo-selector.exe" -Force
```

If `$HOME\bin` is not on PATH yet, you can test it temporarily:

```pwsh
$env:PATH = "$HOME\bin;$env:PATH"
photo-selector help
```

For permanent use, add `%USERPROFILE%\bin` to your user PATH in Windows environment variable settings.

## Quick Start

Configure a provider:

```pwsh
photo-selector config set provider openrouter
photo-selector config set base_url https://openrouter.ai/api/v1
photo-selector config set model <vision-model>
```

Store an API key in the system credential store:

```pwsh
Get-Content key.txt | photo-selector auth login --profile default --api-key-stdin
photo-selector auth status --verbose
```

Cull a shoot:

```pwsh
photo-selector pick "C:\Photos\Shoot" --concurrency 2
```

Inspect results:

```pwsh
photo-selector results "C:\Photos\Shoot"
photo-selector results "C:\Photos\Shoot" --json
```

Export kept photos:

```pwsh
photo-selector export keep "C:\Photos\Shoot" "C:\Photos\Selected"
```

Generate a local HTML workbench:

```pwsh
photo-selector workbench "C:\Photos\Shoot" "C:\Photos\Workbench" --json
```

## Common Commands

- `photo-selector help`: show command help.
- `photo-selector help --json`: emit machine-readable command help.
- `photo-selector config set <key> <value>`: set shared configuration.
- `photo-selector config list`: print the active configuration.
- `photo-selector auth login --profile default --api-key-stdin`: store an API key.
- `photo-selector auth status --verbose`: check credential availability.
- `photo-selector pick <directory>`: cull a directory.
- `photo-selector scan <directory>`: synchronously import and rate a directory for automation.
- `photo-selector rate <image>`: rate one photo.
- `photo-selector coach <image>`: critique one photo.
- `photo-selector groups <directory> --json`: compute adjacent or same-sequence photo groups.
- `photo-selector review <directory> [--ai] [--save] [--json]`: build a shoot review.
- `photo-selector review group <directory> <group-id> [--winner <photo-id|base-name> --reason <text>] [--json]`: save or generate a group review.
- `photo-selector results [directory]`: summarize rating results.
- `photo-selector mark <directory> <photo-id|base-name> --decision <decision>`: save a manual decision.
- `photo-selector export <keep|maybe|reject> <directory> <target>`: export matching JPG+RAW pairs.
- `photo-selector projects list --json`: list indexed projects.
- `photo-selector open <project-id|directory> --json`: open one project context.
- `photo-selector photos list --project <project-id> --json`: list photos for one project.

## Configuration And Data

The default catalog and configuration live under a `.photo-selector` directory in the user's home directory. Normal user-facing commands do not require SQLite database paths; the database path is an implementation detail.

API keys are not stored in config files, SQLite, tests, or audit logs. Prefer:

- System credential store: `photo-selector auth login --profile default --api-key-stdin`
- Environment variables for CI or temporary scripts

RAW files are not uploaded to providers. Rating requests use generated JPG previews. Audit logs store redacted request metadata and raw model responses for traceability, debugging, and evaluation.

## Troubleshooting

### What is the shortest way to check that the project builds?

```pwsh
dotnet restore
dotnet build
dotnet test
```

### How do I create a Windows ARM64 executable?

```pwsh
dotnet publish src\PhotoSelector.Cli\PhotoSelector.Cli.csproj `
  -c Release `
  -r win-arm64 `
  --self-contained true `
  -p:PublishAot=true `
  -p:StripSymbols=true `
  -o artifacts\photo-selector-win-arm64

.\artifacts\photo-selector-win-arm64\PhotoSelector.Cli.exe help
```

### What if `dotnet publish` fails?

First check:

```pwsh
dotnet --info
dotnet restore
dotnet test --configuration Release
```

If normal tests pass but NativeAOT publish fails, the issue is often the target RID or local native toolchain. Start with the RID that matches the current machine: `win-arm64` for Windows ARM64, `win-x64` for Windows x64.

### Where are release packages created?

For local publishing, the output directory is controlled by `-o`, for example:

```text
artifacts\photo-selector-win-arm64\
```

The GitHub Actions release workflow uploads final zip/tar.gz packages to the GitHub Release page.

## License

MIT
