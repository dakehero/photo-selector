# PhotoSelector.Ai Code Map

## Purpose

`PhotoSelector.Ai` owns the photography critique and rating provider layer. It turns a photo path and prompt contract into provider requests, parses model output into structured ratings, and preserves provider/audit metadata for traceability.

## Main Areas

- `Ratings`: rating contracts, default prompt, parser, provider clients, request payload construction, and provider factory.

## Important Files

- `Ratings/IPhotoRatingClient.cs`: common provider interface used by CLI and future agent surfaces.
- `Ratings/PhotoRatingRequest.cs`: normalized request passed to rating clients.
- `Ratings/AiRating.cs`: parsed model rating, criteria, and audit metadata.
- `Ratings/AiRatingParser.cs`: extracts strict rating JSON from model text.
- `Ratings/DefaultPhotoRatingPrompt.cs`: default photography-review prompt.
- `Ratings/RatingRequestPayload.cs`: OpenAI-compatible JSON payload and redacted audit payload builder.
- `Ratings/OpenAiSdkRatingClient.cs`: official OpenAI SDK implementation.
- `Ratings/OpenAiCompatibleRatingClient.cs`: HTTP client for OpenRouter and compatible backends.
- `Ratings/ProviderRatingClientFactory.cs`: provider-name to client selection.

## Dependencies

This project may depend on provider SDKs and HTTP abstractions. It should not depend on UI projects or CLI command parsing.

## Boundaries

- Keep provider-specific logic inside provider clients or factories.
- Preserve raw model output and raw provider responses when available, but never persist API keys or base64 image data.
- JSON property names should stay stable English even when user-visible comments follow `output_language`.
- Add parser and provider tests before changing rating contracts, prompt shape, or request payloads.

