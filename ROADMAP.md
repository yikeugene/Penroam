# Penroam University Note-Taking Roadmap

**Write fast. Organize easily. Review quickly.**

Penroam should support a complete university workflow: write during a lecture, annotate course material, organize knowledge, and revisit it without losing time or notes. This roadmap preserves all 41 requested feature areas. It is a development plan, not a claim that every listed feature is available.

Penroam is the new name for Moye. Version numbering restarts at 1.0.0 with the complete current feature set and new name/icon; the existing library location and `.moye` file compatibility remain unchanged. Dated verification below retains the scope of the original Moye checks.

This status reflects **Penroam 1.1.0**, reviewed on **2026-10-05**. Version 1.1 adds finite page extension and live lasso movement; see [release notes](docs/RELEASE_NOTES_1.1.0.md) for scope and compatibility. Version 1.12.0 adds redesigned context menus and section actions on right-click or Shift+F10. Version 1.11.0 adds selected-section PDF export, 21 fixed pen thickness levels and button/interface polish. Version 1.10.0 adds floating focus tools, page context menus and PDF navigation annotation import fixes. Version 1.9.0 adds the refreshed workspace, local Office document import and PDF compatibility fixes. Version 1.8.0 adds visual color selection, live stroke-thickness previews and touch-navigation scheduling/inertia, while retaining notebook sections and the SQLite startup repair from 1.7.0. The EXE installer continues to create desktop and Start menu shortcuts automatically. Checked items describe implemented behavior, including the first writing-workflow milestone below; unchecked items remain work to complete. Implementation and automated coverage do not establish successful physical pen or desktop interaction.

**Penroam 1.0.0, 2026-09-30:** this release establishes the current workflows below as the new 1.0 baseline. It does not complete all 41 roadmap areas or establish device acceptance.

- Saved reading position/zoom and Continue; quick capture into an Inbox notebook.
- Notebook pinning, cover colors and session category/name ordering; page titles/bookmarks, local typed/PDF text search and return navigation.
- Multi-page organization and atomic cross-notebook move/copy with a checked session-only Undo Transfer.
- Previewed page/range import and export, destination choices, export overflow preflight, progress/cancel and output actions.
- A read-only reference pane with independent navigation/zoom; mixed ink/text/image lasso and clipboard; explicit text continuation; keyboard object movement.
- Two-finger-only navigation and zoom lock, compact toolbar, left/right focus-tool docking with favorite pens/Type/Select, direct page numbers, and searchable Commands & Help.
- Atomic best-effort pending draft files, complete-notebook recovery copies before deletion retained for 30 days, selected-notebook restoration, and automatic local backups with retention while the app is open.

Penroam 1.1 writes **schema 4 / backup format 4** for PDF placement and paper-guide bounds, reads backup formats 1–4 and retains preference formats at 1. Penroam 1.0.0 and former Moye releases cannot open schema/format 4. Keep a backup made with the older app before upgrading. See [file compatibility](docs/FILE_FORMAT.md). PDF search has no OCR; recovery does not guarantee the last input events; the reference pane is read-only; full persistent version history and hardware acceptance remain open.

Version 1.6.0 adds an explicit Type action, contextual text controls and whole-box formatting. Basic live desktop typing and Chinese IME candidate selection have been checked. Its scope and remaining acceptance work are recorded in area 24; publication does not mark those outstanding checks complete.

Version 1.8.0 includes visual color palettes for ink, pen presets and text, plus a draggable thickness slider with a live pen/highlighter preview. Mouse checks covered these controls and saved values; physical touch and pen interaction still needs device validation.

Version 1.11.0 changes that slider to 21 fixed thickness levels, with visible ticks, finer pen increments and larger highlighter increments. Pen Settings (including focus mode) and preset editing share the same levels; opening existing custom presets preserves their exact width until the slider is adjusted.

The 1.8.0 finger-navigation implementation combines touch packets once per display update, adds elapsed-time swipe inertia, and defers thumbnail work during movement. Synthetic tests cover retained movement, pinch/contact transitions, boundary reversal and cancellation; these are correctness checks, not measured hardware frame-rate or latency acceptance.

Penroam 1.0 blocks finger input on document scrollbars, keeps rejected contacts blocked until release, and requires deliberate movement before a one-finger margin touch pans. Mouse/pen scrollbar use and ordinary paper pan/pinch remain available. Hardware palm-rejection and touch acceptance remain outstanding.

