# SystemCleaner Architecture Reference

## Solution At A Glance

| Project | Target | Purpose |
| --- | --- | --- |
| `SystemCleaner.Core` | `net9.0` | Domain library that knows how to discover clutter, execute cleanup, inspect startup items, and drive uninstall logic. |
| `SystemCleaner.App` | `net9.0-windows` (WPF) | Desktop client that presents the UI, wires services together, persists settings, and hosts background monitors. |
| `SystemCleaner.Tests` | `net9.0` | xUnit-based regression coverage. Currently exercises the directory cleanup module end to end. |

Supporting artifacts:

- `global.json` pins the .NET SDK Major/Minor used in CI.
- `.vscode/tasks.json` exposes `build` and `test` tasks that map to `dotnet build` and `dotnet test`.
- GitHub Actions workflow `CI` produces the required `build-and-test` status check enforced by branch protection.

## Runtime and Build Requirements

- Windows 10/11 because the app depends on WPF, registry APIs, Volume Shadow Copy restore points, and LibreHardwareMonitor sensors.
- .NET SDK 9.0 (matching `global.json`).
- Packages used at runtime: `LibreHardwareMonitorLib` for hardware telemetry, `System.Management` for WMI queries, `System.ServiceProcess.ServiceController` for residual service cleanup, standard WPF libraries.
- Test-only packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, and `coverlet.collector`.

## High-Level Execution Flow

1. `App.xaml.cs` boots WPF, attaches global exception logging handlers, and builds the object graph manually (no DI container).
2. `CleanupModuleCatalog.CreateDefaultModules()` constructs the default set of `ICleanupModule` implementations.
3. `MainWindow` is created with a `MainViewModel` instance as its `DataContext`. The view model receives all core services.
4. The view model populates observable collections for modules, summaries, startup entries, uninstaller data, and VirusTotal state, wiring commands for every button exposed in XAML.
5. Views (`OverviewPage`, `CleaningPage`, etc.) bind to `MainViewModel` and child view-models. Converters and utilities shape the UI output (size formatting, arc geometries, visibility toggles).
6. User actions (scan, clean, quick clean, uninstall) flow through `RelayCommand` into asynchronous methods that call the domain layer (`CleanupService`, `StartupDiscoveryService`, `UninstallerService`).
7. Results, log entries, and status/usage telemetry bubble back up to the UI via observable collections.

## Core Domain (SystemCleaner.Core)

### Abstractions

- `ICleanupModule` defines the contract every cleanup module must follow (`ScanAsync`, `CleanAsync`, and metadata exposure via `CleanupModuleInfo`).

### Models

- `CleanupModuleInfo` describes a module (id, display name, description, quick-clean safety, and optional warning copy).
- `CleanupScanResult` wraps the items a module found along with aggregate size.
- `CleanupItem` represents one candidate (directory, file, registry residual, etc.) and carries owning module metadata.
- `CleanupExecutionResult` reports the outcome for a module (item-level results, bytes freed, issues).
- `CleanupBatchResult` aggregates the execution results from the entire cleanup batch.
- `CleanupProgressUpdate` is raised during scan/clean operations to update status text.
- `CleanupItemResult` stores success status, bytes freed, and issues for a single item.
- `CleanupItemType` enum differentiates directories, files, registry items, services, tasks, and drivers.
- Startup models (`StartupEntry`, `StartupEntryKind`, `StartupDiscoveryResult`) encapsulate autorun discovery.
- Uninstall models (`InstalledApplication`, `InstalledSoftwareSnapshot`, `BrowserExtensionInfo`, `ResidualItem`, etc.) describe installed software, extension inventory, residual traces, and cleanup options.

### Cleanup Modules

Modules are created through `CleanupModuleCatalog` and implement `ICleanupModule`:

