# Penroam 1.2.1 — Clearer Typing and Focus Controls

Penroam 1.2.1 improves the spacing of free typing on ruled paper and the readability of the Add page button in focus mode.

Download **Penroam-1.2.1-Setup-win-x64.exe** for Windows 11 x64. The installer includes the .NET runtime and updates the existing per-user installation. The accompanying SHA-256 file verifies the download. Penroam 1.2.0 users can also use **Check for Updates…** in the app.

## Changes

- Free typing on Ruled, Cornell and Meeting paper places text baselines four device-independent pixels above the horizontal guides. Line spacing continues to follow the paper, including larger fonts, extended pages and text continued onto another page. Reapplying alignment preserves the same position instead of moving text upward repeatedly.
- The focus-mode **Add page** button uses a wider horizontal icon and label layout, with larger text. Layout checks cover both window sizes and left/right focus-tool docking so the label remains inside the button.

## Compatibility and upgrading

This patch retains **SQLite schema 5**, **backup format 5** and support for reading backup formats **1–5**, matching Penroam 1.2.0. It introduces no new data-format migration.

**If upgrading from 1.1.0 or earlier, create a `.moye` backup with the older app before opening your library.** Those versions cannot open schema 5 libraries or format 5 backups. Uninstalling does not reverse a migration. The library remains in `%LOCALAPPDATA%\Moye`; notebook, preference and recovery locations are unchanged. See the [format guide](FILE_FORMAT.md).

## Verification and limits

The GitHub Release records the exact source commit and Windows CI results for automated tests, detached WPF layouts, packaged SQLite save/reopen checks and installation/reinstallation/uninstallation checks. Regression coverage includes ruled text spacing, stable repeated alignment, text continuation and focus-button content bounds.

Detached rendering and synthetic tests do not establish physical pen/touch/palm behavior or complete native IME, printer and high-DPI acceptance. The installer remains unsigned. The broader writing and study workflows are described in the [user guide](USER_GUIDE.md).