**Added in 1.9.0:** local Office document import through **Insert → Import Document…**. DOCX uses installed Microsoft Word or LibreOffice, PPTX/PPSX use PowerPoint or LibreOffice, and ODT/ODP require LibreOffice. Pages become fixed PDF backgrounds in the current section, with editable Penroam annotations. The source document remains untouched and only the converted PDF is embedded in the library and backups. No converter is bundled or downloaded.

## Status and evidence

- **Existing:** the described behavior is implemented. This does not establish performance on every device.
- **Partial:** an area has usable foundations and remaining features.
- **Planned:** the requested area has no complete user-facing implementation yet.
- **Hardware validation:** code exists, but physical pen, palm, touch, display, or IME acceptance remains outstanding. These checks stay unchecked.

Code and tests provide the following evidence:

| Evidence | Source |
|---|---|
| Native pen/touch separation, capture cleanup and live ink | [PenInkCanvas](src/Moye/Controls/PenInkCanvas.cs), [PageEditor](src/Moye/Controls/PageEditor.cs), [ink tests](tests/Moye.Tests/InkTests.cs) |
| Draw-and-hold recognition and native commit lifecycle | [HoldToStraightenSession](src/Moye/Controls/HoldToStraightenSession.cs), [gesture tests](tests/Moye.Tests/HoldToStraightenTests.cs), [lifecycle tests](tests/Moye.Tests/StraightInkLifecycleTests.cs) |
| Library, pages, history, navigation and shortcuts | [MainViewModel](src/Moye/ViewModels/MainViewModel.cs), [NotebookHistory](src/Moye/ViewModels/NotebookHistory.cs), [MainWindow](src/Moye/MainWindow.xaml.cs), [library tests](tests/Moye.Tests/LibraryTests.cs), [history tests](tests/Moye.Tests/HistoryTests.cs) |
| Paper, text, images and persistent document fields | [DocumentModels](src/Moye/Models/DocumentModels.cs), [PaperVisual](src/Moye/Controls/PaperVisual.cs), [PaperTemplatePicker](src/Moye/Controls/PaperTemplatePicker.cs), [NoteItemFrame](src/Moye/Controls/NoteItemFrame.cs) |
| Typing UI, whole-box typography and plain line prefixes | [typing UI](src/Moye/MainWindow.Typing.cs), [PageEditor](src/Moye/Controls/PageEditor.cs), [text editing helpers](src/Moye/Controls/TextEditing.cs), [typing tests](tests/Moye.Tests/TypingTests.cs), [text persistence tests](tests/Moye.Tests/TextPersistenceTests.cs) |
| Autosave, transaction integrity and recovery after write failure | [AutosaveCoordinator](src/Moye/Services/AutosaveCoordinator.cs), [SQLite repository](src/Moye/Services/SqliteNotebookRepository.cs), [storage tests](tests/Moye.Tests/StorageTests.cs) |
| Persistent writing presets and settings recovery | [WritingPreferences](src/Moye/Models/WritingPreferences.cs), [WritingPreferencesStore](src/Moye/Services/WritingPreferencesStore.cs), [preset manager](src/Moye/Controls/PresetManagerDialog.cs), [preference tests](tests/Moye.Tests/WritingPreferencesTests.cs) |
| Preset application, editable ink clipboard, selection width, eraser filtering and thumbnail tool preservation | [writing UI](src/Moye/MainWindow.Writing.cs), [writing workflow tests](tests/Moye.Tests/WritingWorkflowTests.cs) |
| Monotonic autosave scheduling and canonical library identity | [autosave timing tests](tests/Moye.Tests/AutosaveTimingTests.cs), [library location tests](tests/Moye.Tests/LibraryLocationTests.cs), [application startup](src/Moye/App.xaml.cs) |
| PDF rendering/export and editable backup packages | [PdfService](src/Moye/Services/PdfService.cs), [BackupService](src/Moye/Services/BackupService.cs), [PDF tests](tests/Moye.Tests/PdfTests.cs), [PDF performance fixtures](tests/Moye.Tests/PdfPerformanceTests.cs), [backup tests](tests/Moye.Tests/BackupTests.cs) |
| Penroam 1.0 import/export selection, staged assets and local PDF text extraction | [document workflows](src/Moye/MainWindow.DocumentWorkflows.cs), [workflow service](src/Moye/Services/DocumentWorkflows.cs), [PDF extraction](src/Moye/Services/PdfTextExtractionService.cs), [workflow tests](tests/Moye.Tests/DocumentWorkflowTests.cs), [PDF search tests](tests/Moye.Tests/PdfTextExtractionTests.cs) |
| Penroam 1.0 page organization, search and mixed selection | [organization view model](src/Moye/ViewModels/MainViewModel.Organization.cs), [page organizer](src/Moye/Controls/PageManagerDialog.cs), [mixed selection](src/Moye/Controls/PageEditor.Selection.cs), [text flow](src/Moye/Controls/TextFlow.cs), [organization tests](tests/Moye.Tests/OrganizationTests.cs) |
| Penroam 1.0 workspace, backup and recovery | [workspace UI](src/Moye/MainWindow.Workspace.cs), [recovery UI](src/Moye/MainWindow.Safety.cs), [draft journal](src/Moye/Services/DraftRecoveryStore.cs), [local safety](src/Moye/Services/LocalSafetyService.cs), [safety tests](tests/Moye.Tests/WorkspaceSafetyTests.cs) |

