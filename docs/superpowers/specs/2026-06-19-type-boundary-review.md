# Type Boundary Review

## Goal

Review the new grouping and metadata work for places where Photo Selector should prefer library-provided types or parsers over hand-written models. The immediate outcome is a clear boundary decision, not a production dependency change.

## Principle

Keep small Photo Selector domain contracts when they describe product concepts. Prefer mature libraries for standard file formats, binary parsing, metadata standards, perceptual hashing, image embeddings, and model-runtime-specific tensors.

## Keep As Domain Types

These types are product-facing or boundary-facing and should remain owned by Photo Selector:

- `PhotoGroup`: a derived product object used by CLI, future GUI, and future agent tools.
- `PhotoGroupItem`: stable group-local ordering and photo reference.
- `SequenceGroupingOptions`: product tuning knobs for grouping behavior.
- `GroupingPipelineStage`: CLI/API explanation of which grouping filters ran.
- CLI JSON records in `Program.cs`: private serialization contracts for stable command output.

These should not expose third-party library types because that would leak implementation details into CLI and future UI/agent contracts.

## Replace Or Wrap Library Functionality

`PhotoMetadataReader` currently hand-parses JPEG APP1 Exif/TIFF data. That is useful as a no-dependency first pass, but it is standard-format parsing and should not grow into a custom metadata library.

Recommended next step:

- Introduce an internal metadata adapter interface, for example `IPhotoMetadataReader`.
- Use a mature metadata library behind that adapter after explicit dependency approval.
- Keep the external domain result as `DateTimeOffset? CaptureTime` so the rest of Core remains independent from library-specific directory/tag objects.

Candidate libraries to evaluate:

- `MetadataExtractor`: focused metadata reader for Exif, IPTC, XMP, ICC, and multiple image/media formats.
- `SixLabors.ImageSharp`: full managed image library with metadata support, but broader than metadata extraction alone.
- `DateTakenExtractor`: small date-taken focused package, but narrower and less general than the roadmap likely needs.

Preliminary recommendation: evaluate `MetadataExtractor` first because this project needs metadata breadth more than pixel editing in Core.

## Encoder And Embedding Types

`IPhotoGroupingEncoder` and `PhotoEmbedding` are acceptable as temporary domain contracts, but they should not become a home-grown tensor/model API.

When an actual encoder backend is selected:

- Keep `IPhotoGroupingEncoder` as the provider-neutral boundary.
- Let the implementation use backend-native tensor types internally.
- Convert to a small neutral vector result at the boundary only if the grouping service needs provider-independent cosine similarity.
- Do not add ONNX, CLIP, MobileCLIP, SigLIP, or DINOv2 runtime dependencies until the grouping/eval workflow justifies them.

## Future Review Checklist

Before adding or expanding a hand-written type, ask:

1. Is this a Photo Selector domain concept, or a standard from another ecosystem?
2. Would exposing a third-party type leak dependency details across Core/CLI/UI boundaries?
3. Does a mature library already parse or model this format?
4. Is the dependency NativeAOT-friendly enough for CLI publishing?
5. Can the dependency stay behind an adapter so we can swap it later?

## Decision

Do not replace types in this commit. The next implementation commit should focus on replacing the internals of `PhotoMetadataReader` with an approved metadata library adapter, while preserving the public Core/CLI contracts added for grouping.
