# Moye user guide

This guide describes the English interface in **Moye 2.0.0** for Windows 11 x64. Historical release and device-check notes below keep their original scope.

Version 1.6.0 adds the **Type** button and text-formatting workflow described below.

**Before upgrading to Moye 2.0.0:** create a `.moye` backup using your current app and keep it separately. Moye 2.0 migrates libraries to schema 3 and writes format 3 backups, while reading backups in formats 1, 2 and 3. Published Moye 1.12.0 and earlier cannot reopen schema 3 or format 3. Uninstalling does not reverse this migration.

## Install, update and uninstall

Download `Moye-2.0.0-Setup-win-x64.exe` from the GitHub Release and run it. Setup installs Moye for your Windows account and automatically creates desktop and Start menu shortcuts. No administrator password or separate .NET runtime installation is required. The default application folder is `%LOCALAPPDATA%\Programs\Moye`.

Open the **Moye** desktop shortcut after installation. When updating, close the app and run the newer installer. Existing notes stay in `%LOCALAPPDATA%\Moye`; the installer does not move or replace them. If you previously used a portable ZIP with the default library, the installed app uses that same library. A custom `--data-dir` library still needs its custom launch argument.

Remove Moye through **Windows Settings → Apps → Installed apps → Moye → Uninstall**. This removes installed program files and shortcuts while retaining notebook data and writing preferences. Use Moye's backup commands to make a portable copy of your notes.

## Notebooks, sections and pages

The **1.9.0 interface** uses a warm neutral workspace with forest green controls, clearer notebook covers and visible keyboard focus. Version 1.11.0 refines buttons, pen badges and focus indicators. Version 1.12.0 adds redesigned context menus and section actions on right-click or Shift+F10. The navigation names below describe 2.0.0; version 1.8.0 uses **All Notes**, **Presets…** and **Create a Notebook** for the corresponding actions.

Open `Moye.exe` to see **Your notebooks**. Moye starts on this home screen and waits for you to choose a notebook. An empty library shows **Create your first notebook**; it does not create a notebook automatically. Notebook cards show the title, category and page count. **Continue** opens the last notebook at its saved reading position. **Quick note** creates a page in an Inbox notebook and selects Pen, ready to write; use the page organizer to move it into a course later.

Each card's options menu offers **Pin / Unpin** and **Choose Cover Color…**. Pinned notebooks stay above other notebooks. **Library Options → Sort & Filter Library…** filters by the existing single category and chooses name order or recent-modification order. The filter and sort choice apply to the current app session; pin and cover choices are notebook content and survive saving/backups.

Click a notebook cover to open it, or **New Notebook** to choose a name, category, and first-page paper. The visual template cards offer six choices; new notebooks initially select Ruled. Canceling leaves the library unchanged.

| Paper | Layout |
|---|---|
| Blank | White paper without guides. |
| Ruled | Horizontal lines for writing. |
| Grid | Even square guides for diagrams and calculations. |
| Dot Grid | Evenly spaced dots for flexible layouts. |
| Cornell | A cue column, ruled notes area and summary area. |
| Graph | Fine squares with stronger major guides. |

New paper pages are A4, and each template uses fixed spacing. The same geometry appears in page backgrounds, thumbnails and exported PDFs. Custom sizes, adjustable spacing and saved custom templates remain future work.

Click **My notebooks** to save your changes and return home. You can also switch notebooks using the editor's **Notebooks** sidebar tab. Opening a notebook restores its saved page, position and zoom when available; otherwise it opens its first section. Click the document title, or choose **More → Rename and Category**, to change its title and category. The home/sidebar search filters notebook titles and categories. On the home screen, `Ctrl+F` focuses that search. Click its clear button, press `Esc` while search has focus, or choose **Clear search** in the no-results message to clear text and category filtering. **Find content** or `Ctrl+Shift+F` opens the separate content search described below. The home screen also offers **Restore backup** and **Library Options → Back Up All Notebooks**.

Select a page thumbnail, then use **Insert → Add Page**, or **Add Page** beneath the thumbnails. Choose a paper preview and click **Add Page** to insert after the current page. The picker starts with the current ordinary page's style, or Ruled when viewing a PDF. Right-click a thumbnail or the paper to open that page's menu: duplicate, move or delete it without first selecting it. The menu identifies the target page. Page deletion can be undone. Deleting the last page in the notebook leaves a new blank page; a section can otherwise be empty.

