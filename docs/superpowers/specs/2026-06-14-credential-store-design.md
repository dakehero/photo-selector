# Credential Store Design

## Decision

Photo Selector should not depend on a single "universal" credential library as the product contract. The stable product contract is `ISecretStore`, with first-class implementations for:

- Windows Credential Manager
- macOS Keychain
- Linux Freedesktop Secret Service
- unsupported platforms
- in-memory test and sandbox usage

CLI, GUI, MCP, and future agent surfaces must use `PhotoSelector.Config.Secrets` instead of calling platform APIs or shell commands directly.

## Rationale

Cross-platform credential storage is not one uniform operating-system feature. Windows, macOS, and Linux desktop environments expose different capabilities, session assumptions, and failure modes. Linux is especially environment-dependent because Secret Service availability depends on a user session and a compatible service such as GNOME Keyring or KWallet integration.

The product should therefore expose one internal interface and explicit provider diagnostics, not pretend that every platform behaves the same.

## Current Contract

- `auth login` writes API keys through `ISecretStore` and stores only `api_key_ref` in config.
- `auth status --verbose` reports human-readable secret-store availability diagnostics.
- `auth status --json` reports machine-readable diagnostics without exposing the API key.
- `api_key_env` remains the right path for CLI, CI, and environments where a system secret service is unavailable.
- `MemorySecretStore` is a real provider for tests and future isolated agent sandboxes.

## Security Rules

- Never store raw API keys in config, SQLite, audit logs, tests, or command help output.
- Never emit resolved API keys in JSON output.
- Prefer native platform APIs when practical.
- If a provider shells out to a platform tool, the shelling-out must stay inside `PhotoSelector.Config.Secrets` and must return readable diagnostics when the tool is unavailable.

## Known Follow-Up

The macOS provider should be revisited before a macOS release. A simple `security` command integration is useful as an adapter shape, but robust non-interactive writes may need a native Keychain implementation to avoid putting secrets in process arguments.
