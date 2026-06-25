# Photo Selector

Photo Selector is evolving into a local-first photography editor agent: a tool that helps photographers review whole shoots, compare similar frames, explain editorial decisions, and turn each shoot into feedback for the next one.

The current CLI MVP is the local engine for that direction. It focuses on the foundations that make a long-lived photography assistant possible:

- Scan photo directories and pair JPG/JPEG files with matching RAW files.
- Rate, pick, and critique photos with OpenAI-compatible vision models.
- Store results in a local SQLite catalog under the user's config directory.
- Keep API keys out of config files by using system credential stores or environment variables.
- Export selected JPG+RAW pairs without modifying the original source directory.
- Emit JSON output and audit logs for scripts, eval harnesses, and future agent/MCP integrations.

The long-term value is not one-off AI scoring. Photo Selector should learn from a user's shoots over time: what was kept, exported, edited, or rejected; which frames were near-duplicates; what failure patterns repeat; and how the user's photographic taste develops.

The desktop GUI is intentionally not part of the current MVP. It is planned as a future replaceable shell over the same core engine.

## Status

Early CLI MVP. The current product can cull and critique photos, but the design direction is moving toward shoot-level review, prompt/model evaluation, and personal photography feedback loops.

## Product Direction

Photo Selector should grow beyond single-image ratings into a photography review system:

- **Shoot review**: summarize a whole directory as one photographic session, not just independent photos.
- **Sequence comparison**: group visually similar or adjacent frames and explain which one is strongest.
- **Editorial memory**: retain AI ratings, manual marks, exports, and audit trails so future recommendations can learn from past choices.
- **Photography coaching**: identify repeated weaknesses in composition, timing, light, subject clarity, editing intent, and storytelling.
- **Agent chat workbench**: let the user ask for shoot overviews, contact sheets, sequence comparisons, winner explanations, exports, and learning notes in natural language while keeping the photos visible.
- **Prompt/model evaluation**: use stable JSON output and audit logs so external eval harnesses can compare prompts, models, and rubrics against fixed photo sets.
- **Local-first workflow**: keep the catalog, credentials, and user decisions on the user's machine; cloud models are providers, not the product core.

See [docs/ROADMAP.md](docs/ROADMAP.md) for the staged product roadmap.

## Requirements

- .NET 10 SDK
- A vision-capable OpenAI-compatible provider, such as OpenRouter or a local compatible server

Command examples use PowerShell/pwsh on Windows and Bash/Zsh on Linux or macOS.

## Build

Build and test from source:

Windows (PowerShell/pwsh):

```pwsh
dotnet build
dotnet test
```

Linux/macOS (Bash/Zsh):

```bash
dotnet build
dotnet test
```

Run the CLI from source:

Windows (PowerShell/pwsh):

```pwsh
dotnet run --project src\PhotoSelector.Cli -- help
```

Linux/macOS (Bash/Zsh):

```bash
dotnet run --project src/PhotoSelector.Cli -- help
```

Publish a self-contained NativeAOT CLI for your platform:

Windows (PowerShell/pwsh):

```pwsh
dotnet publish src\PhotoSelector.Cli\PhotoSelector.Cli.csproj -c Release -r <RID> --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts\photo-selector-cli-<RID>-aot
```

Linux/macOS (Bash/Zsh):

```bash
dotnet publish src/PhotoSelector.Cli/PhotoSelector.Cli.csproj -c Release -r <RID> --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts/photo-selector-cli-<RID>-aot
```

Common runtime identifiers include `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`.

Example for Windows ARM64:

```pwsh
dotnet publish src\PhotoSelector.Cli\PhotoSelector.Cli.csproj -c Release -r win-arm64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts\photo-selector-cli-win-arm64-aot-latest
```

Example for Linux x64:

```bash
dotnet publish src/PhotoSelector.Cli/PhotoSelector.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts/photo-selector-cli-linux-x64-aot-latest
```

## Release

GitHub Releases are built by `.github/workflows/release.yml`.

Create and push a semantic version tag:

All supported shells:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The workflow runs tests, publishes NativeAOT CLI packages for Windows, Linux, and macOS, then uploads them to the GitHub Release. Windows packages contain `photo-selector.exe`; Linux and macOS packages contain `photo-selector`.

## Quick Start

Configure a provider:

All supported shells:

```bash
photo-selector config set provider openrouter
photo-selector config set base_url https://openrouter.ai/api/v1
photo-selector config set model <vision-model>
```

Store an API key in the system credential store:

Windows (PowerShell/pwsh):

```pwsh
Get-Content key.txt | photo-selector auth login --profile default --api-key-stdin
photo-selector auth status --verbose
```

Linux/macOS (Bash/Zsh):

```bash
cat key.txt | photo-selector auth login --profile default --api-key-stdin
photo-selector auth status --verbose
```

Run photo culling:

Windows (PowerShell/pwsh):

```pwsh
photo-selector pick "C:\Photos\Shoot" --concurrency 2
```

Linux/macOS (Bash/Zsh):

```bash
photo-selector pick "$HOME/Photos/Shoot" --concurrency 2
```

The current product layer is moving into shoot review: `groups` derives similar-frame clusters, `review group` records or asks AI for group winners, and `review <directory>` builds a session-level draft that can be saved for history/evaluation. Commands such as `pick`, `results`, `mark`, `arena`, and audit logs remain the groundwork for this workflow.

Inspect results:

Windows (PowerShell/pwsh):

```pwsh
photo-selector results "C:\Photos\Shoot"
photo-selector results "C:\Photos\Shoot" --json
```

Linux/macOS (Bash/Zsh):

```bash
photo-selector results "$HOME/Photos/Shoot"
photo-selector results "$HOME/Photos/Shoot" --json
```

Export selected pairs:

Windows (PowerShell/pwsh):

```pwsh
photo-selector export keep "C:\Photos\Shoot" "C:\Photos\Exports"
```

Linux/macOS (Bash/Zsh):

```bash
photo-selector export keep "$HOME/Photos/Shoot" "$HOME/Photos/Exports"
```

## Main Commands

- `pick <directory>`: multi-photo culling workflow.
- `scan <directory>`: synchronous import and rating path for automation.
- `rate <image>`: rate one photo.
- `coach <image>`: critique one photo.
- `arena <directory> --models <model-a,model-b>`: compare models on the same photo set.
- `results [directory]`: summarize ratings.
- `results [directory] --photo <photo-id|base-name> --audit --json`: inspect one decision trace.
- `groups <directory> --json`: compute in-memory sequence groups with filename and JPEG EXIF capture-time stages, with an AI encoder stage reserved for future visual similarity.
- `review <directory> [--save] --json`: build a local shoot review draft from catalog ratings, groups, and saved group review snapshots, optionally saving it for history/evaluation.
- `review group <directory> <group-id> [--winner <photo-id|base-name> --reason <text>] [--json]`: save a group review snapshot or ask the configured AI provider to select a group winner.
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
