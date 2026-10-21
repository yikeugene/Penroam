# Agent guide for Penroam

This guide applies to the entire source tree. Use the current code and project files as the source of truth; historical release notes and unchecked roadmap items are not evidence of current functionality.

## Project essentials

- Penroam is a Windows 11 x64 handwriting notebook built with C# and WPF on .NET 10. The target framework is `net10.0-windows10.0.26100.0`.
- The product and executable are **Penroam** / `Penroam.exe`; project paths and the root namespace remain **Moye**. Do not perform a global rename.
- There is no root solution file. Use the scripts below or explicitly name a `.csproj`; do not assume that root-level `dotnet build` will select a project.
- Runtime dependencies include Microsoft.Data.Sqlite, SQLitePCLRaw, PDFsharp-WPF and PdfPig. Read [Moye.csproj](src/Moye/Moye.csproj) for current package and application versions.
- Notes can be edited and saved offline. Normal launches may contact GitHub for updates; do not describe the application as never using the network.

## Code map

| Area | Entry points and responsibilities |
|---|---|
| Startup | [App.xaml.cs](src/Moye/App.xaml.cs): library selection, single-instance identity, service setup and special command-line modes. |
| Window and resources | [MainWindow.xaml](src/Moye/MainWindow.xaml), [MainWindow.xaml.cs](src/Moye/MainWindow.xaml.cs), [App.xaml](src/Moye/App.xaml). Feature-specific behavior lives in `MainWindow.*.cs` partial files. |
| View models and history | [MainViewModel.cs](src/Moye/ViewModels/MainViewModel.cs) and its partials coordinate documents, navigation and services; [NotebookHistory.cs](src/Moye/ViewModels/NotebookHistory.cs) manages bounded session history. |
| Document model | [DocumentModels.cs](src/Moye/Models/DocumentModels.cs): notebooks, sections, ordered pages, text, images, PDF placement and serialized fields. |
| Editing and input | [PageEditor.cs](src/Moye/Controls/PageEditor.cs), its partials, [PenInkCanvas.cs](src/Moye/Controls/PenInkCanvas.cs) and [PaperVisual.cs](src/Moye/Controls/PaperVisual.cs). The editor uses fixed page coordinates; the window owns zoom and page virtualization. |
| Persistence and safety | [SqliteNotebookRepository.cs](src/Moye/Services/SqliteNotebookRepository.cs), [AutosaveCoordinator.cs](src/Moye/Services/AutosaveCoordinator.cs), [BackupService.cs](src/Moye/Services/BackupService.cs), plus preferences and recovery services. |
| Documents and updates | [PdfService.cs](src/Moye/Services/PdfService.cs), [DocumentImportService.cs](src/Moye/Services/DocumentImportService.cs), [PdfTextExtractionService.cs](src/Moye/Services/PdfTextExtractionService.cs), [GitHubUpdateService.cs](src/Moye/Services/GitHubUpdateService.cs). |
| Verification and packaging | [Moye.Tests.csproj](tests/Moye.Tests/Moye.Tests.csproj), [preview tool](tools/Moye.UiPreview/README.md), `scripts/`, [Moye.iss](installer/Moye.iss) and [Windows CI](.github/workflows/windows.yml). |

Follow the existing WPF/code-behind and view-model boundaries rather than imposing a new architecture. Place changes in the relevant partial/service, match surrounding C# style and comment density, and keep unrelated refactors out of a focused fix. Keep interface strings in the shared English and Traditional Chinese localization resources, and user documentation in natural English; never translate a user's notebook content.

## Build and test

Run these commands in PowerShell from the project root, on Windows:

```powershell
.\scripts\build.ps1 -Configuration Release
```

```powershell
.\scripts\test.ps1 -Configuration Release
```

The scripts prefer `.tools/dotnet/dotnet.exe`, then an installed .NET 10 SDK. If the SDK is missing, `build.ps1 -InstallSdk` downloads Microsoft's official installer. Tools and caches live under `.tools`; initial restore requires network access. Do not weaken PowerShell or Windows security settings to make a command run.

| Task | Command |
|---|---|
| Targeted storage tests | `.\scripts\test.ps1 -Filter 'FullyQualifiedName~Moye.Tests.StorageTests'` |
| Detached WPF layout checks | `.\scripts\preview-ui.ps1 -Configuration Release` |
| Self-contained payload and portable ZIP | `.\scripts\publish.ps1` |
| Installer and SHA-256 checksum | `.\scripts\publish-installer.ps1 -InstallCompiler` |
| Packaged SQLite smoke test | `.\scripts\test-package.ps1 -PackagePath artifacts/Penroam-win-x64` |

Use `-NoRestore` only after the required packages/runtime packs have been restored. The preview script restores from the existing local cache; build/test first if dependencies are missing. Tests write TRX results to `artifacts/TestResults`, and preview reports/images go to `artifacts`.

The single test project uses xUnit. Tests are grouped by behavior, including storage, backups, ink, typing, PDF, organization, recovery and updates. Follow existing STA-thread helpers for WPF/ink tests; these are not cross-platform tests. Use synthetic fixtures and temporary directories, not real notebook content.

## Safe interactive verification

After a Debug build, launch an isolated library rather than the default user library:

```powershell
.\src\Moye\bin\Debug\net10.0-windows10.0.26100.0\Penroam.exe --data-dir .\sample-library
```