The linked public test sources describe reproducible automated coverage. Tests and detached layout renders do not prove current hardware latency, palm rejection, high-refresh rendering, sleep recovery, or live IME composition. Benchmark numbers from small synthetic fixtures must not be presented as guarantees for scanned textbooks or long lectures.

## First writing-workflow milestone

Version 1.6.1 adds notebook deletion from home cards and the editor's More menu, with confirmation. Tests cover failed-save/delete recovery, pending-save ordering, and clearing the deleted notebook's history. Earlier isolated desktop checks on 2026-09-14 covered canceling and confirming deletion from both entry points. Penroam 1.0 adds complete-notebook recovery copies before notebook/section/page deletion, with 30-day retention and restore as a new copy; it does not merge individual deleted pages back automatically.

- [x] Persistent named pen/highlighter presets with rename, duplicate, delete, reorder and favorite visibility; favorite preset buttons support drag reordering.
- [x] Per-preset pen opacity, pressure sensitivity and smoothing, plus exact preset widths. Highlighters retain native 50% compositing.
- [x] Independent 12–120 DIP eraser size and highlighter-only erasing for pixel/stroke modes.
- [x] Editable ink copy/cut/paste across pages, preserving pressure and custom metadata; oversized pasted ink fits the destination page. Selected ink width can be changed.
- [x] Ctrl+Shift+Z redo, Space plus mouse drag to pan, and 1–9/numpad preset access, with existing text editing shortcuts protected.
- [x] Focus mode hides notebook and writing chrome, provides an exit control, and keeps save failures accessible.
- [x] Monotonic autosave deadlines and temporary touch suppression; canonical default/explicit library paths share the single-instance identity.
- [x] Thumbnail generation preserves Pen, Highlighter, Pixel Eraser, Stroke Eraser and Lasso modes without changing saved ink or losing lasso selection.

The in-memory writing workflow suite passed **24 cases** on 2026-09-14. It uses no desktop windows or system clipboard actions; eraser filtering invokes the compiled cancellable event hook. This is method-level regression evidence. Real clipboard interaction, drag reordering, shortcut focus transitions and physical input still need desktop/device acceptance. The wider roadmap remains open.

## Prioritized milestones

### P0 — Dependable lecture writing

The core is ink, palm handling, pen/highlighter, erasing, lasso, undo, zoom, autosave, and page management. Reliability work has priority over adding more tools.

- [ ] Validate fast small handwriting, pressure transitions, pen lift/out-of-range, palm before/after pen, one-finger pan, pinch zoom, eraser input, and alignment after zoom on real Windows Ink hardware.
- [ ] Measure input callback time, frame pacing, memory and save delay with long handwritten notebooks and representative 100-page scanned PDFs at normal and high DPI. Record hardware and document size with each result.
- [ ] Reduce unnecessary work on the UI input path. The baseline copies every page's ISF in `AutosaveCoordinator.Schedule`; the repository snapshots again and hashes every page. Profile this cost before moving to immutable changed-page snapshots.
- [x] Use monotonic elapsed time for autosave deadlines and temporary touch suppression, so clock adjustment cannot extend a hold-off or the continuous-edit save window.
- [x] Resolve one canonical library identity for single-instance protection, including equivalent explicit/default data directories and path casing; cover the mapping with automated tests.
- [ ] Exercise normal close, notebook switching, return home, forced termination after a committed save, disk-full/write denial, save retry and recovery backup. Clearly distinguish committed data from memory-only pending edits.
- [ ] Keep one undoable edit per completed ink/erase/selection operation, and prevent hidden editors or popup shortcuts from mutating a notebook unintentionally.

P0 acceptance requires recorded device checks and failure-path evidence. Existing automated tests remain regression gates; a passing build alone does not close these items.

### P1 — Faster course work

