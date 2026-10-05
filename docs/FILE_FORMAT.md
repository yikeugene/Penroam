# Penroam notebook formats

**Penroam 1.1.0** writes SQLite schema 4 and `.moye` format 4 to preserve PDF placement and paper guides on extended pages. Released Penroam 1.0.0 and Moye 2.0.0 use schema 3 and backup format 3; published Moye 1.7.0–1.12.0 uses schema 2 and backup format 2. Application versions, SQLite schema versions, preference versions and backup format versions are independent.

Penroam is the new product name for Moye. The released 1.0.0 branding change retained schema 3 and backup format 3. Page extension in Penroam 1.1.0 introduces the later schema/format 4 change. The `.moye` extension and manifest identifier `"moye"` remain unchanged. Existing Moye files can still be opened or imported. The default library remains `%LOCALAPPDATA%\Moye\moye.db`; preferences and recovery files also stay in their existing locations.

## Compatibility

| File | Penroam 1.1.0 writer | Penroam 1.1.0 reader | Released Penroam 1.0.0 / Moye 2.0.0 |
|---|---|---|---|
| SQLite library | Schema 4 | Migrates schema 0/1/2/3; reads 4 | Writes schema 3; rejects 4 |
| `.moye` backup | Format 4 | Reads 1, 2, 3 and 4 | Writes format 3; reads 1/2/3; rejects 4 |
| Writing preferences | Format 1, unchanged | Existing behavior | Format 1 |
| Workspace preferences | Format 1 | Format 1; keeps unreadable/newer files | Format 1 |
| Recovery drafts | Format 1 | Format 1; keeps unreadable/newer files | Format 1; requires its supported library schema |

Opening an older library migrates its schema in one transaction. Libraries without sections receive a **General** section for each notebook; section IDs are derived deterministically from the notebook ID. Existing page IDs, order, content and ISF ink are retained. Schema 3 added notebook pins, cover color and a quick-note destination marker; missing page titles/bookmarks read with empty/false defaults. Schema 4 protects PDF placement and paper layout fields from older writers and leaves existing page JSON and ink unchanged during the schema 3 upgrade.

Back up with the older app first if you need a copy usable by that app. Released Penroam 1.0.0 and Moye 2.0.0 cannot open schema 4 / backup format 4. Moye 1.12.0 and earlier also reject schema 3 / format 3; 1.6.2 and earlier reject schema 2 / format 2. Uninstalling does not roll back a migration. Format 1 backups restore into General; all restores create new IDs and do not overwrite existing notebooks.

## Notebook structure

JSON uses UTF-8 and camel-case property names. The model in [DocumentModels.cs](../src/Moye/Models/DocumentModels.cs) defines the fields and defaults. A simplified example (ink and assets omitted) is:

```json
{
  "id": "course-id",
  "title": "Mathematics",
  "folder": "Semester 1",
  "isPinned": true,
  "coverColor": "#D9E7DC",
  "isQuickInbox": false,
  "sections": [
    { "id": "algebra-id", "title": "Linear Algebra" },
    { "id": "calculus-id", "title": "Calculus" }
  ],
  "pages": [
    { "id": "page-1", "sectionId": "algebra-id", "title": "Eigenvalues", "isBookmarked": true, "width": 793.700787, "height": 1122.519685 },
    { "id": "page-2", "sectionId": "calculus-id", "title": "", "isBookmarked": false, "width": 793.700787, "height": 1122.519685 }
  ]
}
```

`sections` defines section order. Each page belongs to exactly one section through `sectionId`. `pages` is a flat ordered list, grouped by section order and preserving page order within each section. PDF export filters this sequence by the chosen page IDs, preserving displayed order. Empty sections are allowed. Titles need not be unique; IDs are unique within a notebook. Sections do not contain nested sections. Notebook `coverColor` is empty for the automatic choice or a supported hex color. `isQuickInbox` identifies the notebook used by quick capture; restored copies clear this flag so they do not silently become the destination.

Pages also contain `template`, `texts`, `images`, and an optional `pdf` background reference. Paper templates use stable integer values: Blank 0, Ruled 1, Grid 2, Dot Grid 3, Cornell 4 and Graph 5. Geometry uses fixed 96-DPI page coordinates; display zoom never changes stored coordinates. Text includes font family, size in DIP, bold, italic, alignment and color. Images reference original assets; PDF references retain the original PDF asset and page/crop/rotation metadata. Ink is vector ISF with pressure information, not a flattened bitmap.

An extended page stores the full writing canvas in page `width` and `height`. Its PDF background additionally stores `offsetX`, `offsetY`, `displayWidth` and `displayHeight` in DIP. These fields describe the original PDF's position and size on the canvas, independently of source crop and rotation. All four fields default to zero for legacy PDFs, meaning that the PDF fills the existing page. The first extension materializes its original displayed dimensions. Added space on the left or top shifts PDF, ink, text and images together without scaling them. Later extensions preserve that original PDF size. Placement must be finite, have nonnegative offsets and either two positive display dimensions within the canvas or the all-zero legacy form. New page extensions are limited to 19,200 DIP (5,080 mm) per dimension; existing backup dimension limits remain unchanged.

For paper pages, optional `paperLayout` stores `x`, `y`, `width` and `height` for the original paper-guide region. A missing or null value means that guides fill the whole page, preserving legacy behavior. Extending a paper page fixes that region at its previous size and moves it with the existing notes when space is added on the left or top; the added margins are blank. This retains ruled/grid alignment and Cornell divisions. All four values must be finite, offsets nonnegative, dimensions positive, and the entire region inside the canvas. Page snapshots, SQLite metadata, recovery drafts and editable backups preserve this layout.