- `DirectoryCleanupModule` (paired with `DirectoryCleanupRule`) scans known directories (temp, browser caches, diagnostic folders) and deletes their contents using `FileSystemHelper.CleanDirectoryItem`.
- `LargeFileCleanupModule` (with `LargeFileScanRule`) enumerates common folders for large files above thresholds, limits results, and deletes selected files.
- `DuplicateCleanupModule` (with `DuplicateScanRule`) hashes files by size, then SHA-256 to find duplicates and offers surplus copies for deletion.
- Catalog rules are built dynamically based on environment paths (e.g., multiple browser profiles, both 32-bit/64-bit temp locations).

### Services

- `CleanupService` orchestrates scanning all registered modules and performing grouped cleanups. Progress is surfaced via `CleanupProgressUpdate`.
- `StartupDiscoveryService` reads Windows registry run/runonce keys and startup folders (32-bit and 64-bit hives) to surface autorun entries, their approval state, and supports toggling enablement.
- `UninstallerService` inventories software (registry providers), browser extensions, calculates health insights, launches uninstall commands, optionally creates system restore points, and performs residual file/registry/service/task/driver cleanup via specialized handlers.
- Residual cleanup uses scanners and handlers (e.g., `FileResidualScanner`, `ServiceResidualScanner`, `ScheduledTaskCleanupHandler`) defined within `SystemCleaner.Core.Uninstall`.

### Utilities

- `FileSystemHelper` calculates directory sizes, deletes files/directories safely (handling read-only flags), and returns detailed issue lists.
- Startup helpers process registry access and approval states robustly (exception handling and reporting).

## Application Layer (SystemCleaner.App)

### Composition Root

- `App.xaml` defines merged theme dictionaries; `App.xaml.cs` wires services together and shows `MainWindow`.
- Crash handling routes all unhandled exceptions through `DiagnosticLogger`, displaying a message box with the written log path.

### Services and Infrastructure

- `ThemeService` swaps between Light, Dark, or System themes by swapping resource dictionaries and reading Windows personalization registry settings.
- `HardwareMonitorService` wraps `LibreHardwareMonitorLib` to poll hardware every ~2 seconds, raising `HardwareSnapshot` events (CPU/GPU usage, temps, storage health).
- `UserConfirmationService` centralizes confirmation prompts, with an opt-out flag bound to the UI settings toggle.
- `AppSettingsService` persists `AppSettings` to `%AppData%\SystemCleaner\settings.json`, using a semaphore for thread safety and logging failures.
- `VirusTotalService` manages API key storage (in-memory), quota fetching (`groups/self`), file/url submission, polling analyses, fallback enrichment, and quota updates. Models in `VirusTotalModels.cs` map API responses.
- `DiagnosticLogger` writes per-day log files inside `%LocalAppData%\SystemCleaner\logs` for crash and info tracking.

### ViewModels and Command Surface

Central orchestrator:

- `MainViewModel` (≈850 lines) holds module collections, commands (`ScanCommand`, `CleanCommand`, `QuickCleanCommand`, etc.), startup/uninstaller sub-viewmodels, VirusTotal state, log history, theme selection, and overall status text. It coordinates restore point creation, quick clean workflow, and hardware monitor subscription.

Supporting viewmodels:

- `CleanupModuleViewModel`, `CleanupItemViewModel`, and `ModuleSummaryViewModel` expose module metadata, selection state, and total/selected sizes for the cleanup tabs.
- `SystemUsageViewModel` and `SystemInfoViewModel` surface hardware telemetry and OS info (with sample data fallback for design-time/testing).
- `StartupManagerViewModel` wraps `StartupDiscoveryService`, exposing enable/disable commands and issue tracking.
- `UninstallerViewModel` uses `UninstallerService` to list software, trigger removals, show insights, and launch residual cleanup.
- VirusTotal viewmodels (`VirusTotalViewModel`, `VirusTotalSubmissionViewModel`, `VirusTotalEngineResultViewModel`) manage submission history, engine verdicts, and quota display.
- `LogEntryViewModel` keeps the UI log feed bounded (`MaxLogEntries = 200`).
- `ObservableObject` implements property-change notification; `RelayCommand` centralizes `ICommand` logic for buttons, enabling/disabling based on `CanExecute` delegates.

