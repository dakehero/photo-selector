# PhotoSelector.Tests Code Map

## Purpose

`PhotoSelector.Tests` is the regression suite for shared behavior across core workflow, config, AI provider logic, CLI orchestration, and provisional app view models.

## Main Test Areas

- Core scanning, JPG+RAW pairing, storage, ratings, audit logs, and export behavior.
- Config persistence, credential provider selection, and API key resolution behavior.
- AI rating parsing, prompt/request payloads, provider factory behavior, and compatible-provider HTTP behavior.
- CLI smoke tests and rating command orchestration.
- Catalog-first CLI scan/open/list behavior.
- Product CLI rating, status, reset, and removed-command behavior.
- Avalonia view-model behavior that can be tested without launching the full UI.

## Important Files

- `PhotoScannerTests.cs`: file classification and pairing behavior.
- `ProjectDatabaseTests.cs`: SQLite project/photo/rating/audit persistence.
- `ExportServiceTests.cs`: selected photo export behavior.
- `AiRatingParserTests.cs`: strict structured-rating parsing.
- `OpenAiCompatibleRatingClientTests.cs`: HTTP request/response and audit capture.
- `ProviderRatingClientFactoryTests.cs`: provider-name routing.
- `CliConfigTests.cs`: CLI config behavior.
- `CliRateTests.cs`: rating workflow orchestration and audit persistence.
- `CliSmokeTests.cs`: basic CLI command coverage.
- `SecretStoreFactoryTests.cs`: platform and memory secret-store selection.
- `MainWindowViewModelTests.cs`: app view-model behavior.

## Boundaries

- Add focused tests before changing rating contracts, provider behavior, secret handling, storage migrations, or export behavior.
- Tests must not store real API keys, unredacted requests, or base64 image data.
- Prefer deterministic fake clients and temp directories over network calls.
