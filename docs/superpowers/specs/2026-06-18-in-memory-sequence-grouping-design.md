# In-Memory Sequence Grouping Design

## Goal

Add the first Phase 1 local grouping slice without changing the catalog schema. The feature groups adjacent filename sequences in memory and exposes the result through CLI JSON for later shoot review, compare, GUI, and agent work.

## Scope

This slice uses catalog photo metadata that already exists or can be derived locally: project id, base name, JPEG path, RAW path, JPEG EXIF capture time when available, and existing ordering. It does not compute perceptual hashes, create embeddings, call AI providers, or persist group rows.

## Behavior

The grouping service receives photos for one project and returns deterministic sequence groups. A sequence group is formed when at least two photos share the same filename prefix and have numeric suffixes within a configurable maximum gap. For example, `IMG_0001`, `IMG_0002`, and `IMG_0004` can form one group when the filename maximum gap is `2`. A larger gap starts a new group.

When capture times are available for adjacent candidates, the service also applies a capture-time maximum gap. Scanning reads JPEG EXIF `DateTimeOriginal` into `PhotoItem.CaptureTime`, and grouping uses that catalog value. Missing capture times do not split a group because older catalog rows or files without EXIF data may not have metadata.

Photos with no trailing number, unmatched prefixes, or singleton sequences are omitted from groups. Output order is deterministic: groups sort by prefix and first sequence number; items sort by sequence number and base name.

## Architecture

- `PhotoSelector.Core.Grouping` owns the pure in-memory grouping service and DTOs.
- `PhotoSelector.Core.Metadata` reads JPEG EXIF `DateTimeOriginal` without adding image-processing dependencies.
- Grouping is a staged filter pipeline:
  1. filename sequence candidate generation
  2. capture-time window filtering when metadata is present
  3. AI encoder/embedding visual similarity, reserved but disabled in this slice
- `PhotoSelector.Cli` adds `groups <directory> --json`, opens the existing catalog project, computes groups in memory, and serializes stable JSON with stage metadata.
- `ProjectDatabase` remains unchanged. Grouping results are disposable derived data until later phases need persisted review/audit references.

## JSON Shape

```json
{
  "project": { "id": 1, "sourceDirectory": "C:\\Photos\\Shoot" },
  "method": "filename-sequence",
  "maxFilenameGap": 2,
  "maxCaptureTimeGapSeconds": 10,
  "stages": [
    { "name": "filename-sequence", "status": "applied" },
    { "name": "capture-time-window", "status": "applied-when-present" },
    { "name": "ai-encoder", "status": "reserved" }
  ],
  "groups": [
    {
      "id": "filename-sequence:IMG_:0001-0004",
      "type": "sequence",
      "key": "IMG_",
      "reason": "filename sequence within gap 2; capture time gap <= 10s when available",
      "items": [
        { "photoId": 1, "baseName": "IMG_0001", "order": 0, "sequenceNumber": 1 },
        { "photoId": 2, "baseName": "IMG_0002", "order": 1, "sequenceNumber": 2 }
      ]
    }
  ]
}
```

## Error Handling

The CLI requires `--json`, matching existing catalog query commands. If the project is not indexed, it returns `Project not found: <directory>`. If there are no groups, it returns an empty `groups` array with exit code `0`.

## Testing

Core tests cover grouping by shared prefix, gap splitting, singleton omission, and non-number omission. CLI tests cover `groups <directory> --json` after `scan`, including stable method, project, group id, and item ordering.
