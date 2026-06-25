# PhotoSelector.Tests Code Map

## Purpose

`PhotoSelector.Tests` is the regression suite for shared behavior across core workflow, config, AI provider logic, CLI orchestration, storage migrations, lifecycle state, grouping, and shared workflow behavior.

## Main Test Areas

- Core scanning, JPG+RAW pairing, metadata reading, grouping, storage, lifecycle state, ratings, audit logs, group reviews, and export behavior.
- Config persistence, credential provider selection, and API key resolution behavior.
- AI rating parsing, group review parsing, prompt/request payloads, provider factory behavior, and compatible-provider HTTP behavior.
- CLI smoke tests, catalog command behavior, shoot review draft behavior, grouping/review command behavior, and rating command orchestration.
- Catalog-first CLI scan/open/list behavior.
- Product CLI rating, status, reset, and removed-command behavior.

## Important Files

- `PhotoScannerTests.cs`: file classification and pairing behavior.
- `PhotoMetadataReaderTests.cs`: metadata extraction behavior.
- `FilenameSequenceGrouperTests.cs`: local grouping behavior.
- `PhotoGroupingEncoderTests.cs`: embedding adapter placeholder behavior.
- `ProjectDatabaseTests.cs`: SQLite project/photo lifecycle, rating, audit, group review, shoot review, and migration persistence.
- `ExportServiceTests.cs`: selected photo export behavior.
- `AiRatingParserTests.cs`: strict structured-rating parsing and parser/source-generation guardrails.
- `OpenAiCompatibleRatingClientTests.cs`: HTTP request/response and audit capture.
- `ProviderRatingClientFactoryTests.cs`: provider-name routing.
- `CliConfigTests.cs`: CLI config behavior.
- `CliRateTests.cs`: rating workflow orchestration and audit persistence.
- `CliSmokeTests.cs`: catalog, groups, shoot review, review group, help schema, and basic CLI command coverage.
- `SecretStoreFactoryTests.cs`: platform and memory secret-store selection.

## Boundaries

- Add focused tests before changing rating contracts, provider behavior, secret handling, storage migrations, or export behavior.
- Tests must not store real API keys, unredacted requests, or base64 image data.
- Prefer deterministic fake clients and temp directories over network calls.
- For schema changes, include a migration regression test that starts from an older table shape.
