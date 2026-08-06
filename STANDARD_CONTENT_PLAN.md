# Standard Content Library — Plan

Status: implemented (first item shipped — see "Shipped content" below).

## Goal

Let any fiddle (not just curated Examples) reference a small set of built-in assets
(fonts, textures, shaders, ...) without the user uploading them — e.g. a font to test
KernSmith dynamic resizing without requiring a custom `.ttf` upload.

## Constraints (settled)

- **Zip size stays small.** Today's export can be ~3kb zipped for a fiddle with no
  assets. Standard content must **not** be copied into every export by default —
  only when the fiddle actually uses it.
- **Detection = source-text scan**, same mechanism as `IExportableLibrary.IsUsedInSource`
  (a plain `.Contains("name")` check per plugin). Same known blind spot: a
  dynamically-constructed string won't be detected. Accepted risk, same as today.
- **Append-only, forever.** Once a standard content item ships, it can never be
  renamed or removed — an existing shared fiddle/URL may reference it by name, and
  breaking that is not acceptable. Only additions are allowed after ship.
- Because of the append-only rule, **naming is the one decision that must be right
  before the first item ships** — see Open Questions.

## Shape of the mechanism (proposed)

Mirrors the existing example-asset pipeline (see the `file-loading` skill), but not
gated behind `ExampleGallery.LoadAssets(exampleName)`:

1. Standard assets live as embedded resources (+ static `wwwroot` copies for share
   links), same convention as example assets.
2. At startup, their names are known but their bytes are **not** registered into
   `InMemoryContentManager` unconditionally — only lazily, when source-scan detection
   says the fiddle references one.
3. Export: same detection scan runs against `ProjectExporter`'s source input; only
   matched standard-content files get written into the export's `Content/` folder.

## Decisions

1. **Naming convention: `std/` prefix.** Confirmed. Works regardless of extension
   (or no extension, for a future `.xnb`-style binary) since the prefix applies to
   the key, not the file suffix — e.g. `std/DroidSans.ttf`, or `std/SomeBinaryBlob`
   with none. **Implementation guard:** the `std/` namespace must be reserved against
   *all* non-standard registration paths (drag-and-drop can't produce a `/` in a
   filename, but URL-fetch and gist-import names are derived from paths and could —
   reject/rename an incoming asset whose name starts with `std/` from any of those
   paths, so a crafted share URL can never shadow a standard asset).
2. **Initial content set: `DroidSans.ttf`.** Reuse the existing Apache-2.0 file
   already vendored for the `DynamicFonts` example.
3. **Asset-list visibility: falls out of the design for free.** Because standard
   content is only registered into `InMemoryContentManager` lazily, on detection (see
   "Shape of the mechanism" above), the existing asset-list UI already shows exactly
   the standard items a fiddle actually uses — unused ones were never registered, so
   they never appear. No dozen-item pollution, no separate UI logic, existing `.png`
   hover-preview behavior applies unchanged. Nothing extra to build here.
4. **Bytes are mutable, the name is the permanent part.** A standard-content name,
   once shipped, must always resolve — but the bytes behind it can be replaced (e.g.
   to fix a corrupt file) as long as the replacement is the same logical asset.

## Shipped content

Append-only — add a row per item, never remove or rename one (see decisions above).

| Name | Source | License |
|---|---|---|
| `std/DroidSans.ttf` | Google Fonts (Droid Sans) | Apache-2.0 |

## Implementation notes / known follow-ups (as of the first shipped item)

- No static `wwwroot` copy was added (item 1 above assumed one, "for share links"). Not
  needed in practice: standard content never round-trips through a fetchable URL — a
  shared fiddle simply re-runs the same source-scan detection on load, wherever it's
  opened. `wwwroot` copies only exist for example assets because those ARE fetched by
  URL (`{origin}/examples/{Name}/{File}`).
- Decision #3 ("asset-list visibility falls out for free") assumed the Assets panel
  reads `InMemoryContentManager.Files` directly. It doesn't — the panel is driven by a
  separately-maintained UI list (`Index.razor.cs`'s `_assets`). Fixed: the lazy
  standard-content detection hook in `DoCompileAndRun` now adds to `_assets` too.
- `InMemoryContentManager.AddFile`'s candidate-key expansion aliased a `/`-containing
  key (e.g. `std/DroidSans.ttf`) to its bare filename (`DroidSans.ttf`) as well, and
  export shipped every key in `Files` verbatim, so the asset shipped twice. Fixed:
  `Files` now returns a separate alias-free dict of only the exact registered names.