- [x] Provide persistent named pen presets and a quick-access favorite-preset toolbar.
- [ ] Extend customization to the fixed tool buttons and separate favorite/recent color management.
- [ ] Extend the existing draw-and-hold line feature into explicit shape tools with predictable undo and pressure behavior.
- [ ] Add richer paper templates and template management without changing existing notebooks unexpectedly.
- [x] Included in Penroam 1.0.0: PDF page/range previews and import/export choices; editable mixed-object clipboard and ordinary text paste.
- [x] Included in Penroam 1.0.0: page bookmarks and a read-only reference pane for material beside handwritten notes.
- [ ] Continue native interaction and representative long-document validation; image cropping/rotation and fully editable split panes remain future work.

Each feature must retain editing, save/reopen, backup and PDF behavior where applicable. Keep primary actions usable with a pen and at least 44 DIP touch targets. Introduce these changes in small releases rather than marking the whole milestone complete at once.

### P2 — Organize and revisit knowledge

- [x] Included in Penroam 1.0.0: page titles, typed/PDF content search with coverage messages, and return navigation after search/quick capture.
- [ ] Add tags, internal notebook links and broader persistent navigation history.
- [ ] Add durable version history with safe restore and comparison.
- [ ] Add infinite canvas as an optional document mode with a minimap and a defined export strategy.
- [ ] Add selection export, customizable shortcuts and advanced arrangement tools.

P2 changes that introduce new persistent fields or document modes need a documented format version, compatibility behavior and backup round trips. Infinite canvas is now a future product direction; it is not available in the current fixed-page editor.

## Feature areas

### 1. Writing Experience

**Status: Partial · Hardware validation. Priority: P0.**

- [x] Native WPF pressure-aware ink, vector stroke storage, pen-priority handling, finger pan/pinch routing, and page-coordinate zoom.
- [ ] Measure and tune low latency, high-refresh frame pacing, smoothing, prediction and crisp rendering across zoom levels.
- [ ] Validate palm rejection and pen priority on real devices; add handedness and sensitivity preferences where needed.

### 2. Pen Tools

**Status: Partial. Priority: P0 for the basic pen/highlighter; P1 for presets and toolbar.**

- [x] Pen and highlighter, color and width controls, native pressure for pen, and separate remembered pen/highlighter colors during a session.
- [x] Per-preset pen opacity, pressure-sensitivity and smoothing toggles; highlighter presets use their exact stored width and native half-opacity.
- [x] Persistent named presets with rename, reorder, duplicate and delete; favorite preset quick access, drag reordering and per-preset toolbar visibility.
- [ ] Add distinct ballpoint, fountain pen, pencil and marker behavior, configurable pressure-response curves and richer smoothing controls.
- [ ] Add drag/reorder/hide customization for the fixed tool buttons, recent colors and favorite colors.

### 3. Eraser

**Status: Partial · Hardware validation. Priority: P0.**

- [x] Pixel/partial and whole-stroke erasing; toolbar mode selection and remembered eraser mode.
- [x] Native inverted-pen erasing is configured in the ink control.
- [x] Highlighter-only erasing and independent eraser size controls, persisted with writing preferences.
- [ ] Validate tail erasers and side buttons; support configurable pen-button behavior where the device exposes it.

### 4. Undo and Redo

**Status: Partial. Priority: P0.**

- [x] Up to 100 history steps in the open notebook session, with toolbar and keyboard undo/redo, including Ctrl+Y and Ctrl+Shift+Z redo.
- [ ] Add deliberate two-finger-tap undo and three-finger-tap redo, with gesture recognition that does not conflict with pan/zoom or palm contact.
- [ ] Validate coherent history for every new object, clipboard and shape operation. Session undo is separate from durable version history in area 32.

### 5. Lasso

**Status: Partial. Priority: P0 for existing selection; P1 for extensions.**

- [x] Select ink, move, resize, duplicate, delete and recolor it.
- [x] Selected-stroke width changes and editable ink copy/cut/paste across pages, with pressure/metadata retained and a single undoable paste.
- [ ] Add rotation, grouping and ungrouping.
- [x] Included in Penroam 1.0.0: select, move, resize and copy mixed ink/text/image selections across pages, retaining image assets and undo.
- [ ] Extend mixed selection to future shape objects and validate native clipboard/focus transitions.

### 6. Natural Gestures

**Status: Partial · Hardware validation. Priority: P1.**

- [x] Draw and hold a line for about 650 ms, adjust its endpoint, then lift to commit one straight stroke; the behavior can be disabled.
- [ ] Validate recognition timing, false positives and live preview transitions with an active pen.
- [ ] Add scribble-to-delete and circle-to-select with clear cancellation and undo behavior.

### 7. Shapes

**Status: Partial. Priority: P1.**

