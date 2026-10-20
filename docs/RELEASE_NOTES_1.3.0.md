# Penroam 1.3.0 — Folder Import and Two-Page View

Penroam 1.3.0 turns a folder of PDF and PowerPoint documents into an organized notebook and lets you read and edit adjacent pages side by side.

Download **Penroam-1.3.0-Setup-win-x64.exe** for Windows 11 x64. The installer includes the .NET runtime and updates the existing per-user installation. The accompanying SHA-256 file verifies the download. Penroam 1.2 users can also use **Check for Updates…** in the app.

## Import a folder as a notebook

- Choose **Import folder** on the notebook home screen, or drop one folder from File Explorer. Each PDF or PPTX directly inside the folder becomes a section, with filenames in natural order and pages or slides in their original order.
- Subfolders and unsupported files are skipped and reported. PPTX conversion requires locally installed Microsoft PowerPoint or LibreOffice and retains the existing restrictions on macros and linked external resources. Source documents remain unchanged.
- Preparation reports progress and supports cancellation. When some files fail, review the summary and explicitly choose **Import Successful Files**, or cancel without creating a notebook. Empty or entirely failed batches do not create empty notebooks.
- Failed saves can retry the prepared content without converting files again. If saving succeeds but opening fails, retry opens the same saved notebook instead of creating a duplicate.

## Read and edit two pages together

- Choose **Two Pages** from the zoom menu to show pages as 1–2, 3–4 and so on within the current section. Both pages support editing; an odd final page appears on its own.
- **Fit Spread** fits the pair, and **Fit Width** fits their combined width. Fit Width remains active when switching layouts. Page navigation, reading positions and keyboard navigation retain the selected page, including the right page.
- The device remembers the layout preference. Switching layouts does not alter page coordinates, page order, PDF placement, ink or export geometry. Zoom remains limited to 25%–400%; very wide pages can still require horizontal scrolling.

## Compatibility and upgrading

This release retains **SQLite schema 5**, **backup format 5** and support for reading backup formats **1–5**, matching Penroam 1.2. Workspace preferences remain format 1; the optional `twoPageView` setting defaults to single-page view when absent.

**If upgrading from 1.1.0 or earlier, create a `.moye` backup with the older app before opening your library.** Those versions cannot open schema 5 libraries or format 5 backups. Uninstalling does not reverse a migration. The library remains in `%LOCALAPPDATA%\Moye`; notebook, preference and recovery locations are unchanged. See the [format guide](https://github.com/yikeugene/Penroam/blob/v1.3.0/docs/FILE_FORMAT.md).

## Verification and limits

The GitHub Release records the published source commit and its Windows CI results. Regression coverage includes folder discovery and ordering, partial acceptance, cancellation, unchanged source files, save/open retries, asset verification, page pairing, navigation, virtualization, layout changes and preference compatibility. Detached WPF previews cover the import summaries and both page layouts at supported window sizes.

Physical pen/touch/palm behavior, broader IME sessions, printer output and application-control compatibility still require device testing. Automated conversion tests do not establish compatibility with every PowerPoint or LibreOffice installation. The installer remains unsigned. See the [user guide](https://github.com/yikeugene/Penroam/blob/v1.3.0/docs/USER_GUIDE.md) for workflows and limits.

The earlier **Moye 1.3.0** belongs to the pre-rebrand release sequence; its original notes are preserved in the [historical record](https://github.com/yikeugene/Penroam/blob/v1.3.0/docs/RELEASE_NOTES_MOYE_1.3.0.md).