Right-click the page and choose **Paper Style**, select a visual template, and click **Apply Paper** to change an existing page. Your writing, text and images stay in place. This changes only that ordinary page; PDF pages keep their original background.

To delete a notebook, open its **Notebook options (⋯)** menu below the cover and choose **Delete Notebook…**, or open the notebook and choose **More → Delete Notebook…**. The confirmation names the notebook and defaults to **No**. Before deleting a notebook, section or pages, Moye preserves a complete notebook copy for up to 30 days in **Backup & Recovery → Recently deleted**. Restoring creates another notebook; it does not put individual pages back automatically. Deleting the open notebook returns you to the home screen. If saving, recovery-copy creation or deletion fails, retry after addressing the error.

## Organizing a course

Use one notebook per course, such as **Mathematics**, then create a section for each topic, such as **Linear Algebra** or **Calculus**. A section contains ordinary paper pages and imported PDF pages.

In **Contents**, click **＋** beside **SECTIONS** to name a section. It starts with one page using your current paper style (or Ruled when the current page is a PDF). Select a section to show its pages; page numbers start at 1 within each section. Returning to a section recalls its last selected page during the current editing session.

Right-click any section to rename, move it up/down or delete it. Opening the menu keeps your current page in view; its heading identifies the section you clicked. You can also focus a section and press **Shift+F10**, or use **Section Options (⋯)** for the selected section. The first section cannot move up, the last cannot move down, and the only remaining section cannot be deleted. **Delete Section…** asks for confirmation and removes its pages; notebook Undo can restore them while the notebook stays open. Right-click a page and choose **Move Page to Section** to organize existing pages; the editor follows the moved page. An empty source section remains available, with **Add Page** ready to create its next page.

New and older notebooks begin with **General**, which you can rename to your first topic. Section changes are autosaved, and switching sections commits pending handwriting and text. Document import lets you choose a destination. **Export** initially selects the current section and lets you choose another page scope. A `.moye` notebook backup preserves every section, its name, order and editable page contents.

Use **More → Manage Pages…** for a thumbnail overview. Select pages using their checkboxes or Ctrl/Shift-click; drag them before another page, use **Move up/down**, duplicate them, delete them or move them to a selected section. **Apply changes** applies the organizer edit as one notebook undo step; **Cancel** discards it. **Move / copy to notebook…** lets you choose another notebook and section, with **Keep a copy in this notebook** for copying. The two notebooks are saved together. **More → Undo Last Page Transfer** reverses the latest transfer in this session only if neither notebook has changed since; it refuses to overwrite later edits.

Use a page's **Rename Page…** action or the organizer's **Title / bookmark** button to name it and mark it for review. **Bookmark Page** / **Remove Bookmark** also toggles the mark directly. Titles and bookmarks follow the page when reordered or moved, and appear in backups.

## Finding and revisiting notes

**Find content**, **More → Search Contents…**, or `Ctrl+Shift+F` searches across notebook/category names, section/page titles, typed text and extractable text in original PDFs. In the editor, `Ctrl+F` opens this search when a text control does not own the shortcut. Results show notebook, section, page and a nearby text excerpt; open a result to jump there. **More → Bookmarks…** lists marked pages. **More → Back to Previous Page** returns from a search or quick-note visit to the earlier reading location during the current session.

PDF extraction runs locally using PdfPig. It does not run OCR or recognize handwriting; image-only scans may have no searchable text. The search dialog reports pages it could not fully search, including damaged or unusually large text content. A missing result is not proof that a scanned page lacks the word. Search results are temporary and never added to the page's editable text boxes.

## Pen presets

The favorite toolbar starts with four tools:

| Preset | Thickness |
|---|---:|
| Black Pen | 0.45 mm |
| Blue Pen | 0.45 mm |
| Red Pen | 0.35 mm |
| Yellow Highlighter | 3 mm |

Click a favorite or press its displayed number, `1`–`9`, to select it. The toolbar shows the first nine presets marked as favorites. Drag a favorite onto another to change their order. If a favorite is outside the visible area, scroll the toolbar horizontally.