`--data-dir` keeps the library/preferences in the selected directory and disables startup/manual updater checks. The preview tool also supports an explicit `--interactive` memory-only fixture; follow its [guide](tools/Moye.UiPreview/README.md). Close test application windows before rebuilding their output directories.

- For behavior changes, add a meaningful regression test, run the relevant tests and then the full suite when feasible.
- For UI changes, run the detached preview and check the changed interaction in a real desktop window. Preserve accessible labels, visible keyboard focus and the existing 44-DIP touch-target checks.
- Detached layout rendering does not validate live focus, popups, clipboard, IME composition, pen pressure, touch or palm rejection. Report the actual checks performed and leave untested device behavior explicitly unverified.
- `scripts/test-installer.ps1 -AllowDesktopChanges` installs, reinstalls and uninstalls the real app, changing shortcuts and registration. Run it only with explicit authorization in a disposable Windows account, never as an ordinary unit test.
- Package creation is not release publication. Do not upload releases, install updates or change the user's real notebooks as an incidental verification step.

## Data and compatibility invariants

1. **Keep legacy identities stable.** The default database remains `%LOCALAPPDATA%\Moye\moye.db`; backups use `.moye` with manifest identifier `moye`. Preserve the canonical library mutex identity and the installer AppId/AppUserModelID `net.yikeugene.moye`. These allow existing installations and libraries to keep working.
2. **Version persistent changes deliberately.** Current SQLite schema and backup format are **5**; backups read formats **1–5**. Application, database, backup and preference versions are independent. Preserve persisted enum values and legacy defaults; reject unknown/newer formats rather than silently dropping fields. Update [FILE_FORMAT.md](docs/FILE_FORMAT.md) and migration/round-trip tests when changing formats.
3. **Ink arrays are immutable snapshots.** Replace `InkData` rather than mutating bytes in place: history shares these arrays. Preserve independent snapshots across asynchronous save/export work, including study and practice metadata.
4. **Preserve commit boundaries.** Completed edits update the document, notebook history and autosave. Commit pending editor input before page detachment, notebook changes and close. Failed saves must retain pending content; normal close must not silently discard it. Cross-notebook transfers/restores must retain their atomic transaction behavior.
5. **Keep page geometry stable.** Stored coordinates are 96-DPI DIP, not screen pixels after zoom. Page extension shifts content and anchors without rescaling the original PDF or paper guides. Keep editor, thumbnails, reference views, persistence, backups and PDF export consistent.
6. **Keep source assets intact.** Images and PDFs are content-addressed original assets. Office import converts locally into a staged PDF without modifying the source. Preserve archive path/hash/size validation and external-resource restrictions; never weaken validation merely to accept a fixture.
7. **Distinguish export from backup.** PDF sharing is not an editable notebook backup. Export works from a snapshot and replaces its destination only after success. Backup restoration creates fresh-ID copies instead of overwriting existing notebooks.
8. **Protect preferences and recovery.** Writing/workspace preferences are device-local and excluded from notebook backups. Preserve unreadable/newer settings rather than overwriting them. Recovery drafts depend on library assets and are best effort, not guaranteed portable backups. Do not copy an active SQLite database without its journal or delete WAL/SHM files as a repair shortcut.

## Documentation and release hygiene

- [README.md](README.md): concise product overview and entry points. [USER_GUIDE.md](docs/USER_GUIDE.md): current workflows and limits. [FILE_FORMAT.md](docs/FILE_FORMAT.md): persistence and compatibility. [CONTRIBUTING.md](CONTRIBUTING.md): contribution/release workflow. This file: agent-facing navigation and constraints.
- [ROADMAP.md](ROADMAP.md) retains future work and acceptance criteria; it is not a feature-completion claim. Keep historical test counts and per-version change records out of the current overview. Older Moye and Penroam version numbers are separate release sequences.
- Delete documentation only after checking its content and incoming references. Do not remove unique migration history, unfinished requirements or licensing material merely because it is old. Update existing canonical documents instead of creating duplicate progress reports or extra agent guides.
- `scripts/publish.ps1` uses an explicit documentation whitelist and requires `docs/RELEASE_NOTES_<Version>.md`, where `Version` comes from the app project. Keep that file and the whitelisted README, roadmap, guides and notices available.
- `scripts/publish-installer.ps1` intentionally rejects `AGENTS.md` and internal QA/source files in the payload. Keep this guide source-only. For links from shipped docs to source-only developer material, use repository URLs rather than missing package-relative files.
- Preserve `LICENSE`, [third-party notices](docs/THIRD-PARTY-NOTICES.md) and `docs/licenses/`. Upstream license text must retain its original bytes; `.gitattributes` disables text normalization for those files. Respect LF for Markdown/C#/XAML and CRLF for PowerShell/Inno scripts.
- Keep generated output, `.tools`, `bin/obj`, local assistant/editor configuration, logs, test reports, databases, `.moye` backups and credentials out of source changes. Review logs for private paths/content before sharing them. Use [`.gitignore`](.gitignore) as the existing exclusion baseline.
- For updater-compatible releases, keep project version, stable `vMAJOR.MINOR.PATCH` tag, installer filename and companion checksum aligned. Preserve save-before-exit, checksum validation and installer wait/relaunch behavior. See [CONTRIBUTING.md](CONTRIBUTING.md) for packaging checks.