- [x] Straight lines through draw-and-hold.
- [ ] Add explicit line, arrow, rectangle, square, circle, ellipse, triangle and polygon tools.
- [ ] Extend draw-and-hold recognition to appropriate shapes without changing ordinary handwriting or intentionally rough sketches.

### 8. Ruler and Measurement Tools

**Status: Planned. Priority: P1.**

- [ ] Add a straightedge, ruler and protractor.
- [ ] Support edge snapping, visible angles, and common-angle constraints while keeping tools easy to move away from writing.

### 9. Zoom and Pan

**Status: Partial · Hardware validation. Priority: P0.**

- [x] Finger pan, pinch zoom, Ctrl+wheel, zoom buttons, Fit Page, Fit Width and 100% view.
- [x] Fit Width responds to the available writing area; page-coordinate anchors account for fixed page gaps during zoom.
- [ ] Add double-tap reset and optional smooth transitions that do not delay pen input.
- [ ] Validate touch interaction, edge cases during capture loss, and alignment after resize/zoom on hardware.

### 10. Focus

**Status: Partial. Priority: P0 for usable writing space; P1 for refinements.**

- [x] Sidebar toggle and fullscreen focus mode with the system window frame, writing toolbar and notebook title hidden; an exit control restores editing chrome and save failures remain accessible.
- Version 1.10.0 adds a floating focus toolbar for writing tools, color/width, undo/redo and exit. Page management also moves to each thumbnail/paper's right-click menu. Device touch and pen acceptance remains outstanding.
- Included in Penroam 1.0.0: dock focus tools left/right; keep three favorite pens, Type and Select available; optionally hide the main favorite-pen row. These are device-local workspace settings.
- [ ] Add independent visibility preferences for the writing toolbar, notebook title and other chrome outside the combined focus mode.
- [ ] Add temporary/hover tool access suitable for both pen and keyboard use.

### 11. Keyboard

**Status: Partial. Priority: P2; useful low-risk improvements may ship earlier.**

- [x] Basic tool shortcuts, undo/redo, duplicate, delete, save, sidebar and focus shortcuts; text-box shortcuts are protected.
- [x] Space plus mouse drag pans temporarily; 1–9/numpad keys select favorite presets; Ctrl+C/X/V transfers editable ink between pages.
- [x] Included in Penroam 1.0.0: mixed ink/text/image copy/cut/paste, plain-text paste as a box, keyboard object movement, page-number entry and searchable Commands & Help.
- [ ] Validate native focus/clipboard transitions, including mixed selections and IME; add customizable mappings with conflict detection.

### 12. Better Than Paper

**Status: Partial — continuing design goal. Priority: applies to every milestone.**

- [x] Erase without damaging a page, undo edits, rearrange pages, mix media and make editable backups.
- Included in Penroam 1.1.0: **Extend Page** adds space on any side of PDF or paper pages, with draggable preview handles, millimeter inputs, one-step undo and preserved PDF scale. Extended geometry roundtrips through SQLite, backups and PDF export. Physical pen/touch interaction still needs device validation; this is bounded page expansion, not infinite canvas.
- [ ] Evaluate changes against the full lecture-to-review workflow: fewer interruptions while writing, less effort organizing, and quicker retrieval while studying.
- [ ] Preserve immediate writing access and predictable local saving as the feature set expands.

### 13. Notebook System

**Status: Partial. Priority: P0 for the library; P1 for organization.**

- [x] Startup notebook home, explicit creation/opening, titles, category strings, title/category search and recently modified ordering.
- Implemented in 1.12.0: section context menus for rename, reorder and confirmed deletion, with stable targets and first/last/only-section boundaries. Native menu interaction remains unverified.
- Implemented in 1.7.0: **Notebook → Section → Page**, with section names/order, page transfer, undo/redo, autosave and versioned backup migration. Existing pages are assigned to **General**. This structure supports **Course → Topic → Notes**; it does not add filesystem folders or nested sections.
- [x] Included in Penroam 1.0.0: notebook pins and cover colors, session category filtering/name sorting, Quick Notes inbox, and saved reading position/Continue.
- [ ] Add real folders/subfolders, notebook duplication and a dedicated recent-notebooks view.
- [ ] Make organizational changes reversible where possible and preserve library search/selection during updates.

### 14. Page Management

**Status: Partial. Priority: P0.**

- [x] Page thumbnails, add, delete, duplicate and reorder using move-up/down commands.
- [x] Included in Penroam 1.0.0: multi-page thumbnail selection, drag/button reordering, bulk duplicate/delete, section moves and atomic cross-notebook move/copy. Checked Undo Transfer refuses to overwrite later changes.
- [ ] Add page rotation and validate organizer drag/keyboard/touch behavior on the native desktop.
- [ ] Preserve ink, text, image and PDF alignment during page transformations and bulk operations.