## SQLite schema 4

Moye 1.10.0 also accepts supported PDF annotations with internal destinations or interactive actions. Stored original PDF bytes are unchanged. Export normalizes only its in-memory source document, disabling those actions before copying pages while retaining annotation appearances and ordinary URI links. This requires no database or backup format change.

Moye 1.9.0 also imports Office documents by converting them locally to PDF. These pages use the existing PDF asset/reference fields, so no format migration is required. Backups contain the converted PDF and editable Penroam annotations; the original DOCX, PPTX, PPSX, ODT or ODP file remains outside the notebook and is not modified.

The library uses `PRAGMA user_version=4`, foreign keys and WAL transactions. `notebooks` stores notebook identity, title, category and timestamps, plus `is_pinned`, `cover_color` and `is_quick_inbox`. `sections` stores `(notebook_id, id, ordinal, title)` with a composite primary key and a cascading notebook foreign key. `pages` stores notebook/page identity, global ordinal, JSON metadata, an ISF BLOB and a content hash. Metadata includes `sectionId`, `title`, `isBookmarked`, `paperLayout` and PDF placement when present; section membership and background geometry are validated by the application. `assets` stores original attachment bytes keyed by a SHA-256 content hash.

Each save transaction commits the notebook, section order and page content/order together. Cross-notebook move/copy and selected multi-notebook restoration use a batch transaction. Search can read metadata without loading ink; extracted PDF text is a bounded in-memory search projection and is never saved into `texts`. The implementation is [SqliteNotebookRepository.cs](../src/Moye/Services/SqliteNotebookRepository.cs). Use the app's backup command for a consistent portable copy, rather than copying an active database without its journal.

## `.moye` format 4

A `.moye` file is an unencrypted ZIP with:

- `manifest.json`: `format: "moye"`, `version: 4`, creation time, notebook descriptors and asset descriptors.
- Notebook JSON at paths listed in `manifest.notebooks[].path`, with a SHA-256 checksum for the exact bytes. Each notebook includes the section and page lists. Its page `inkData` values are empty because ink is stored separately.
- One ISF entry per page, indexed by `pageId`, `path` and `sha256` in the notebook descriptor's `inks` list. An empty page can have a zero-length ink entry.
- `assets/<sha256>.bin`: original image/PDF bytes, with filename and content type in the manifest.

Readers should follow manifest paths rather than assume notebook filenames. Import validates paths, document structure, ISF payloads, page/section references and asset checksums before storing assets. It does not decode every image or PDF during backup restoration. Formats 2–4 require an explicit, nonempty section list, unique section IDs, valid memberships and pages grouped in section order; malformed structures are rejected. Formats 3–4 additionally require explicit notebook `isPinned`, `coverColor` and `isQuickInbox`, plus page `title` and `isBookmarked` fields. Format 4 also requires all four placement fields on every PDF background and an explicit `paperLayout` field on every page (null for an ordinary unextended page). Any nonnull paper layout must contain all four geometry fields. Formats 1–3 default missing PDF placement to the legacy all-zero layout and missing paper layout to null, while formats 1/2 retain the earlier organization defaults when fields are absent. Restore remaps notebook, section, page, text and image IDs, preserves asset content hashes, clears `isQuickInbox`, and appends “(restored copy)” to the notebook title.

Current size limits are 2 GiB for both the archive file and its aggregate uncompressed entries, 50,000 ZIP entries, 64 MiB per notebook JSON, ISF entry or manifest, 512 MiB per asset, 10,000 notebooks and 20,000 pages and sections per notebook. Section titles are nonblank and at most 10,000 characters; page titles may be empty and are limited to 10,000 characters; IDs are nonblank and at most 200 characters. Unknown format versions are rejected. [BackupService.cs](../src/Moye/Services/BackupService.cs) contains the complete validation rules. Tool presets, workspace preferences, reading history, recovery journals and in-memory undo history are not included in a notebook backup.

## Device-local workspace and recovery files

Alongside `moye.db`, `writing-preferences.json` retains its separate format 1. `workspace-preferences.json` format 1 contains reading positions keyed by notebook ID, last notebook, zoom/gesture/toolbar options and automatic-backup settings. These files are not notebook content. Workspace writes replace a flushed temporary file atomically; unreadable or unsupported files are kept and protected from automatic replacement.

`recovery/drafts/*.draft.json` contains format 1 `RecoveryDraft` objects: revision, capture time and notebook snapshot, including ISF encoded by JSON. Attachment references still depend on the local database. Draft filenames distinguish notebook and editing session; committing a revision removes only its matching or older pending draft in that session. Writes use an atomic temporary-file replacement, but are queued asynchronously and cannot guarantee capture of the last stroke, uncommitted text composition or events before a crash. Drafts are best-effort recovery, not a portable backup or persistent edit history.

`recovery/deleted` stores a complete `.moye` notebook snapshot plus `.deleted.json` metadata before a destructive notebook/section/page operation. Entries expire after 30 days and are removed when the recovery list is maintained. Restore creates a new notebook; it does not merge deleted pages into the existing one. Automatic backups write ordinary `.moye` files to the chosen folder while the app is running; retention applies only to completed files bearing that library's automatic-backup prefix. Recovery files and backups are not encrypted.