Use **Pen Settings → Save Current as Preset…** to name and save a new tool from the current writing settings. Open **Manage pens** to rename a preset, choose Pen or Highlighter, edit its color and thickness, duplicate it, delete it, or move it with **↑** and **↓**. Clear **Show in favourite toolbar** to hide a preset without deleting it. **Save and Use** applies your changes and activates the selected preset, including hidden presets; **Cancel** leaves your saved presets unchanged. You can keep up to 40 presets and must retain at least one.

Drag the **Thickness** slider in Pen Settings or the preset editor to choose a fixed size and see a live sample stroke. Moye 1.11.0 offers **21 levels**, marked by ticks, from approximately **0.13 to 6.35 mm**. Fine sizes include **0.35** and **0.45 mm**, with larger increments for broad pens and highlighters, including **3 mm**. Arrow keys move to the next size; Home and End select the smallest and largest sizes. Previously saved custom widths keep their exact value until you adjust the slider.

The preview follows the color, pen/highlighter, opacity, pressure and smoothing settings; its width is shown at page scale, independently of document zoom. When editing selected ink with Lasso, the final width is applied once at the end of the drag, so one Undo restores the previous thickness. The app stores widths in fixed page coordinates, so zooming changes their display size rather than the saved stroke width. Pens support **10–100% opacity**, **Pressure sensitivity**, and **Stroke smoothing**. Highlighters use native **50% transparency**; their opacity field is not adjustable. Colors are selected visually: click a swatch in **Pen Settings**, or **More Colors…** to choose from the palette and drag in the color square. The rainbow slider changes the hue; the square changes saturation and brightness. **Apply** uses the new color and **Cancel** keeps the original. Existing color transparency is preserved. The preset editor and text-color button use the same picker.