### 15. Bookmarks

**Status: Partial. Priority: P1.**

- [x] Included in Penroam 1.0.0: bookmark/unbookmark pages, name them with page titles, list/filter bookmarks and open stable page targets. Reordering/moving retains the mark; deleted pages disappear from current search results.
- [ ] Add multiple named locations within one page if needed; validate native bookmark/navigation workflows.

### 16. Tags

**Status: Planned. Priority: P2.**

- [ ] Support multiple tags on notebooks and pages.
- [ ] Add tag management, filtering and search, without treating the current single category string as a complete tagging system.

### 17. Templates

**Status: Partial. Priority: P1.**

- [x] Visual choices for Blank, Ruled, Grid, Dot Grid, Cornell and Graph on new notebooks/pages and existing non-PDF pages.
- [x] Share template geometry across the editor, thumbnails and vector PDF export, with PDF round-trip regression coverage.
- [ ] Add background color, line/grid spacing, paper size and orientation choices. Keep rendering and PDF export consistent.

### 18. Custom Templates

**Status: Planned; import primitives exist. Priority: P1.**

- [x] PDF pages and imported images can be included in notebooks.
- [ ] Turn imported PDF/image material into reusable templates; save a page as a template.
- [ ] Add template naming, preview, management and removal. Ordinary content import is not yet a template library.

### 19. PDF Import

**Status: Partial. Priority: P1.**

- [x] Import all supported PDF pages into the current section; retain the original PDF asset.
- Implemented in **1.9.0**: append locally converted DOCX/PPTX/PPSX/ODT/ODP pages through **Import Document…**, with **Cancel Import**/Esc and a two-minute conversion timeout. Imported Word text and slide objects are fixed backgrounds; animations, video playback and internal jumps are omitted while ordinary URI links are retained. Unsupported encryption, macros and linked external resources produce an error. An existing PowerPoint session may need to be closed before conversion.
- [ ] Complete representative Office/LibreOffice layout and conversion acceptance across supported formats and installed application versions. Converter availability and installed fonts affect the result; this does not add editable Word or presentation documents.
- [x] Included in Penroam 1.0.0: staged import with thumbnails, selected-page/range selection, destination section/new section/new notebook and current-page/end placement. Canceling before acceptance leaves the notebook unchanged.
- Implemented in **1.10.0**: import supported annotations with internal destinations or interactive actions as static annotations. Original PDF bytes remain in the library and backups; exported copies disable these actions while retaining annotation appearances and ordinary URI links, including after page reordering or duplication.
- [ ] Keep clear validation for unsupported files. Encrypted PDFs, interactive forms, signatures and unsupported interactive annotation types remain unsupported.

### 20. PDF Annotation

**Status: Partial. Priority: P1.**

- [x] Freehand ink/highlighter, erasing and lasso over imported pages, plus text boxes and images.
- [ ] Add the planned shape tools and richer text/object controls to PDF pages.
- [ ] Continue crop/rotation alignment and transparency regression checks; distinguish editable `.moye` content from flattened PDF sharing.

### 21. PDF Export

**Status: Partial. Priority: P1.**

- [x] Selected-section export with original PDF content, vector ink outlines, images and flattened annotations (since 1.11.0; earlier releases export the whole notebook).
- [x] Added text boxes export as vector glyph outlines; original PDF text keeps its original capabilities.
- [x] Included in Penroam 1.0.0: current-page/section/notebook/checklist/range export with preview/count, full-range text-overflow preflight, progress/cancel and completed-file actions.
- [ ] Add searchable/selectable added text with correct Unicode/font behavior. Current added text remains vector outlines; PDF text extraction does not change that export limitation.

### 22. Images

**Status: Partial. Priority: P1.**

- [x] PNG/JPEG import, clipboard image paste, move, resize, duplicate and delete.
- [ ] Add drag-and-drop import, rotation, cropping and locking.
- [ ] Preserve original image assets and define whether transformations remain reversible in editable backups.

### 23. Screenshots

**Status: Partial. Priority: P1.**

- [x] Paste a clipboard screenshot as an image; fit it within the page from a fixed initial position.
- [ ] Improve placement at the cursor or visible writing area and offer cropping immediately after paste.
- [ ] Keep pasted text, images and notebook selections distinct so standard clipboard behavior remains predictable.

### 24. Text

**Status: Partial · Hardware validation for IME. Priority: P1.**

