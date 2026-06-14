# Photo Selector

Photo Selector is a local-first CLI engine for AI-assisted photo culling and critique.

The current MVP focuses on the shared core and command-line workflow:

- Scan photo directories and pair JPG/JPEG files with matching RAW files.
- Rate, pick, and critique photos with OpenAI-compatible vision models.
- Store results in a local SQLite catalog under the user's config directory.
- Keep API keys out of config files by using system credential stores or environment variables.
- Export selected JPG+RAW pairs without modifying the original source directory.
- Emit JSON output for scripts and future agent/MCP integrations.

The desktop GUI is intentionally not part of the current MVP. It is planned as a future replaceable shell over the same core engine.

## Status

Early CLI MVP. Expect prompt, model, and workflow tuning to continue.

## Requirements

- .NET 10 SDK
- A vision-capable OpenAI-compatible provider, such as OpenRouter or a local compatible server

## Build

Build and test from source:

```powershell
dotnet build
dotnet test
```

Run the CLI from source:

```powershell
dotnet run --project src\PhotoSelector.Cli -- help
```

Publish a self-contained NativeAOT CLI for your platform:

```powershell
dotnet publish src\PhotoSelector.Cli\PhotoSelector.Cli.csproj -c Release -r <RID> --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts\photo-selector-cli-<RID>-aot
```

Common runtime identifiers include `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`.

Example for Windows ARM64:

```powershell
dotnet publish src\PhotoSelector.Cli\PhotoSelector.Cli.csproj -c Release -r win-arm64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts\photo-selector-cli-win-arm64-aot-latest
```

## Release

GitHub Releases are built by `.github/workflows/release.yml`.

Create and push a semantic version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The workflow runs tests, publishes NativeAOT CLI packages for Windows, Linux, and macOS, then uploads them to the GitHub Release. Windows packages contain `photo-selector.exe`; Linux and macOS packages contain `photo-selector`.

## Quick Start

Configure a provider:

```powershell
photo-selector config set provider openrouter
photo-selector config set base_url https://openrouter.ai/api/v1
photo-selector config set model <vision-model>
```

Store an API key in the system credential store:

```powershell
Get-Content key.txt | photo-selector auth login --profile default --api-key-stdin
photo-selector auth status --verbose
```

Run photo culling:

```powershell
photo-selector pick "C:\Photos\Shoot" --concurrency 2
```

Inspect results:

```powershell
photo-selector results "C:\Photos\Shoot"
photo-selector results "C:\Photos\Shoot" --json
```

Export selected pairs:

```powershell
photo-selector export keep "C:\Photos\Shoot" "C:\Photos\Exports"
```

## Main Commands

- `pick <directory>`: multi-photo culling workflow.
- `scan <directory>`: synchronous import and rating path for automation.
- `rate <image>`: rate one photo.
- `coach <image>`: critique one photo.
- `arena <directory> --models <model-a,model-b>`: compare models on the same photo set.
- `results [directory]`: summarize ratings.
- `results [directory] --photo <photo-id|base-name> --audit --json`: inspect one decision trace.
- `mark <directory> <photo-id|base-name>`: save manual decisions and notes.
- `export <keep|maybe|reject> <directory> <target>`: copy selected JPG+RAW pairs.
- `help --json`: expose machine-readable CLI help.

## Secrets And Privacy

- API keys are not stored in config files, SQLite, tests, or audit logs.
- `api_key_ref` uses the platform credential store.
- `api_key_env` is available for CLI and CI environments.
- RAW files are not uploaded for scoring; the provider receives a generated JPG preview.
- Audit logs store redacted request metadata and raw model responses for traceability.

## License

MIT
