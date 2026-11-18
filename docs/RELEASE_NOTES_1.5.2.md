# Penroam 1.5.2

Penroam 1.5.2 improves import reliability, search results and recovery while preserving existing notebook formats.

## Fixes and improvements

- **Find by appearance** now loads matching pages in batches of 60, with **Load more** and displayed/total counts. Results beyond the first 60 remain reachable. Changing filters cancels outdated previews; a failed preview can be retried without duplicating earlier results. Keyboard users can open a selected result with Enter.
- PDF import requires an entered page range to be applied before continuing. Press Enter or **Apply range** to select those pages; clear an unfinished or invalid range to retain the current checkmarks.
- Original PDF and image attachments are checked against the **512 MiB** backup attachment limit before being stored. Converted Office PDFs use the same limit. Selecting fewer PDF pages does not reduce the original attachment size; the separate 2 GB total backup limit still applies.
- Image import validates dimensions before decoding a bounded preview in the background. Original bytes and dimensions remain intact, and the editor reuses the prepared preview.
- Creating a quick note while editing the Inbox is now one undoable edit and preserves earlier undo history.
- Switching from Lasso to Type resumes the selected text box, even when another box was edited more recently.
- Bookmark-only content search can find a notebook or section by its name at the first eligible bookmarked page.
- Backup restoration and interrupted-draft recovery preserve practice-page source links and give copied question groups a separate identity when their source is included. Draft recovery checks saved practice-question attachments and keeps the draft if an attachment is missing.
- Missing or repeated `--data-dir` arguments report an error and exit instead of opening the default notebook library.

## Installation and compatibility

Download `Penroam-1.5.2-Setup-win-x64.exe` and its companion `.sha256` file. The installer includes the .NET runtime, installs for the current Windows account and creates desktop and Start menu shortcuts. Windows 11 x64 is required. The installer is unsigned.

Back up existing notebooks before upgrading. SQLite schema **5** and `.moye` backup format **5** are unchanged; backup formats **1–5** remain readable. Existing installation identity and the default `%LOCALAPPDATA%\Moye` library location are preserved.

Penroam 1.2.0 and later can use the built-in updater. Install manually when upgrading from 1.1.0 or earlier.

## Verification and limits

The release workflow runs the Windows test suite, English and Traditional Chinese detached layout checks, self-contained SQLite package checks, and installation/reinstallation/uninstallation checks in a disposable Windows runner. See the matching [GitHub Actions run](https://github.com/yikeugene/Penroam/actions) for the published build's results.

Detached rendering does not establish live keyboard focus, IME, pen pressure, touch or palm rejection. Physical pen/touch validation remains outstanding. Native Windows dialog buttons follow the Windows language; upstream release notes and technical error details may retain their original language.