Implemented locally: Type creates or resumes a box; the contextual bar formats the whole box; plain list markers support Enter continuation; boxes grow to the page boundary and scroll after reaching it. The local run on **2026-09-14** passed **178 automated tests and 21 detached UI scenes**, including the linked typing and text-persistence regressions. A live Windows desktop check that day confirmed a short Chinese IME candidate selection (`t` then Space committed `他`), English input, Type resume, bold, 24 pt size entry followed by more typing, bullet continuation and native undo, and Ctrl+Enter returning to Pen. The acceptance items below stay open because clipboard, longer/interrupted composition, cross-page transitions and the complete end-to-end desktop workflow still require their respective verification.

- [x] Editable Unicode text boxes, line wrapping, movement/resizing and selected text-object color changes. The model stores font family and size.
- [ ] Complete and verify the local Type workflow: start/continue a text box, add another box, and expose font family/size, bold, italic, color and alignment for the whole box. Preserve formatting through save/reopen, editable backup and vector PDF export.
- [ ] Verify local plain bullet/number prefixes, Ctrl+B/Ctrl+I whole-box formatting, Ctrl+Enter to finish, and Escape focus handling with native text clipboard and undo.
- [x] Included in Penroam 1.0.0: explicit Continue text on next page preserves the remainder and formatting as one undoable edit; export preflight checks all selected pages and can jump to an overflowing box.
- [ ] Verify automatic box-height growth, internal overflow scrolling and continuation with native IME/clipboard interaction. Continuous automatic flow between pages remains unavailable.
- [ ] Add per-range rich text and structured list behavior if needed; plain line prefixes and whole-box font controls do not complete those capabilities.
- [ ] Extend the short live candidate/cancellation checks to long or interrupted composition, additional candidate choices and mixed-language editing across pages. Direct Unicode persistence tests do not cover composition, and short desktop checks do not complete IME acceptance.

### 25. Search

**Status: Partial. Priority: P2.**

- [x] Notebook-title and category search.
- [x] Included in Penroam 1.0.0: search notebook/category/section/page names, bookmarks, typed notes and extractable original PDF text, with snippets and page navigation. PDF extraction is local, bounded and reports incomplete/no-text coverage.
- [ ] Add tags and search them; validate search performance with representative course libraries.
- [ ] Consider handwriting/OCR search later, with an explicit offline capability and storage design rather than an assumed external service.

### 26. Page Titles

**Status: Partial. Priority: P2.**

- [x] Page numbers and template/PDF captions in page navigation.
- [x] Included in Penroam 1.0.0: manually editable page titles persist through moves, save/reopen and backups, and appear in bookmarks/search. Quick Notes pages receive a date/time title.
- [ ] Add configurable date/number naming patterns for ordinary pages.

### 27. Links

**Status: Partial navigation; internal links planned. Priority: P2.**

- [x] Included in Penroam 1.0.0: session back navigation after a content-search or quick-note visit.
- [ ] Add links to pages and notebooks and copyable internal links.
- [ ] Define stable targets, broken-link handling, and behavior when a notebook is restored as a new copy. Retained external URI annotations in a source PDF are not internal notebook links.

### 28. Split View

**Status: Partial. Priority: P1.**

- [x] Included in Penroam 1.0.0: a read-only reference snapshot beside the main editor, including other notebooks or PDF pages; independent page navigation, zoom, refresh and return-to-writing action.
- [ ] Support adjustable ratios and horizontal/vertical arrangements.
- [ ] Define the active editing pane, shared-document save/history ownership, independent navigation and safe pen focus transfer.

### 29. Infinite Canvas

**Status: Planned. Priority: P2.**

- [ ] Add a separate canvas mode with navigation on both axes, zoom and a minimap.
- [ ] Define spatial loading, selection across large distances, meaningful limits and export/page slicing.
- [ ] Preserve compatibility with existing fixed-page notebooks and make document-mode conversion explicit. The current continuous-page workspace is not an infinite canvas.

### 30. Page Mode

**Status: Partial. Priority: P1.**

- [x] Fixed A4 pages; imported PDFs retain their page dimensions, and the data model stores per-page width/height.
- [ ] Expose A4, A5, Letter and custom sizes, portrait and landscape orientation.
- [ ] Define how existing content behaves when changing size or orientation, including undo and PDF output.

### 31. Autosave

**Status: Partial. Priority: P0.**

