# Penroam 1.5.1

Penroam 1.5.1 combines Paper only writing, expanded floating tools and filtered PDF export.

## Changes

- Press **F10** or choose **View → Paper only** to hide the workspace controls while retaining the current window size. A floating circle opens and closes the writing toolbar. The sidebar and reference pane restore when leaving the mode, and save errors remain visible.
- Combine Paper only with **F11** full-screen focus mode. Floating tools now provide direct access to Two pages, Fit Page/Spread, Fit Width, Reference, Manage pages, All tools, full-screen switching and Restore workspace, alongside the existing writing tools.
- Filter PDF export pages by named section, current page or whole notebook; search page titles and section names, restrict results to bookmarks, and apply notebook-wide page ranges. Filters work together, with visible and selected counts and page previews.
- Export only checked pages in the filtered results, preserving notebook order. Select all results and Clear selection affect only visible results; changing filters keeps each page's checkmark, and Reset filters restores the full list. Pending or invalid page ranges must be resolved before export.
- Both features support English and Traditional Chinese, compact layouts, keyboard navigation and labelled touch targets. Search filters do not recognize handwriting or inspect PDF contents.

## Installation and compatibility

Download `Penroam-1.5.1-Setup-win-x64.exe` and its companion `.sha256` file. The installer includes the .NET runtime, installs for the current Windows account and creates desktop and Start menu shortcuts. Windows 11 x64 is required. The installer is unsigned.

Back up existing notebooks before upgrading. SQLite schema **5** and `.moye` backup format **5** are unchanged; backup formats **1–5** remain readable. Existing installation identity and the default `%LOCALAPPDATA%\Moye` library location are preserved.

Penroam 1.2.0 and later can use the built-in updater. Install manually when upgrading from 1.1.0 or earlier. The `v1.5.1` tag avoids reusing the historical Moye `v1.5.0` tag.

## Verification and limits

The release workflow runs the Windows test suite, English and Traditional Chinese detached layout checks, self-contained SQLite package checks, and installation/reinstallation/uninstallation checks in a disposable Windows runner. See the matching [GitHub Actions run](https://github.com/yikeugene/Penroam/actions) for the published build's results.

Detached rendering does not establish live keyboard focus, IME, pen pressure, touch or palm rejection. Physical pen/touch validation remains outstanding. Native Windows dialog buttons follow the Windows language; upstream release notes and technical error details may retain their original language.
