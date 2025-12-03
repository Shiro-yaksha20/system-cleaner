# Error Updates

## Compile Errors (IDE/Build)

### 1. SettingsPage.xaml.cs – XAML partial class not generated
**File:** `SystemCleaner.App/Views/SettingsPage.xaml.cs`
**Lines:** 11, 18, 29, 33, 44
**Error:** `InitializeComponent()` and `ApiKeyBox` do not exist in the current context.
**Root cause:** The XAML designer reports the code-behind as a partial class that should be auto-generated from `SettingsPage.xaml`. If the XAML file's `x:Class` directive or build action is incorrect, the generated partial class is missing.
**Fix suggestions:**
- Ensure `SettingsPage.xaml` has Build Action = `Page` in the project file.
- Confirm `x:Class="SystemCleaner.App.Views.SettingsPage"` matches the namespace/class declaration.
- Rebuild the solution to regenerate `obj\SettingsPage.g.cs`.

### 2. VirusTotalPage.xaml.cs – XAML partial class not generated
**File:** `SystemCleaner.App/Views/VirusTotalPage.xaml.cs`
**Line:** 11
**Error:** `InitializeComponent()` does not exist in the current context.
**Root cause:** Same as SettingsPage—XAML code-gen is not producing the expected partial class.
**Fix suggestions:**
- Verify build action and `x:Class` as above.
- If the problem persists, close VS Code, delete `obj` and `bin` folders, and rebuild.

---

## Architectural Issues (No Compiler Error, But Risky/Incomplete)

### 3. No path deny-list in FileSystemHelper
**Location:** `SystemCleaner.Core/Utilities/FileSystemHelper.cs`
**Issue:** The cleanup helpers can theoretically delete any path passed in. There is no built-in guard against dangerous locations like `C:\Windows\System32`, `WinSxS`, or `C:\Windows\Installer`.
**Fix suggestions:**
- Add a static deny-list of critical system paths.
- Refuse to delete any path that starts with or equals an entry in the deny-list.
- Log a warning when a path is skipped for safety.

### 4. No junction/reparse-point detection
**Location:** `FileSystemHelper.cs`, cleanup modules
**Issue:** If a folder is a junction pointing to a system location (or another drive), the cleaner will follow it and potentially delete external data.
**Fix suggestions:**
- Check `FileAttributes.ReparsePoint` before traversing or deleting a directory.
- Skip reparse points by default or require explicit user opt-in.

### 5. VirusTotalService has no client-side rate limiting
**Location:** `SystemCleaner.App/Services/VirusTotalService.cs`
**Issue:** Public VirusTotal API allows only 4 requests/minute. A burst of user clicks can exhaust the quota instantly and return HTTP 429 errors, which are not handled gracefully.
**Fix suggestions:**
- Add a simple token-bucket or timestamp-based rate limiter.
- Implement exponential backoff when 429 is received.
- Surface "waiting for quota" status in the UI instead of hard errors.

### 6. VirusTotal API key stored in plaintext JSON
**Location:** `SystemCleaner.App/Settings/AppSettingsService.cs` → `settings.json`
**Issue:** The API key is persisted in a readable JSON file under `%AppData%`. Any local process can read it.
**Fix suggestions:**
- Use Windows Credential Locker (`PasswordVault`) or DPAPI encryption to protect sensitive credentials.
- Store only a reference in `settings.json` indicating that a key exists.

### 7. `async void` event handler in MainWindow
**File:** `SystemCleaner.App/MainWindow.xaml.cs`, line 21
**Issue:** `async void` swallows exceptions and cannot be awaited. If `InitializeAsync` throws, the error escapes without proper handling.
**Fix suggestions:**
- Wrap the body in try/catch with error logging or reporting.
- Consider routing the async initialization through a command or state machine that can surface failures to the UI.

### 8. DataGrid in UninstallerPage lacks row virtualization
**File:** `SystemCleaner.App/Views/UninstallerPage.xaml`, DataGrid starting line 77
**Issue:** For large software lists (hundreds of items), absence of explicit `EnableRowVirtualization="True"` and `VirtualizingPanel.VirtualizationMode="Recycling"` can cause UI lag.
**Fix suggestions:**
- Add `EnableRowVirtualization="True"` and `VirtualizingPanel.VirtualizationMode="Recycling"` to each DataGrid.
- Confirm existing filtering is done via `ICollectionView`, not by regenerating collections.