### Views and UI Layout

- `MainWindow.xaml` defines the frameless window chrome, title bar buttons, theming toggles, and tab host.
- Individual pages replace the main content based on `MainTab` (enum):
  - `OverviewPage` shows last scan summary, quick clean card, system usage graphs (CPU/GPU arcs), and recent activity.
  - `CleaningPage` renders module lists, selection grids, and action toolbar (scan, clean, select/deselect all, open location).
  - `StartupPage` lists autorun entries with toggle buttons and opens Explorer/Registry.
  - `UninstallerPage` displays installed software, insights, and residual cleanup controls.
  - `SystemInfoPage` surfaces hardware snapshot data, storage health, OS build info.
  - `VirusTotalPage` handles file/url submissions, result tables, engine verdict breakdown, and quota chip.
  - `SettingsPage` exposes confirmation toggles, theme mode, VirusTotal API key entry, and export options.

Data templates and styles are centralized in `Themes/LightTheme.xaml` and `Themes/DarkTheme.xaml`, with supporting brushes referenced across views.

### Converters and Utilities

- `NullToVisibilityConverter` hides UI elements when bound values are null/empty.
- `PercentToActualWidthConverter` and `UsagePercentToArcGeometryConverter` translate usage percentages into bar widths or arc geometries for gauges.
- `SizeFormatter` prints byte counts with human-readable suffixes used throughout summaries and module lists.

### Buttons and Commands

Every button defined in XAML binds to a `RelayCommand` instance in `MainViewModel` or subordinate viewmodels. Key interactions include:

- `ScanCommand` / `QuickCleanCommand` / `CleanCommand` on Overview and Cleaning pages.
- Select-all/deselect-all toolbar buttons per module list.
- `OpenItemLocationCommand` opens Explorer on the selected path.
- Per-entry commands in Startup and Uninstaller pages toggle enablement or launch uninstall/residual cleanup flows.
- `ChangeTabCommand` drives tab switching tied to title-bar navigation buttons.
- Theme toggle uses `IsDarkTheme` binding updated via `ThemeService`.

## Data Persistence and Logging

- App settings persist to JSON under `%AppData%` and include theme preference, confirmation flag, and stored VirusTotal API key.
- Diagnostic logs write to `%LocalAppData%` daily files whenever unhandled exceptions occur or services log informational events.
- No relational database is involved; runtime data is discovered on demand and kept in memory.

## External Integrations

- LibreHardwareMonitor sensors require elevated process access on some hardware; the service polls CPU, GPU, and storage metrics.
- Windows System Restore APIs are invoked before uninstall/cleanup when configured.
- Windows Registry and Task Scheduler APIs are used through `Microsoft.Win32` and WMI.
- VirusTotal REST API endpoints `files`, `urls`, `analyses`, and `groups/self` are used with bearer-less `x-apikey` header.

## Testing Surface

- `DirectoryCleanupModuleTests` (xUnit) verifies scan counts, size aggregation, and file deletion for the directory module. Use `dotnet test SystemCleaner.sln` to execute.
- Additional tests can follow the pattern to cover `LargeFileCleanupModule`, `DuplicateCleanupModule`, service orchestration, and viewmodel logic (via headless unit tests).

## Suggested Next Enhancements

- Expand the test project to cover large-file and duplicate modules, plus service-layer edge cases (restore point failures, residual cleanup handlers).
- Introduce dependency injection to simplify wiring in `App.xaml.cs` and enable mock injection for unit tests.
- Persist VirusTotal API key securely (Windows Credential Locker) instead of plaintext JSON.
- Add telemetry to surface residual cleanup outcomes in the UI (e.g., toast notifications).
- Provide localization support by moving hard-coded strings into resource dictionaries.