- [x] Completed edits schedule background transactional saves; pending revisions coalesce with a two-second target during continuous edits.
- [x] Notebook changes/normal close flush pending saves; errors retain memory snapshots and offer retry/backup rather than falsely reporting success.
- [x] Monotonic autosave scheduling with deterministic wall-clock-change regression coverage.
- [x] Included in Penroam 1.0.0: atomic local pending-draft snapshots and explicit recovery-as-copy. Committed revisions cannot remove a newer draft; recovery remains best effort and may miss the final events.
- [ ] Validate deadline behavior, memory/snapshot overhead and journal write cost with heavy content and abrupt termination/disk failure.
- [ ] Validate crash boundaries on representative devices: committed SQLite data, queued drafts and active input have different durability; draft files still depend on library assets.

### 32. Version History

**Status: Planned. Priority: P2.**

- [ ] Keep automatic historical snapshots with retention controls, a version list, comparison and restore-as-copy/safe restore.
- [ ] Separate durable versions from the current in-memory undo history and manual backup archives.

### 33. Local First

**Status: Existing. Priority: P0 — preserve throughout development.**

- [x] Writing, editing, PDF work and local saving work offline without an account or server.
- [x] Notebooks live in a local SQLite library; portable backups move editable content between installations.
- [ ] Keep future search, recovery and optional integrations explicit about local resources and network use. No cloud dependency is planned for basic note-taking.

### 34. Backup

**Status: Partial. Priority: P0 for recovery; P1 for automation.**

- [x] Manual single-notebook/all-notebook `.moye` packages, integrity checks and restore as new copies.
- [x] Included in Penroam 1.0.0: automatic local backups while Penroam is open, folder/interval/retention settings, last-success/failure display, selected-notebook restoration and 30-day deleted-notebook recovery copies.
- [ ] Validate interruption, unavailable destinations and retention on representative large libraries. No backup task runs while Penroam is closed.
- [ ] Exercise restore against large real course libraries. Backups are currently unencrypted, and copying a live database file is not a replacement for a consistent export.

### 35. Open Format

**Status: Partial. Priority: P1 for documentation; P2 for broader interoperability.**

- [x] Versioned ZIP backups contain a JSON manifest and notebook structure, ISF page ink, assets and original PDFs; implementation is open source.
- [ ] Publish the local [schema/format guide](docs/FILE_FORMAT.md), examples and compatibility rules; add optional previews.
- [ ] Consider portable stroke-point interchange. ISF retains editable Windows ink, but is not already a plain JSON point format that every platform can read directly.

### 36. Vector Ink

**Status: Partial. Priority: P0 for current ink integrity; P2 for richer interchange.**

- [x] Editable stroke samples, pressure, drawing attributes and widths retained through ISF; vector outlines used for PDF sharing.
- [ ] Add explicit per-sample timestamp persistence and documented point/width/opacity representation where needed.
- [x] Pen opacity and pressure-sensitivity controls with ISF/clipboard regression coverage; highlighters keep native half-opacity compositing.
- [ ] Add configurable highlighter opacity and richer pressure response only with consistent display/export behavior. The model's hold-recognition clock is not persisted stroke timing.

### 37. Object Layers

**Status: Planned. Priority: P2.**

- [ ] Add object bring-forward/send-back controls and locking.
- [ ] Define a shared ordering model for ink, text, images and shapes. Current rendering has fixed paper/content/ink layers rather than a user-editable object layer stack.

### 38. Alignment and Snapping

**Status: Planned. Priority: P2.**

- [ ] Add optional grid snapping, alignment guides, rotation constraints and snapping between objects.
- [ ] Keep freehand input unconstrained unless explicitly requested, and make snapping state visible and reversible.

### 39. Selection Export

**Status: Planned. Priority: P2.**

- [ ] Copy a selection as an image or to the clipboard; export PNG, transparent PNG or a selection PDF.
- [ ] Define bounds, resolution, transparency and mixed-content rendering. Internal thumbnail rendering is not yet a selection-export feature.

### 40. Dark Paper

**Status: Planned. Priority: P2.**

- [ ] Add black, gray and cream paper choices with suitable pen color adaptation.
- [ ] Define readable preview/print/export behavior without silently recoloring existing ink or original PDF content.

### 41. Presentation

**Status: Partial. Priority: P2.**

- [x] Fullscreen focus and existing page navigation provide a basic starting point.
- [ ] Add presentation-specific hidden UI, direct page navigation, a laser pointer and temporary ink that does not enter saved notes unless requested.
- [ ] Keep presentation annotations separate from notebook edits and support a reliable return to writing mode.

## Completion policy

Close an unchecked item only after the implementation is usable, its persistence/export effects are verified, and any stated device acceptance is recorded. Changes to paper, shapes, links, layers or canvas modes must round-trip through saving and editable backups. Any remaining limitation should appear in release documentation. This roadmap can change in priority, but feature availability and validation status must stay factual.
