# Penroam 1.1.0 — More Room to Write

Penroam 1.1 adds writable margins around existing PDF and paper pages, and makes selected ink, text and images move visibly while you drag them.

Download **Penroam-1.1.0-Setup-win-x64.exe** for Windows 11 x64. The installer includes the .NET runtime, updates the existing per-user installation and automatically creates desktop and Start menu shortcuts. A SHA-256 checksum is included with the download.

## Back up before upgrading

**Create a `.moye` backup with your current app before opening your library in 1.1.0. Keep that backup separately if you may return to an older version.**

This release upgrades libraries to **SQLite schema 4** and writes **backup format 4** to preserve PDF and paper-guide placement. **Penroam 1.0.0 and all former Moye releases cannot open the upgraded library or new backups.** Uninstalling does not reverse the migration. Penroam 1.1 reads backup formats 1, 2, 3 and 4; restoration creates new notebook copies.

The existing library location, `.moye` extension, preferences and recovery locations remain unchanged. Writing preferences, workspace preferences and recovery drafts remain format 1; recovery drafts still depend on the associated library assets. See the [format guide](FILE_FORMAT.md).

## Extend a page without shrinking its contents

Choose **Insert → Extend Page**, use a page's context menu, or find the command in **Commands & Help**. Drag a preview edge or corner, enter the extra space in millimeters, or use the right/bottom/both **+50 mm** actions. Reset and Cancel leave the original page unchanged until you apply the extension.

- Add blank writing space to any side of a PDF or paper page. Original content keeps its size; left or top additions move the PDF, ink, text and images together.
- Paper guides keep their original alignment and occupy their original area. Added margins are blank.
- Apply creates one undoable edit. Extended pages retain their geometry through saving, reopening, duplication, editable backups, previews and PDF export.
- Each resulting page dimension is limited to **5,080 mm**. This extends a finite page; it does not crop pages or create an infinite canvas.

PDF export preserves original vector content, searchable source text, crop/rotation and supported annotation appearances while adding the writing margins. Previously cropped-away content stays clipped. If an annotation crosses a crop edge and lacks a usable saved appearance, export reports the affected problem and leaves an existing destination file intact. Newly typed Penroam text still exports as outlines; keep `.moye` backups for editing.

## See selected content move as you drag

Lasso body dragging now moves the selected ink, text and images immediately, including a single selected text box. Releasing the pointer creates one edit for Undo/Redo. **Esc** cancels the drag, and a click or return to the original position creates no history entry. Saving, changing tools, reloading a page or losing capture commits the current complete position. Native lasso drawing and resize handles retain their existing behavior.

## Verification and remaining limits

The Release page records full automated test, detached WPF layout, packaged storage and installer checks for the exact published commit. Regression coverage includes live drag/model isolation, pressure ink, one-step undo, all right-angle PDF rotations with nonzero crops, annotation clipping, patterned-paper alignment, storage/backup/draft round trips, legacy formats and schema migration.

Earlier isolated native mouse checks verified lasso movement/Undo/Redo on 2026-10-04 and the Extend Page dialog/Apply/Undo/Redo on 2026-10-05. The latter preceded the final paper-guide alignment adjustment; final alignment is covered by subsequent WPF rendering and PDF tests. These checks do not establish physical pen/touch/palm behavior, printer output, high DPI or complete native workflow acceptance.

The installer remains unsigned. No new hardware acceptance or compatibility with every Windows application-control policy is claimed.
