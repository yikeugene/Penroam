# Penroam 1.4.1

Penroam 1.4.1 brings together the feature navigation and interface language branches.

## Changes

- Switch between English and Traditional Chinese (繁體中文) from the notebook home header without restarting. The choice is remembered for this library location on this device. Notebook content, names and pen preset names remain unchanged.
- Find features through labelled Notebook, Pages, Insert & export, Search, Study, View, Tools, Backup and Help menus, with direct buttons for common page and view actions.
- Open **All tools** with Ctrl+Shift+P to browse categories, search tools and help, see shortcuts and understand why an action is unavailable. The catalog and its help support both interface languages.
- Reach page, section and notebook actions through visible buttons. Page and section actions retain their original targets and recheck availability before changing content.
- Open text formatting and advanced pen settings from the tool browser. Focus mode retains access to All tools, and compact layouts keep touch targets accessible.

## Installation and compatibility

Download `Penroam-1.4.1-Setup-win-x64.exe` and its companion `.sha256` file. The installer includes the .NET runtime, installs for the current Windows account and creates desktop and Start menu shortcuts. Windows 11 x64 is required. The installer is unsigned.

Back up existing notebooks before upgrading. SQLite schema **5** and `.moye` backup format **5** are unchanged; backup formats **1–5** remain readable. Existing installation identity and the default `%LOCALAPPDATA%\Moye` library location are preserved. Workspace language is a device-local preference and is excluded from notebook backups.

Penroam 1.2.0 and later can use the built-in updater. Install manually when upgrading from 1.1.0 or earlier. The `v1.4.1` tag avoids reusing the historical Moye `v1.4.0` tag.

## Verification and limits

The release workflow runs the Windows test suite, English and Traditional Chinese detached layout checks, self-contained SQLite package checks, and installation/reinstallation/uninstallation checks in a disposable Windows runner. See the matching [GitHub Actions run](https://github.com/yikeugene/Penroam/actions) for the published build's results.

Detached rendering does not establish live keyboard focus, IME, pen pressure, touch or palm rejection. Physical pen/touch validation remains outstanding. Native Windows dialog buttons follow the Windows language; upstream release notes and technical error details may retain their original language.
