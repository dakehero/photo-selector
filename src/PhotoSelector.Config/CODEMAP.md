# PhotoSelector.Config Code Map

## Purpose

`PhotoSelector.Config` owns shared configuration paths, default catalog database path, config file persistence, and credential lookup. CLI, GUI, and future MCP/agent tools should share this project so logging in or configuring a provider from one surface works for the others.

## Main Areas

- Root config files: app config models, path selection, and config persistence.
- `Secrets`: API key resolution and secret-store providers.

## Important Files

- `AppConfig.cs`: provider profile and application config model.
- `ConfigPaths.cs`: shared config directory, config file path, and default SQLite catalog path.
- `ConfigStore.cs`: config load/save behavior.
- `Secrets/ApiKeyResolver.cs`: resolves `api_key_ref` and `api_key_env`.
- `Secrets/ISecretStore.cs`: stable credential provider contract, including availability diagnostics.
- `Secrets/SecretStoreFactory.cs`: selects platform or memory providers.
- `Secrets/WindowsCredentialSecretStore.cs`: Windows credential manager provider.
- `Secrets/MacOsKeychainSecretStore.cs`: macOS keychain provider.
- `Secrets/FreedesktopSecretServiceStore.cs`: Linux secret-service provider.
- `Secrets/MemorySecretStore.cs`: first-class in-memory provider for tests and agent sandboxes.
- `Secrets/UnsupportedSecretStore.cs`: explicit unsupported-platform behavior.

## Dependencies

This project should stay independent from UI, CLI command parsing, photo workflow, and AI provider calls. Consumers pass resolved config and secrets into other layers.

## Boundaries

- Do not store API keys in config files.
- Keep default catalog path selection here so CLI and GUI write to the same database.
- Do not shell out or P/Invoke from CLI code; credential access goes through `ISecretStore`.
- Keep platform-specific credential implementations in separate files.
- `SecretStoreFactory` should select providers, not contain platform implementation details.
- Every secret-store provider should return a stable `ProviderName` and a readable `GetStatus()` result for CLI/GUI diagnostics.