### 9. No unified error notification service
**Location:** Across viewmodels
**Issue:** Errors surface via scattered `MessageBox.Show` calls and per-viewmodel `StatusMessage` properties. This makes consistent UX difficult and testing harder.
**Fix suggestions:**
- Introduce an `IErrorNotifier` or `INotificationService` abstraction.
- Route all errors to a centralized toast/banner component.
- Replace direct `MessageBox` calls in viewmodels.

### 10. Custom RelayCommand does not integrate with CommandManager
**File:** `SystemCleaner.App/ViewModels/RelayCommand.cs`
**Issue:** The command raises its own `CanExecuteChanged` via manual `RaiseCanExecuteChanged()` calls. This works but forces many manual call sites. Omitting a call leaves UI buttons in stale states.
**Fix suggestions:**
- Consider adopting `CommunityToolkit.Mvvm`'s `RelayCommand` with source generators.
- Or hook into `CommandManager.RequerySuggested` for automatic invalidation (with awareness of perf implications).

### 11. MainViewModel is ~850+ lines ("god viewmodel")
**File:** `SystemCleaner.App/ViewModels/MainViewModel.cs`
**Issue:** The class handles cleanup, startup, uninstall orchestration, settings loading, hardware monitoring, VirusTotal coordination, logging, restore points, and tab navigation. This violates single-responsibility and hampers testing.
**Fix suggestions:**
- Extract dedicated viewmodels for cleanup dashboard, settings, hardware monitoring, etc.
- Let `MainViewModel` coordinate navigation and hold child viewmodels, not business logic.

### 12. Test coverage limited to DirectoryCleanupModule
**File:** `SystemCleaner.Tests/UnitTest1.cs`
**Issue:** Only one module is tested. `LargeFileCleanupModule`, `DuplicateCleanupModule`, service layer, and viewmodel command logic are not covered.
**Fix suggestions:**
- Add tests for large-file and duplicate modules using controlled test folders.
- Mock file system/registry/HTTP for service tests.
- Unit-test viewmodel commands (busy flag, status messages, selection logic).

### 13. No dry-run / preview mode for cleanup
**Location:** Cleanup modules and UI
**Issue:** Users cannot see what will be deleted before committing. Builds distrust and risk of accidental data loss.
**Fix suggestions:**
- Add a `DryRun` flag to `CleanAsync` or a separate `PreviewAsync` method.
- Display the preview in the UI with a confirmation step before actual deletion.

### 14. Logging lacks correlation IDs and configurable levels
**File:** `SystemCleaner.App/Services/DiagnosticLogger.cs`
**Issue:** Each log entry is isolated. No batch/correlation ID links related entries across a scan or cleanup run. There is no way to enable verbose logging for troubleshooting.
**Fix suggestions:**
- Add a correlation ID parameter and include it in all log calls within a single operation.
- Introduce a log-level enum (Debug, Info, Warning, Error) and respect a user setting.

### 15. DuplicateCleanupModule does not preserve per-volume originals
**File:** `SystemCleaner.Core/Modules/DuplicateCleanupModule.cs`
**Issue:** The module keeps the first file per hash group but does not ensure at least one copy remains on each physical volume if duplicates span drives.
**Fix suggestions:**
- Group duplicates by volume/drive letter before auto-selecting which to keep.
- Add policy options (keep oldest, keep in Program Files, etc.) configurable via settings.

---

## Summary
| Category | Count |
|----------|-------|
| Compile errors | 2 issues (both XAML code-gen related) |
| Safety/Security | 4 issues (deny-list, junctions, VT rate limit, API key storage) |
| UX/Performance | 3 issues (async void, virtualization, error notifications) |
| Architecture/Maintainability | 4 issues (god viewmodel, custom commands, test coverage, logging) |
| Feature gaps | 2 issues (dry-run, duplicate volume handling) |

**Total: 15 identified issues**
