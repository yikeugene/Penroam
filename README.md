# Penroam

An offline handwriting notebook for Windows 11. Penroam, formerly Moye, combines pressure-sensitive ink, PDF annotation, typed notes, images and study tools in a native WPF app designed for a touchscreen laptop and an active pen.

[Downloads and releases](https://github.com/yikeugene/Penroam/releases) · [User guide](docs/USER_GUIDE.md) · [File formats](docs/FILE_FORMAT.md) · [Contributing](https://github.com/yikeugene/Penroam/blob/master/CONTRIBUTING.md) · [Agent guide](https://github.com/yikeugene/Penroam/blob/master/AGENTS.md)

## Get started

1. Download [Penroam-1.3.0-Setup-win-x64.exe](https://github.com/yikeugene/Penroam/releases/download/v1.3.0/Penroam-1.3.0-Setup-win-x64.exe) from the [Releases page](https://github.com/yikeugene/Penroam/releases).
2. Run the installer, then open the **Penroam** desktop or Start menu shortcut. The .NET runtime is included; no separate runtime installation or administrator account is needed.
3. Choose **New Notebook**, select a paper template and start writing. A mouse works too; pressure input requires a Windows Ink-compatible active pen.

Penroam requires **Windows 11 x64**. Writing and saving work without an account or an internet connection. The installer is currently unsigned; release assets include SHA-256 checksums.

**Before upgrading an existing library, keep a backup made by your current app.** Penroam 1.3 retains **SQLite schema 5** and **`.moye` backup format 5** from Penroam 1.2, and reads backup formats 1–5. Penroam 1.1.0 and earlier cannot open the newer formats. Uninstalling does not reverse a migration. See [compatibility](docs/FILE_FORMAT.md#compatibility).

Version **1.3.0** adds folder import into notebook sections and an editable two-page view. See the [current release notes](docs/RELEASE_NOTES_1.3.0.md); historical releases are listed on [GitHub Releases](https://github.com/yikeugene/Penroam/releases).

## Features

- **Interface languages:** switch between English and Traditional Chinese (繁體中文) in the notebook home header, without restarting. The choice is remembered on this device; notebook contents and names stay unchanged.
- **Handwriting:** pressure-sensitive pen and highlighter, saved pen presets, draw-and-hold straight lines, pixel/stroke erasers, mixed ink/text/image lasso selections and notebook undo/redo.
- **Flexible pages:** Blank, Ruled, Grid, Dot Grid, Cornell, Graph and Meeting paper. Extend any paper or PDF page on any side without scaling the original content.
- **Typed notes and images:** free typing with ruled alignment, fixed text boxes, whole-box formatting, PNG/JPEG images and pasted screenshots. Move overflowing text to a new page explicitly.
- **Organization:** notebooks, sections, page titles and bookmarks; pins and cover colors; batch page reordering, duplication and cross-notebook move/copy.
- **Search and study:** local typed/PDF-text search, visual search by color and position, anchored questions, saved next-step hints, separate practice attempts and regional restore from the current editing session.
- **Document workflows:** preview and choose PDF or locally converted Office pages before import, or import a folder of PDF/PPTX files as notebook sections. Export a page, section, notebook or selected pages, using original sizes or actual-size A4 tiling.
- **Workspace:** remembered reading position, editable single/two-page layouts, Fit Page/Fit Spread/Fit Width, focus mode, optional two-finger navigation and zoom lock, and a read-only reference pane.
- **Backup and recovery:** editable `.moye` backups, optional local automatic backups while the app is open, best-effort interrupted-draft recovery and recently deleted notebook copies.

See the [user guide](docs/USER_GUIDE.md) for commands, keyboard shortcuts and limits. The [roadmap](ROADMAP.md) contains future ideas, not a list of implemented capabilities. Infinite canvas, cloud sync, recording, handwriting recognition and AI features are not implemented.

## Your notes stay local

The default library is `%LOCALAPPDATA%\Moye\moye.db`. The Penroam rename did not move the library, preferences or recovery files, or change the `.moye` extension. Do not copy only the database or remove its SQLite journal files while the app is open.

Use **More → Back Up All Notebooks** to make a portable editable backup. Restoring creates new notebook copies without overwriting existing ones. Backups preserve ink, text, images, original PDFs and notebook organization; **they are not encrypted**.

Writing settings live in `writing-preferences.json`; interface language, reading positions, workspace options and backup settings live in `workspace-preferences.json`, alongside the database. These device-local preferences are not included in notebook backups. Recovery drafts depend on assets in the local library and are not portable backups. See [autosave and backups](docs/USER_GUIDE.md#autosave-and-backups).

## Install, update and uninstall

New installations default to `%LOCALAPPDATA%\Programs\Penroam`. Upgrades recognize existing Moye installations and may retain their previous application folder. Removing Penroam through Windows Settings removes program files and shortcuts but retains notebook data.

Normal launches check GitHub for newer stable releases. **Update now** downloads and verifies the installer checksum, saves pending notes and closes the app for installation. Manual checks are available through **More → Check for Updates…** or **Library Options → Check for Updates…**. Update requests include the app version, not notebook content. Launching with `--data-dir` disables both startup and manual update checks.

Install 1.3.0 manually when upgrading from 1.1.0 or earlier. Closing the app and running a newer installer remains a manual fallback. See [in-app updates](docs/USER_GUIDE.md#in-app-updates) for cancellation, retry and installation behavior.

## Build from source

Use PowerShell on Windows, from the project root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

The scripts prefer `.tools\dotnet\dotnet.exe`, then an installed .NET 10 SDK. If neither is available, `build.ps1 -InstallSdk` downloads Microsoft's official installer and installs the SDK under `.tools`. Initial installation, package restore and the first self-contained publish require internet access. CLI and NuGet caches stay in `.tools`; scripts do not change PowerShell security settings. Add `-NoRestore` only after the required packages and runtime packs are available.

| Command | Purpose |
|---|---|
| `build.ps1 -Configuration Release` | Build the Release configuration. |
| `test.ps1 -Filter 'FullyQualifiedName~StorageTests'` | Run selected tests; TRX output goes to `artifacts/TestResults`. |
| `preview-ui.ps1` | Render real WPF layouts with synthetic content; see the [preview tool guide](https://github.com/yikeugene/Penroam/blob/master/tools/Moye.UiPreview/README.md). |
| `publish.ps1` | Build the self-contained Windows x64 payload, portable ZIP and checksum under `artifacts`. |
| `publish-installer.ps1 -InstallCompiler` | Build the installer and checksum, installing the pinned Inno Setup compiler if needed. |
| `test-package.ps1 -PackagePath artifacts/Penroam-win-x64` | Check SQLite save/reopen in the actual packaged application with synthetic data. |
| `test-installer.ps1 -AllowDesktopChanges` | Install/reinstall/uninstall smoke test; run only in a disposable Windows account. |

All commands in the table are scripts under `scripts/`. For interactive development, launch the built `Penroam.exe` with `--data-dir .\sample-library` rather than using your real notebook library.

See [CONTRIBUTING.md](https://github.com/yikeugene/Penroam/blob/master/CONTRIBUTING.md) for development and release checks, and [AGENTS.md](https://github.com/yikeugene/Penroam/blob/master/AGENTS.md) for architecture, compatibility constraints and agent workflow. Building a package does not publish a release. Updater-compatible releases require a matching stable `vMAJOR.MINOR.PATCH` tag and the exact installer and companion checksum produced by the scripts.

## Verification and limitations

Use the [current release notes](docs/RELEASE_NOTES_1.3.0.md) and [GitHub Actions](https://github.com/yikeugene/Penroam/actions) for verification tied to a published build. Historical test counts do not establish the status of the current source.

- Managed tests, detached WPF renders, packaged storage checks and installer checks cover different things. A detached render does not verify live keyboard focus, popups, IME composition, pen pressure, touch or palm rejection.
- Physical pen/touch behavior, broader IME sessions, printer output and application-control compatibility require testing on the target device. Do not infer acceptance from a mouse test or successful CI run.
- Office import requires locally installed Word, PowerPoint or LibreOffice, depending on the file type. Converted pages are fixed PDF backgrounds, not editable Office documents. See [Office import](docs/USER_GUIDE.md#office-document-import).
- Encrypted PDFs, interactive forms, digital signatures and unsupported interactive annotations are not supported. Export preserves supported annotation appearances and ordinary URI links but removes internal destinations and interactive actions.
- Added typed notes export as vector outlines, not selectable/searchable PDF text. Keep `.moye` backups for editable content. Local PDF search does not recognize handwriting or image-only scans.
- Recovery is best effort; it cannot guarantee that the final stroke, active text composition or failed disk write reached storage. Keep backups on another storage location.

## License

Penroam's original code is available under the [MIT License](LICENSE). Third-party components retain their own licenses; see [Third-party notices](docs/THIRD-PARTY-NOTICES.md). Published packages include the applicable runtime and dependency notices.
