# Moye 2.0.0 — Organize, Read and Recover

Moye 2.0 brings notebook search and organization, flexible document workflows, a reference pane, and local backup and recovery tools to the offline Windows workspace.

Download **Moye-2.0.0-Setup-win-x64.exe** for Windows 11 x64. Setup includes the .NET runtime, updates the existing per-user installation and automatically creates desktop and Start menu shortcuts.

## Back up before upgrading

**Use your current Moye version to create a `.moye` backup before opening your library in 2.0.0. Keep it separately if you might return to the older app.**

Moye 2.0.0 migrates existing libraries to **SQLite schema 3** and writes **backup format 3**. **Moye 1.12.0 and earlier cannot open the upgraded library or new backups.** Uninstalling does not reverse the migration. Moye 2.0 reads backups in formats 1, 2 and 3; restoration creates new notebook copies. Writing preferences remain format 1. See the [format guide](https://github.com/yikeugene/moye/blob/v2.0.0/docs/FILE_FORMAT.md).

## Find your place and organize your notes

- **Continue** resumes the last notebook; reading position, page, section, zoom and Fit Width are remembered on this device. **Quick note** captures a page in an Inbox notebook.
- Pin notebooks, choose cover colors, filter by category and sort by name or modification date. Name pages and bookmark them.
- Search notebook, section and page names, typed notes and extractable original PDF text, with snippets and return navigation. PDF extraction runs locally through PdfPig; handwriting and scanned images are not recognized, and incomplete coverage is reported.
- Use the page grid for multi-page selection, reorder, duplicate, delete and section moves. Move or copy pages across notebooks atomically; **Undo Last Page Transfer** is available while both notebooks remain unchanged.

## Choose the pages you import and export

Preview PDF or locally converted Office pages, select pages or ranges, and choose a section or new notebook before import. Export the current page, section, whole notebook or selected pages. Text-overflow checks cover every selected page and can return you to the affected text box. Preparation and export provide progress and supported cancellation; completed PDFs can be opened directly or shown in their folder.

Office conversion still requires an installed compatible Word, PowerPoint or LibreOffice application. Original documents remain unchanged; imported pages are fixed backgrounds. Added PDF text remains vector outlines. Keep editable `.moye` backups.

## Write beside your reference material

A read-only **Reference View** has independent notebook/page navigation, zoom and refresh. Lasso selections can include ink, text and images together, including portable image assets when copied to another notebook. Plain-text paste creates a text box in the visible area. Nudge selected objects with the keyboard, and use **Continue text on next page** to move overflowing text with its formatting in one undoable action.

Workspace settings add optional two-finger-only navigation, zoom lock, compact favorites and left/right focus-tool docking. Focus mode includes Type, Select and three favorite pens. Use direct page numbers and searchable **Commands & Help**. Finger input on the outer document scrollbar is ignored, and one-finger touches beginning in the gray margin require a deliberate drag, reducing accidental page jumps.

## Local backup and recovery

- Restore selected notebooks from a backup as new copies.
- Enable automatic local backups with a chosen folder, interval and retention. They run only while Moye is open.
- Recover interrupted draft snapshots as new notebooks. Drafts are best effort, depend on attachment assets in the local library and may miss the final input events.
- Restore a complete notebook snapshot saved before supported notebook, section or page deletions, retained for up to 30 days. Restoration does not merge deleted pages into an existing notebook.

Workspace preferences and recovery files stay local and are not included in ordinary notebook backups. Backups and recovery files are not encrypted. Keep a separate backup in another storage location.

## Verification and remaining limits

The release page records full test, detached WPF layout, packaged storage and install/reinstall/uninstall results for the published commit. Regression coverage includes schema migration, older backup formats, cross-notebook transactions, PDF text extraction, mixed selection, export ordering, draft races and backup retention.

The installer remains unsigned. Native interaction with the new workflows, clipboard/drag-and-drop/focus, physical pen/touch/palm behavior, high DPI and broader IME handling remain unverified in this update. Detached rendering and managed tests do not establish device acceptance or compatibility with every Windows application-control policy. Infinite canvas, handwriting OCR, cloud synchronization and a persistent edit-history browser remain outside this release.
