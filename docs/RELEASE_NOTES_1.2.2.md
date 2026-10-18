# Penroam 1.2.2 — Accurate Eraser Feedback

Penroam 1.2.2 makes the visible eraser outline match the area used to erase ink, including when the page is zoomed.

Download **Penroam-1.2.2-Setup-win-x64.exe** for Windows 11 x64. The installer includes the .NET runtime and updates the existing per-user installation. The accompanying SHA-256 file verifies the download. Penroam 1.2.0 and 1.2.1 users can also use **Check for Updates…** in the app.

## Changes

- Pixel and Stroke erasers display an outline derived from the same shape and page coordinates used by WPF ink hit testing. The outline follows page zoom, scrolling and eraser-size changes.
- Eraser feedback follows the active editing mode and pointer lifecycle. It clears when leaving the page, ending input, losing the relevant capture or changing tools, and restores the normal cursor when erasing is inactive.
- Touch navigation does not move a pen's eraser outline. The outline does not intercept input and is omitted from page thumbnails. Pixel erasing, whole-stroke erasing, highlighter-only filtering and undo retain their existing behavior.
- The merged documentation clarifies schema/backup format 5, Meeting paper and actual-size A4 export, and consolidates the product overview and development guide.

## Compatibility and upgrading

This patch retains **SQLite schema 5**, **backup format 5** and support for reading backup formats **1–5**, matching Penroam 1.2.0 and 1.2.1. It introduces no new data-format migration.

**If upgrading from 1.1.0 or earlier, create a `.moye` backup with the older app before opening your library.** Those versions cannot open schema 5 libraries or format 5 backups. Uninstalling does not reverse a migration. The library remains in `%LOCALAPPDATA%\Moye`; notebook, preference and recovery locations are unchanged. See the [format guide](https://github.com/yikeugene/Penroam/blob/v1.2.2/docs/FILE_FORMAT.md).

## Verification and limits

The GitHub Release records the exact source commit and Windows CI results for automated tests, detached WPF layouts, packaged SQLite save/reopen checks and installation/reinstallation/uninstallation checks. Eraser regression coverage includes hit boundaries, zoom, mode and size changes, highlighter-only behavior, touch suppression, capture cleanup and thumbnail isolation.

Detached rendering and synthetic tests do not establish physical pen/touch/palm behavior or complete native IME, printer and high-DPI acceptance. Tail-eraser behavior depends on the device and driver and still needs physical-device validation. The installer remains unsigned. See the [user guide](https://github.com/yikeugene/Penroam/blob/v1.2.2/docs/USER_GUIDE.md) for writing and study workflows.