Presets, their order and favorites, the last selected preset, eraser settings, and Draw and Hold are saved locally between launches. They are writing preferences rather than notebook content; see [Autosave and backups](#autosave-and-backups) for their separate settings file.

## Writing and touch

Select **Pen** or **Highlighter**, then open **Pen Settings** to choose a color and width. The two tools retain separate colors when you switch between them. Pressure input requires a Windows Ink-compatible active pen. A mouse can also draw.

Page thumbnails update in the background without changing your selected writing or eraser mode. During finger navigation, preview generation waits until movement settles, then refreshes one thumbnail at a time. Cached thumbnails are reused when revisiting unchanged pages.

Finger input is reserved for navigation: drag with one finger to scroll, or use two fingers to pan and zoom. A quick one-finger swipe continues with a gentle slowdown after release; holding still before releasing stops without a fling. Touch the page again, use a tool or keyboard command, or put your pen down to stop the motion. Input is combined for each display update so rapid touch packets do not lose movement. Page gestures pause while the pen is down. Pen response, scrolling frame rate and palm rejection depend on your laptop, pen, and drivers, and still need validation on the device you use.

In Moye 2.0, the outer page scrollbars ignore finger touches to prevent accidental track taps from jumping a page. Use a mouse or pen on the scrollbar, or drag the paper with a finger. A one-finger gesture starting in the gray margin waits for more than 12 screen DIP of movement before panning; small taps and jitter leave the page still. Two-finger pan/zoom remains responsive. Contacts rejected during pen input stay rejected until lifted, even if the pen lifts or a gesture is canceled first. Text-box scrollbars retain their separate behavior.

With **Pen** or **Highlighter**, draw a line and keep the tip down near its endpoint for about **0.65 seconds**. It straightens while you are still holding. Continue dragging to adjust its length and angle, then lift to finish. The same gesture works by holding the left mouse button. Pressure and ink appearance are retained, and the result saves as one editable stroke. Short marks, circles, and strongly curved handwriting stay freehand. **Pen Settings → Draw and Hold** is enabled by default; turn it off for uninterrupted freehand drawing. This switch applies to both Pen and Highlighter and is remembered between launches.

Click the eraser icon to open **Eraser Settings**:

- **Pixel Eraser** removes only the touched portion of ink, leaving the surviving fragments editable. It does not erase the paper, PDF background, text boxes, or images.
- **Stroke Eraser** removes the entire ink stroke when you touch any part of it.

The toolbar shows **Eraser**; its tooltip and settings identify the remembered Pixel or Stroke mode. Click the eraser again to close its picker, or select a mode and continue writing. Press `E` to recall the last eraser mode. **Eraser Size** independently adjusts the erasing area from **12 to 120 DIP**, with a default of 20 DIP. Turn on **Erase highlighter only** to protect ordinary pen strokes while removing highlighting. Both modes remain undoable. Size, mode and the highlighter-only choice are remembered between launches.

The remembered mode also applies to the pen's tail eraser when the device sends inverted-pen events. Actual tail-eraser and side-button behavior still depends on your device and drivers.

Use **Lasso** to circle ink, text boxes and images together. Drag the selection to move it, or drag its boundary handles to resize it. Press `Ctrl+D` to duplicate or `Delete` to remove the selection. With ink selected, you can also change its color or thickness through **Pen Settings**. Arrow keys move a selected object or mixed selection by one page DIP; hold `Shift` for ten. These commands apply outside text editing.

Press `Ctrl+C` to copy the selection, or `Ctrl+X` to cut it. Open another page and press `Ctrl+V` to paste editable ink, text and images with their layout and image assets. Ink retains pressure and stroke attributes. Outside a text editor, clipboard text creates a new text box, and clipboard images can be pasted as before. Text boxes keep their normal text clipboard, undo and IME behavior while being edited; notebook selection shortcuts do not replace those native commands.

## Viewing a page

Notebooks restore their last saved page, position, zoom and Fit Width setting on this device; a new or unavailable saved location uses the normal fit-page opening. The footer combines page navigation, save status, and zoom controls. Click the page counter or press `Ctrl+G` to enter a page number within the current section. **Fit Page** shows the whole sheet. **Fit Width**, directly beside it, expands the paper to the available writing area's width with a small margin on both sides; scroll vertically to see the rest of the page. While Fit Width is active, resizing the window or showing/hiding the sidebar refits the paper. **−**, **+**, pinch zoom, `Ctrl+mouse wheel`, or **Fit Page** leave width-fitting mode. Click the percentage for zoom options, including **Actual Size · 100%**. Turn off zoom lock in Workspace settings before changing the scale.

The sidebar button or `F9` hides and shows the sidebar. Hold **Space** and drag with the left mouse button to pan temporarily; release Space to return to your writing tool.

The **Focus Mode** icon or `F11` switches to a full-screen writing workspace and hides the notebook header, main writing toolbar, sidebar and footer. A compact floating toolbar keeps **Pen**, **Highlighter**, **Eraser**, **Lasso**, **Type**, **Select**, the first three favorite pens, **Pen Settings**, **Undo**, **Redo** and **Exit Focus** available. **Workspace settings** docks it on the left or right. The selected tool is highlighted; the color button opens the same palette and thickness preview used in the normal editor. The eraser button offers Pixel and Stroke modes. Keyboard tool shortcuts still work, and save errors remain visible. Click **Exit Focus**, press `F11`, or press `Esc` to leave. When typing in a text box, `Esc` first leaves text editing. These controls change the display, not the paper's dimensions.

Open **Workspace** on the home screen or **More → Workspace Settings…** to require two fingers for page movement, lock zoom while writing, hide the main favorite-pen row, or change focus-tool docking. When two-finger navigation or zoom lock is enabled, **Guard** appears in the editor footer and focus toolbar and opens these settings. These settings are stored on this device and do not change saved page coordinates. Physical pen/palm/touch behavior still needs device testing.

**More → Reference View…** opens another notebook or a different page beside your writing. The reference is a read-only snapshot, with its own page selection, zoom and **Fit** action. **Refresh** reads the latest copy; **Back to writing** returns focus to the editing area. Close the pane to reclaim its space. Only the main pane edits notes; this is not simultaneous editing in two panes.

Open **Commands & Help** from More or Library Options, or press `Ctrl+Shift+P`. Search a command's name or explanation, select it to see its purpose, then press `Enter` or **Run selected command**. `Esc` closes the panel. This is a command reference with working actions; it does not change shortcut mappings.

## Typing notes

Click **Type** to begin typing on the current page or continue editing the selected or most recently used text box on that page. If no text box exists, Moye creates one. Click an existing box to place the caret there. To add another box, use **＋ Text box** in the text bar or click an empty part of the page while Type is selected.

The text bar controls the selected box's font family, font size (**6–96 pt**), bold, italic, color and Left/Center/Right alignment. **All formatting applies to the entire box**, even if you have selected just one word. To use different formatting for a heading and body, create separate text boxes.

**• List** and **1. List** toggle plain text prefixes on the current line or selected lines. `Enter` continues a marked line; pressing `Enter` on an empty marked item ends the list. The markers remain editable characters, not structured rich-text list objects or an automatic outline.

While editing an ordinary paragraph, `Enter` inserts a line break. `Ctrl+B` toggles bold for the box and `Ctrl+I` toggles italic. `Ctrl+Enter` or `Esc` finishes typing, moves focus out of the text box and returns to Pen. Standard text selection, copy/cut/paste and undo continue to act on the text editor; notebook ink shortcuts do not replace them. Finish typing before using notebook Undo to reverse box formatting. Windows IME composition uses the native text control. A live desktop check confirmed basic Chinese candidate selection followed by English typing; the broader input checks below remain open.

A box grows vertically while you type, up to the bottom of its page. Additional text scrolls within the box and triggers an overflow hint. Select **Continue text on next page** to keep the visible portion in the current box and put the remainder into a new page's box with the same formatting. The operation is one notebook undo step. Repeat if the continuation is still too long. Pagination is an explicit action; text does not flow automatically between linked pages.

Font, size, bold, italic, color and alignment are stored with the text box. Existing notes remain compatible. A `.moye` backup keeps text editable; added PDF text is still exported as vector outlines rather than searchable or selectable text.

## Images and object placement

Choose **Insert → Insert Image** for PNG or JPEG files. Outside text editing, `Ctrl+V` accepts an editable Moye selection, legacy Moye ink, ordinary text, or an image/screenshot. A mixed Moye selection carries its own image assets and can be pasted into another notebook.

Choose **Select** and click a text box or image. Drag its upper-right move handle to reposition it, or its lower-right handle to resize it. Press `Ctrl+D` to duplicate the selected object or `Delete` to remove it.

Images are limited to 20,000 pixels per side and 80 million pixels in total. The original image bytes are retained; resizing its display does not rewrite the source.

## PDF annotation and export

Choose **Insert → Import Document…** and select a PDF. After validation, inspect the thumbnails and selected-page preview, check pages or enter a range such as `1–5, 8, 12`, then choose the current/another section, a new section or a new notebook. Page numbers refer to the preview list. You can append to the destination section or insert after the current page when importing to that section. Closing the preview leaves the notebook unchanged. Accepted pages retain the original PDF in the library and can receive ink, highlighting, text and images. PDF import needs no Office application.

Click **Export** to choose the current section, current page, whole notebook or individual pages/ranges. The initial selection is the current section. Preview the pages and check the selected count before continuing; chosen pages retain their displayed notebook order. A single-section export suggests **Notebook - Section.pdf**. The export checks all selected pages for overflowing text, including pages not currently visible. **Go to selected text** returns to the affected box so you can continue it on another page; **Export visible text** deliberately keeps only its current visible appearance.

Import validation and PDF export show progress and provide cancellation at supported steps. After accepted import pages start saving, the operation finishes the commit instead of reporting an unchanged notebook. Export writes a temporary file and replaces the destination only when complete. On success, choose **Open PDF**, **Show in folder**, or **Done**.

Added ink and text become PDF page content rather than editable Moye objects. Pressure ink stays vector-based, preserving its changing width and solid joins. New text boxes are exported as vector outlines: their appearance is retained, but the exported text cannot be selected or searched. Original PDF text retains its existing capabilities. Keep a `.moye` backup if you need to edit the contents later.

**New in 1.10.0:** supported annotations containing page jumps or interactive actions no longer block PDF import. The notebook displays static pages and keeps the original PDF bytes in the library and `.moye` backups. Exported copies preserve page content and annotation appearances while disabling internal destinations and interactive actions, including chained actions on web links. Ordinary URI links remain. Your source file is not changed.

Encrypted documents, interactive forms, digital signatures and unsupported interactive annotation types are not supported. Empty form metadata without fields is accepted. For unsupported annotation types, flatten their visible appearance in another PDF tool before importing. Moye validates the document before adding its pages and reports the affected page when page validation fails. If a file still fails, keep the original and include the error message when reporting it.

## Office document import

Open a notebook and section, choose **Insert → Import Document…**, then select a supported document:

| File | Required local application |
|---|---|
| DOCX | Microsoft Word or LibreOffice |
| PPTX, PPSX | Microsoft PowerPoint or LibreOffice |
| ODT, ODP | LibreOffice |

Moye uses the installed desktop application to convert the file locally, then opens the same page preview, range and destination choices as PDF import. It does not bundle or download an Office application or conversion runtime. If none is available, export a PDF in the source application and import that instead. Fonts and layout depend on the installed converter.

Imported pages are fixed PDF backgrounds. Add editable Moye handwriting, highlights, text boxes and images over them; the original Word text and slide objects cannot be edited in Moye. Slides are static: animations, video playback and internal page or slide jumps are not retained. Ordinary web links remain in the PDF. The source file is untouched. Only the converted PDF, together with your Moye annotations, is stored in the library and `.moye` backups; keep the original document separately.

Use **Cancel** or press `Esc` during import preparation. Conversion has a two-minute timeout. Password-protected files, files containing macros, and documents with linked external resources are rejected with an explanation; embed linked content or export a PDF from the source application. PowerPoint may need to be closed before conversion because it can share one running application instance. If Moye reports that PowerPoint is in use, save your work and close it before retrying, or export a PDF there.

## Autosave and backups

Completed edits are queued for background saving, with a coalescing delay of at most two seconds. Completion time depends on the disk and document size. The saved status appears only after a successful database transaction. Switching notebooks, leaving the window, and closing normally also attempt to save.

The default library is `%LOCALAPPDATA%\Moye\moye.db`. SQLite may create `moye.db-wal` and `moye.db-shm` beside it. Do not move only the database or delete its journal files while the app is open. Moye 2.0 upgrades libraries to schema 3 and writes format 3 backups. It reads formats 1, 2 and 3, placing legacy pages without sections in **General**. Published Moye 1.12.0 and earlier cannot open schema 3 / format 3. The new fields preserve page names/bookmarks and notebook pins, cover colors and the quick-note destination marker. See [File format](FILE_FORMAT.md).

Writing preferences are saved separately in `%LOCALAPPDATA%\Moye\writing-preferences.json`. This file contains presets and writing settings, and is **not included in `.moye` backups**. For a library started with `--data-dir`, both the database and preferences stay in that selected directory. A damaged settings file is preserved before defaults are offered; an unreadable or unsupported-version file is protected from replacement. A writing-settings warning offers details and retry when available. When an existing preferences file cannot be read or belongs to a newer version, tool changes apply to the current session only; notebooks still save and the app can close normally. Restart after resolving that file. A later write failure retains pending settings for retry.

`workspace-preferences.json` in the same directory stores reading positions, last notebook, writing guards, toolbar/docking choices and backup settings. These are device-local preferences, separate from notebook undo and excluded from `.moye` backups. Unsupported or unreadable workspace settings are kept; changes then apply to the current session only.

Autosave deadlines and temporary touch suppression use elapsed-time measurements, so changing the system clock does not extend those waiting periods. Launching the same library through equivalent default or explicit paths uses a shared single-instance identity.

Use the **More** menu to move or preserve your notes:

- **Back Up This Notebook** exports the current notebook, including its current unsaved content.
- **Back Up All Notebooks** exports the library, including pending revisions.
- **Restore .moye Backup** validates the backup, then lets you select notebooks to restore with new identifiers. Existing notebooks are not overwritten. Restored copies do not become the quick-note destination automatically.

A `.moye` backup preserves ink, pressure, editable text, images, page order, and original PDFs. Its uncompressed contents are limited to 2 GB, with a 512 MB limit per asset and 64 MB per notebook metadata file or page of ISF ink. Backups are not encrypted.

**Workspace Settings → Automatic local backups** chooses a folder, an interval of **1–168 hours**, and **1–100 completed backups** to retain. These backups run only while Moye is open, defer during busy work or active pen input, and include current/pending notebook snapshots. They are not a Windows background task. Retention deletes only automatic backups belonging to this library; manual backup files remain separate. **Backup & Recovery** shows the last successful automatic backup and any failure, with settings and manual backup actions. A backup folder on another device/storage location provides protection that another copy on the same failing disk cannot.

**Backup & Recovery → Recently deleted** lists complete notebook copies saved before deleting a notebook, section or pages, retained for up to **30 days**. Restore as a new copy and move back any wanted pages yourself; existing notebooks are not overwritten. **Delete permanently…** removes the selected recovery copy after confirmation. These recovery files stay under the library's `recovery/deleted` directory and are not an encrypted archive or a general version-history browser.

Completed edits also queue atomic pending-draft snapshots under `recovery/drafts`. After an interrupted session, **Recover drafts** or **Backup & Recovery → Interrupted drafts** lets you recover a snapshot as a new notebook or discard it after confirmation. Drafts reference image/PDF assets in the local library; they are not self-contained portable backups. The journal is best effort: it may not contain the final input events, a still-active stroke or text composition, and a failed disk write cannot be guaranteed to leave a recoverable draft. Successfully committed revisions clear their matching pending snapshot; a draft is not evidence that a newer database save failed.

If saving fails, pending content stays in memory. Use **Retry Save**, or export a `.moye` backup to a writable location. Normal closing keeps the window open if pending content cannot be saved. Force-quitting can still lose recent uncommitted edits even with draft recovery enabled. Keep regular backups in another storage location.

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| Pen / highlighter / eraser in its selected mode | `B` / `H` / `E` |
| Lasso / Type / select objects | `L` / `T` / `V` |
| First nine favorite presets | `1`–`9` or numeric keypad `1`–`9` |
| Undo / redo | `Ctrl+Z` / `Ctrl+Y` or `Ctrl+Shift+Z` |
| Duplicate selected ink or object | `Ctrl+D` |
| Select all ink, text and images on the current page, outside text editing | `Ctrl+A` |
| Delete the selection | `Delete` |
| Copy / cut selected editable ink, text and images | `Ctrl+C` / `Ctrl+X` |
| Paste an editable selection, text or image outside text editing | `Ctrl+V` |
| Move selected objects by 1 / 10 page DIP | Arrow keys / `Shift` + arrow keys |
| Content search from anywhere | `Ctrl+Shift+F` |
| Content search in editor / notebook filter on home | `Ctrl+F` (native text controls keep their shortcuts) |
| Go to a page within the current section | `Ctrl+G` |
| Commands & Help | `Ctrl+Shift+P` |
| Save now | `Ctrl+S` |
| Show or hide the sidebar | `F9` |
| Focus mode | `F11` |
| Zoom | `Ctrl+mouse wheel` |
| Temporary mouse pan | Hold `Space` and drag with the left mouse button |
| Bold / italic for the whole text box while typing | `Ctrl+B` / `Ctrl+I` |
| Finish typing | `Ctrl+Enter` |
| Leave text-box focus | `Esc` |

## Startup and operation errors

Operation errors include the underlying cause and, when the log can be written, its location. The log is `error.log` beside the notebook database, including for a custom `--data-dir` library. If Windows application control blocks a required component, install the latest Moye update; if the block persists, provide the log to support or your administrator. Keep your notebook database and journal files in place. Logs can contain local paths and operation details; review them before sharing.

## Device checks still needed

Physical pen and touch validation remains outstanding. A live desktop check on 2026-09-14 confirmed basic Chinese IME composition and candidate selection, English typing, Type resume, bold, size entry with restored text focus, bullet continuation and its native undo, and Ctrl+Enter returning to Pen. The packaged app also reopened saved text/formatting, accepted T from the page workspace with the Chinese IME active, and cancelled short composition and font-size edits with Esc. This covers a short typing session; the following device and broader input checks remain open:

1. Fast continuous writing, small characters, light and heavy pressure, highlighting, and both eraser modes.
2. Resting a palm before and after pen contact, without producing finger ink or moving the page.
3. One-finger scrolling, two-finger zoom, and pen alignment after scrolling and zooming.
4. Moving the pen beyond the page, lifting it, switching windows, and resuming from sleep without a stuck input state.
5. The specific pen's tail eraser and side buttons, which depend on driver events.
6. Long or interrupted IME composition, additional candidate choices, text clipboard operations, and retaining composition/focus correctly across page changes. The short candidate and cancellation checks do not establish these longer or interrupted workflows.
7. Draw-and-hold preview before lifting, endpoint adjustment, freehand release, pressure variation, and pen-up/capture-loss cleanup at different zoom levels. The default hold timing and jitter tolerance may need tuning for your digitizer.

See the README's [verification and limitations](../README.md#verification-and-limitations) summary for the scope of automated checks and outstanding device validation.
