# Penroam 1.2.0 — Write, Study and Return

Penroam 1.2 adds free typing, Meeting paper, study questions, saved next steps, separate practice attempts, appearance search, regional session-history restore, actual-size A4 PDF export and in-app updates.

Download **Penroam-1.2.0-Setup-win-x64.exe** for Windows 11 x64. The installer includes the .NET runtime, updates the existing per-user installation and creates desktop and Start menu shortcuts. The accompanying SHA-256 file lets you verify the download.

## Back up before upgrading

**Create a `.moye` backup with your current app before opening your library in 1.2.0. Keep it separately if you may return to an older version.**

Penroam 1.2 upgrades libraries to **SQLite schema 5**, writes **backup format 5** and reads formats **1–5**. Penroam 1.1.0 and earlier releases cannot open the upgraded library or new backups. Uninstalling does not reverse this migration. The schema 4 upgrade retains existing page JSON, ink and hashes; format 5 protects the new text and study metadata from older writers.

The library remains in `%LOCALAPPDATA%\Moye`; preferences, recovery locations and the `.moye` extension remain unchanged. Restoration creates new notebook copies. See the [format guide](FILE_FORMAT.md).

## Writing and study workflows

- **Free typing:** choose Type or press T and click the paper. New text wraps without visible box borders, follows ruled baselines on Ruled, Cornell and Meeting paper, and remains editable. Select reveals movement and resize handles; Text box remains available for fixed layouts.
- **Meeting paper:** type or write into areas for the title, attendees, agenda, discussion, decisions and action items.
- **Questions and next steps:** mark a question against a page or region, review it in Question Inbox, and track explanation/retry states. Leave a Resume Hint saves where to continue and returns you to the source after reopening.
- **Practice attempts:** capture a question once, keep separate solution pages, revisit earlier attempts and save reflections. Each attempt retains the saved question and its source link when available.
- **Find by Appearance:** filter ink and typed text in the open notebook by color family, page position, section and mark type. This does not perform handwriting recognition or inspect colors inside PDFs or images.
- **Restore Part of This Page:** restore complete objects inside a selected region from compatible versions in the current notebook session. Work outside the region remains intact and the operation is undoable. History is limited to 100 edits and is not retained across sessions.
- **Actual-size A4 export:** export original page sizes or tile pages onto A4 sheets without shrinking the content. Choose orientation and overlap, inspect the grid preview, and print at Actual size/100%. Exports are limited to 1,000 A4 sheets.

## In-app updates

Install 1.2.0 manually once when upgrading from 1.1.0 or earlier. Penroam then checks for newer stable GitHub releases at startup; **Check for Updates…** is also available from More or Library Options. Update now downloads the installer, verifies SHA-256 and saves pending notes before installation. Download cancellation and retry are available.

Notes remain usable offline. Update checks send the app version to GitHub without notebook contents. Launches with `--data-dir` disable the updater. The installer remains unsigned. See the [user guide](USER_GUIDE.md#in-app-updates).

## Verification and remaining limits

The GitHub Release records automated tests, detached WPF layout checks, packaged SQLite save/reopen checks, and installation/reinstallation/uninstallation checks for the published commit. Test data is synthetic.

These checks do not establish physical pen/touch/palm behavior, complete native IME acceptance, printer output, high-DPI acceptance or compatibility with every Windows application-control policy. Newly typed text exports as outlines rather than searchable PDF text. Persistent regional history, revised-PDF annotation reconciliation, handwriting OCR, automatic mistake diagnosis and spaced review remain unavailable.
