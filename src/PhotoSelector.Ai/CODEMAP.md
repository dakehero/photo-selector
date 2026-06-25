# PhotoSelector.Ai Code Map

## Purpose

`PhotoSelector.Ai` owns provider-facing AI behavior. It turns photo or group-review requests into provider payloads, parses model output into stable product contracts, and preserves provider/audit metadata for traceability.

## Main Areas

- `Ratings`: single-photo rating contracts, default prompts, parsers, provider clients, request payload construction, and provider factory.
- `Reviews`: group-level review contracts, prompt construction, multi-image request payloads, response parsing, provider clients, and provider factory.

## Important Files

- `Ratings/IPhotoRatingClient.cs`: common provider interface used by CLI and future agent surfaces.
- `Ratings/PhotoRatingRequest.cs`: normalized request passed to rating clients.
- `Ratings/AiRating.cs`: parsed model rating, criteria, and audit metadata.
- `Ratings/AiRatingParser.cs`: validates strict rating JSON and maps it into domain ratings.
- `Ratings/BuiltInRatingPrompts.cs`: built-in default rating prompt.
- `Ratings/RatingJsonContext.cs`: source-generated JSON contracts for rating and chat-completion payloads.
- `Ratings/RatingRequestPayload.cs`: OpenAI-compatible single-image payload and redacted audit payload builder.
- `Ratings/OpenAiSdkRatingClient.cs`: official OpenAI SDK implementation.
- `Ratings/OpenAiCompatibleRatingClient.cs`: HTTP client for OpenRouter and compatible backends.
- `Ratings/ProviderRatingClientFactory.cs`: provider-name to client selection.
- `Reviews/GroupReview.cs`: group-review request, result, item-decision, and client contracts.
- `Reviews/GroupReviewPrompt.cs`: built-in group comparison prompt.
- `Reviews/GroupReviewRequestPayload.cs`: OpenAI-compatible multi-image payload and redacted audit payload builder.
- `Reviews/GroupReviewParser.cs`: validates group review JSON and maps it into domain decisions.
- `Reviews/OpenAiCompatibleGroupReviewClient.cs`: HTTP client for OpenRouter and compatible group review backends.
- `Reviews/ProviderGroupReviewClientFactory.cs`: provider-name to group-review client selection.

## Dependencies

This project may depend on provider SDKs and HTTP abstractions. It should not depend on UI projects or CLI command parsing.

## Boundaries

- Keep provider-specific logic inside provider clients or factories.
- Preserve raw model output and raw provider responses when available, but never persist API keys or base64 image data.
- JSON property names should stay stable English even when user-visible comments follow `output_language`.
- Use source-generated `System.Text.Json` DTOs for provider JSON contracts; keep hand-written parser code focused on validation and domain mapping.
- Add parser and provider tests before changing rating/review contracts, prompt shape, or request payloads.
