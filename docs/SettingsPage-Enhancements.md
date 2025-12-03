# Settings Page Enhancement Recommendations

This document outlines potential new settings to add to the SystemCleaner Settings page, along with implementation guidance for each.

---

## 🎨 Appearance & UI

### 1. Accent Color Picker

**Description:** Allow users to choose their own accent color (blue, green, purple, red, orange, teal, etc.)

**Implementation:**
- **Model:** Add `AccentColor` property to `AppSettings.cs` (store as hex string like `#1E90FF`)
- **View:** Add a `ListBox` or `ComboBox` with colored swatches, or use a color picker control
- **Theme Service:** Update `ThemeService.cs` to dynamically set `AccentBrush`, `AccentHoverBrush`, `AccentSoftBrush` based on selected color
- **Resources:** In `LightTheme.xaml` / `DarkTheme.xaml`, the accent brushes are already DynamicResource, so runtime changes will propagate

**NuGet (optional):** Consider `Extended.Wpf.Toolkit` for a full `ColorPicker` control, or build a simple swatch grid

```csharp
// AppSettings.cs
public string AccentColor { get; set; } = "#1E90FF"; // Default blue
```

```csharp
// ThemeService.cs
public void ApplyAccentColor(string hex)
{
    var color = (Color)ColorConverter.ConvertFromString(hex);
    Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
    // Compute hover/soft variants by adjusting brightness
}
```

---

### 2. Font Size / UI Scale

**Description:** Small / Medium / Large text scaling for accessibility

**Implementation:**
- **Model:** Add `UiScale` enum (`Small`, `Medium`, `Large`) to `AppSettings.cs`
- **View:** ComboBox with three options
- **Theme Service:** Apply a base `FontSize` multiplier (e.g., 12/14/16) to `Application.Current.Resources["BaseFontSize"]`
- **XAML:** Ensure text elements bind to `{DynamicResource BaseFontSize}` or use relative sizing

```csharp
public enum UiScale { Small, Medium, Large }

// AppSettings.cs
public UiScale UiScale { get; set; } = UiScale.Medium;
```

---

### 3. Compact Mode

**Description:** Toggle denser UI layout with smaller padding and row heights

**Implementation:**
- **Model:** Add `bool IsCompactMode` to `AppSettings.cs`
- **Theme Service:** Swap resource values for `CardPadding`, `RowHeight`, `ButtonHeight`, etc.
- **XAML:** Use `{DynamicResource CardPadding}` instead of hardcoded values like `Padding="20"`

```csharp
public bool IsCompactMode { get; set; } = false;
```

---

### 4. Animations Toggle

**Description:** Disable animations for performance or accessibility (reduce motion)

**Implementation:**
- **Model:** Add `bool EnableAnimations` to `AppSettings.cs`
- **View:** CheckBox toggle
- **Code:** Check this flag before starting storyboards; use `Duration="0"` or skip entirely
- **Global:** Set `Timeline.DesiredFrameRateProperty` or conditionally apply styles without transitions

```csharp
public bool EnableAnimations { get; set; } = true;
```

---

## 🧹 Cleanup Behavior

### 5. Default Scan Modules

**Description:** Choose which cleanup modules are enabled by default when scanning

**Implementation:**
- **Model:** Add `List<string> DefaultEnabledModules` to `AppSettings.cs` (store module IDs)
- **View:** ListBox with checkboxes showing all available modules
- **ViewModel:** On scan start, pre-select modules based on this list
- **Core:** Module IDs already exist in `CleanupModule.Id`

```csharp
public List<string> DefaultEnabledModules { get; set; } = new()
{
    "temp-files", "recycle-bin", "browser-cache"
};
```

---

### 6. Skip Files Newer Than X Days

**Description:** Protect recently modified files from cleanup

**Implementation:**
- **Model:** Add `int SkipFilesNewerThanDays` to `AppSettings.cs` (0 = disabled)
- **View:** NumericUpDown or TextBox with validation
- **Core:** In `CleanupService`, filter out files where `File.GetLastWriteTime() > DateTime.Now.AddDays(-X)`

```csharp
public int SkipFilesNewerThanDays { get; set; } = 0; // 0 = disabled
```

---

### 7. Secure Delete (Shred)

**Description:** Overwrite deleted files instead of normal delete for privacy

**Implementation:**
- **Model:** Add `bool UseSecureDelete` to `AppSettings.cs`
- **Core:** Create `SecureDeleteService` that overwrites file bytes before deletion
- **Algorithm:** Single-pass zero-fill is fast; 3-pass DoD 5220.22-M for paranoid mode

```csharp
public bool UseSecureDelete { get; set; } = false;

// SecureDeleteService.cs
public static void SecureDelete(string path)
{
    var info = new FileInfo(path);
    var length = info.Length;
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
    var buffer = new byte[4096];
    // Pass 1: zeros
    for (long i = 0; i < length; i += buffer.Length)
        stream.Write(buffer, 0, (int)Math.Min(buffer.Length, length - i));
    stream.Flush();
    File.Delete(path);
}
```

---

### 8. Create Restore Point Before Clean

**Description:** Automatically create a Windows System Restore point before major operations

**Implementation:**
- **Model:** Add `bool CreateRestorePointBeforeClean` to `AppSettings.cs`
- **Core:** Use WMI or `SystemRestore` COM interop to create restore point
- **Requirement:** Requires admin elevation; show warning if not elevated

```csharp
public bool CreateRestorePointBeforeClean { get; set; } = false;

// RestorePointService.cs (requires admin)
public static void CreateRestorePoint(string description)
{
    var restorePoint = GetObject("winmgmts:\\\\.\\root\\default:SystemRestore");
    restorePoint.InvokeMethod("CreateRestorePoint", new object[] { description, 0, 100 });
}
```

**Alternative:** Use `SRSetRestorePoint` P/Invoke from `srclient.dll`

---

### 9. Exclude Paths

**Description:** User-defined folders/files to always skip during scans

**Implementation:**
- **Model:** Add `List<string> ExcludedPaths` to `AppSettings.cs`
- **View:** ListBox with Add/Remove buttons; use `FolderBrowserDialog` for folder picker
- **Core:** In each cleanup module, check if path starts with any excluded path before adding to results

```csharp
public List<string> ExcludedPaths { get; set; } = new();

// In CleanupModule scanning logic:
if (settings.ExcludedPaths.Any(ex => filePath.StartsWith(ex, StringComparison.OrdinalIgnoreCase)))
    continue;
```

---

## 🚀 Startup & Performance

### 10. Run at Windows Startup

**Description:** Launch SystemCleaner minimized when Windows boots

**Implementation:**
- **Model:** Add `bool RunAtStartup` to `AppSettings.cs`
- **Registry:** Write/remove from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **Value:** Path to executable with `--minimized` argument

```csharp
public bool RunAtStartup { get; set; } = false;

// StartupService.cs
public static void SetRunAtStartup(bool enable)
{
    using var key = Registry.CurrentUser.OpenSubKey(
        @"Software\Microsoft\Windows\CurrentVersion\Run", true);
    if (enable)
        key?.SetValue("SystemCleaner", $"\"{Process.GetCurrentProcess().MainModule.FileName}\" --minimized");
    else
        key?.DeleteValue("SystemCleaner", false);
}
```

---

### 11. Minimize to System Tray

**Description:** Keep running in background when window is closed

**Implementation:**
- **Model:** Add `bool MinimizeToTray` to `AppSettings.cs`
- **View:** Add `NotifyIcon` from `Hardcodet.NotifyIcon.Wpf` NuGet package
- **Window:** Override `OnClosing` to hide window instead of closing when setting is enabled
- **Tray Menu:** Show, Exit, Quick Clean options

```xml
<!-- NuGet: Hardcodet.NotifyIcon.Wpf -->
<tb:TaskbarIcon IconSource="/Assets/icon.ico"
                ToolTipText="SystemCleaner"
                DoubleClickCommand="{Binding ShowWindowCommand}">
    <tb:TaskbarIcon.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Show" Command="{Binding ShowWindowCommand}" />
            <MenuItem Header="Quick Clean" Command="{Binding Cleanup.QuickCleanCommand}" />
            <Separator />
            <MenuItem Header="Exit" Command="{Binding ExitCommand}" />
        </ContextMenu>
    </tb:TaskbarIcon.ContextMenu>
</tb:TaskbarIcon>
```

```csharp
public bool MinimizeToTray { get; set; } = false;
```

---

### 12. Auto-Scan on Launch

**Description:** Automatically start a scan when app opens

**Implementation:**
- **Model:** Add `bool AutoScanOnLaunch` to `AppSettings.cs`
- **ViewModel:** In `MainViewModel.InitializeAsync()`, check setting and invoke `Cleanup.ScanCommand`

```csharp
public bool AutoScanOnLaunch { get; set; } = false;

// MainViewModel.cs
public async Task InitializeAsync()
{
    // ... existing code ...
    if (_settingsService.Current.AutoScanOnLaunch)
        await _cleanup.ScanCommand.ExecuteAsync(null);
}
```

---

### 13. Hardware Monitor Refresh Rate

**Description:** How often to poll CPU/GPU/RAM metrics (1s, 2s, 5s, 10s)

**Implementation:**
- **Model:** Add `int HardwareMonitorIntervalMs` to `AppSettings.cs`
- **View:** ComboBox with preset options (1000, 2000, 5000, 10000)
- **Service:** Update `HardwareMonitorService` to use configurable interval

```csharp
public int HardwareMonitorIntervalMs { get; set; } = 2000; // Default 2 seconds

// HardwareMonitorService.cs
private readonly Timer _timer;
public void UpdateInterval(int ms) => _timer.Change(0, ms);
```

---

## 🔔 Notifications

### 14. Show Desktop Notifications

**Description:** Toast notifications for completed scans, errors, etc.

**Implementation:**
- **Model:** Add `bool ShowDesktopNotifications` to `AppSettings.cs`
- **NuGet:** Use `Microsoft.Toolkit.Uwp.Notifications` for Windows 10/11 toast notifications
- **Service:** Create `ToastNotificationService` that checks setting before showing

```csharp
public bool ShowDesktopNotifications { get; set; } = true;

// ToastNotificationService.cs
public void ShowToast(string title, string message)
{
    if (!_settings.ShowDesktopNotifications) return;
    
    new ToastContentBuilder()
        .AddText(title)
        .AddText(message)
        .Show();
}
```

---

### 15. Notification Sound

**Description:** Enable/disable sound alerts for notifications

**Implementation:**
- **Model:** Add `bool EnableNotificationSound` to `AppSettings.cs`
- **Code:** Use `SystemSounds.Exclamation.Play()` or suppress audio in toast builder

```csharp
public bool EnableNotificationSound { get; set; } = true;

// In ToastNotificationService
new ToastContentBuilder()
    .AddAudio(new ToastAudio { Silent = !_settings.EnableNotificationSound })
    // ...
```

---

### 16. Auto-Dismiss After X Seconds

**Description:** How long in-app notifications stay visible

**Implementation:**
- **Model:** Add `int NotificationAutoDismissSeconds` to `AppSettings.cs`
- **View:** Slider or NumericUpDown (range 3-30 seconds, 0 = manual dismiss)
- **NotificationService:** Start timer on publish, auto-dismiss when elapsed

```csharp
public int NotificationAutoDismissSeconds { get; set; } = 8;
```

---

## 🛡️ Privacy & Data

### 17. Clear Settings on Exit

**Description:** Option for privacy-conscious users to wipe settings when app closes

**Implementation:**
- **Model:** Add `bool ClearSettingsOnExit` to `AppSettings.cs`
- **App.xaml.cs:** In `OnExit`, delete settings file if enabled
- **Warning:** Show confirmation dialog when enabling this setting

```csharp
public bool ClearSettingsOnExit { get; set; } = false;

// App.xaml.cs OnExit
if (_settings.ClearSettingsOnExit)
    File.Delete(settingsPath);
```

---

### 18. Export/Import Settings

**Description:** Backup and restore configuration to/from JSON file

**Implementation:**
- **View:** Two buttons - "Export Settings" and "Import Settings"
- **Code:** Serialize `AppSettings` to JSON with `System.Text.Json`
- **Dialog:** Use `SaveFileDialog` / `OpenFileDialog` for file selection

```csharp
// Export
var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(path, json);

// Import
var json = File.ReadAllText(path);
var imported = JsonSerializer.Deserialize<AppSettings>(json);
```

---

### 19. Logging Level

**Description:** Control diagnostic logging verbosity (Off / Errors Only / Verbose)

**Implementation:**
- **Model:** Add `LogLevel LoggingLevel` enum to `AppSettings.cs`
- **DiagnosticLogger:** Check level before writing log entries

```csharp
public enum LogLevel { Off, ErrorsOnly, Verbose }
public LogLevel LoggingLevel { get; set; } = LogLevel.ErrorsOnly;

// DiagnosticLogger.cs
public static void LogInfo(string message, string source)
{
    if (_settings.LoggingLevel != LogLevel.Verbose) return;
    // ... write log
}
```

---

### 20. Open Logs Folder

**Description:** Quick button to open the logs directory in Explorer

**Implementation:**
- **View:** Button "Open Logs Folder"
- **Command:** `Process.Start("explorer.exe", logFolderPath)`

```csharp
// SettingsViewModel or MainViewModel
public ICommand OpenLogsFolderCommand => new RelayCommand(() =>
{
    var logsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemCleaner", "logs");
    Directory.CreateDirectory(logsPath);
    Process.Start("explorer.exe", logsPath);
});
```

---

## 🔧 Advanced

### 21. Parallel Scan Threads

**Description:** Control how many threads scan simultaneously (1-8)

**Implementation:**
- **Model:** Add `int ParallelScanThreads` to `AppSettings.cs`
- **View:** Slider with range 1-8
- **Core:** Use `ParallelOptions.MaxDegreeOfParallelism` in scan loops

```csharp
public int ParallelScanThreads { get; set; } = Environment.ProcessorCount;

// CleanupService.cs
await Parallel.ForEachAsync(modules, new ParallelOptions
{
    MaxDegreeOfParallelism = _settings.ParallelScanThreads
}, async (module, ct) => { /* scan */ });
```

---

### 22. Max File Size to Scan

**Description:** Skip files larger than X MB in certain modules (e.g., temp files)

**Implementation:**
- **Model:** Add `long MaxFileSizeToScanMB` to `AppSettings.cs`
- **Core:** In file enumeration, skip if `FileInfo.Length > MaxFileSizeToScanMB * 1024 * 1024`

```csharp
public long MaxFileSizeToScanMB { get; set; } = 500; // Skip files > 500 MB

// In scanning logic
if (fileInfo.Length > _settings.MaxFileSizeToScanMB * 1024 * 1024)
    continue;
```

---

### 23. Browser Profiles to Scan

**Description:** Choose which browser profiles to include in cleanup

**Implementation:**
- **Model:** Add `Dictionary<string, List<string>> EnabledBrowserProfiles` to `AppSettings.cs`
- **View:** TreeView with browsers as parents, profiles as children with checkboxes
- **Core:** Browser modules already enumerate profiles; filter by this setting

```csharp
public Dictionary<string, List<string>> EnabledBrowserProfiles { get; set; } = new();
// Key: "Chrome", "Firefox", "Edge"
// Value: List of profile folder names like "Default", "Profile 1"
```

---

### 24. Registry Backup Before Cleanup

**Description:** Auto-backup registry keys before removal during uninstall/cleanup

**Implementation:**
- **Model:** Add `bool BackupRegistryBeforeCleanup` to `AppSettings.cs`
- **Core:** Before deleting registry keys, export them with `reg export` command
- **Storage:** Save `.reg` files to `%AppData%\SystemCleaner\registry-backups\{timestamp}\`

```csharp
public bool BackupRegistryBeforeCleanup { get; set; } = true;

// RegistryBackupService.cs
public static void BackupKey(string keyPath, string outputPath)
{
    Process.Start("reg.exe", $"export \"{keyPath}\" \"{outputPath}\" /y")?.WaitForExit();
}
```

---

## 📐 Recommended Settings Page Layout

```
┌─────────────────────────────────────────────────────────┐
│ Settings                                                │
├─────────────────────────────────────────────────────────┤
│ ┌─ General ──────────────────────────────────────────┐  │
│ │ □ Ask for confirmation before changes              │  │
│ │ Theme: [System ▼]                                  │  │
│ │ Accent Color: [● ● ● ● ● ●]                        │  │
│ │ UI Scale: [Medium ▼]                               │  │
│ │ □ Compact Mode                                     │  │
│ │ □ Enable Animations                                │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ Startup ──────────────────────────────────────────┐  │
│ │ □ Run at Windows startup                           │  │
│ │ □ Minimize to system tray on close                 │  │
│ │ □ Auto-scan on launch                              │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ Cleanup Behavior ─────────────────────────────────┐  │
│ │ Skip files newer than: [0] days (0 = disabled)     │  │
│ │ □ Secure delete (shred files)                      │  │
│ │ □ Create restore point before cleaning             │  │
│ │ Excluded Paths: [Add] [Remove]                     │  │
│ │   ├ C:\Important\Folder                            │  │
│ │   └ D:\MyData                                      │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ Performance ──────────────────────────────────────┐  │
│ │ Hardware monitor interval: [2 seconds ▼]           │  │
│ │ Parallel scan threads: [────●────] 4               │  │
│ │ Max file size to scan: [500] MB                    │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ Notifications ────────────────────────────────────┐  │
│ │ □ Show desktop notifications                       │  │
│ │ □ Enable notification sound                        │  │
│ │ Auto-dismiss after: [8] seconds                    │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ Privacy & Data ───────────────────────────────────┐  │
│ │ Logging level: [Errors Only ▼]                     │  │
│ │ □ Clear settings on exit                           │  │
│ │ [Export Settings] [Import Settings] [Open Logs]    │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ VirusTotal Integration ───────────────────────────┐  │
│ │ API Key: [••••••••••••••••]                        │  │
│ │ [Save] [Clear]                                     │  │
│ │ Status: API key stored                             │  │
│ └────────────────────────────────────────────────────┘  │
│                                                         │
│ ┌─ About ────────────────────────────────────────────┐  │
│ │ SystemCleaner v1.0.0                               │  │
│ │ © 2025 Your Name                                   │  │
│ │ [Check for Updates] [View on GitHub]               │  │
│ └────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 🎯 Implementation Priority

### Phase 1 - Quick Wins (Low Effort, High Impact)
1. ✅ Run at Windows Startup
2. ✅ Open Logs Folder
3. ✅ Auto-Scan on Launch
4. ✅ Hardware Monitor Refresh Rate
5. ✅ Logging Level

### Phase 2 - Core Features
6. Exclude Paths
7. Accent Color Picker
8. Minimize to System Tray
9. Export/Import Settings
10. Skip Files Newer Than X Days

### Phase 3 - Power User Features
11. Secure Delete
12. Create Restore Point
13. Parallel Scan Threads
14. Desktop Notifications (Toast)
15. Default Scan Modules

### Phase 4 - Polish
16. UI Scale
17. Compact Mode
18. Animations Toggle
19. Browser Profiles Selection
20. Registry Backup

---

## 📦 Recommended NuGet Packages

| Package | Purpose |
|---------|---------|
| `Hardcodet.NotifyIcon.Wpf` | System tray icon support |
| `Microsoft.Toolkit.Uwp.Notifications` | Windows 10/11 toast notifications |
| `Extended.Wpf.Toolkit` | Color picker, NumericUpDown controls |
| `Ookii.Dialogs.Wpf` | Modern folder browser dialog |

---

## 📁 Files to Modify

| File | Changes |
|------|---------|
| `AppSettings.cs` | Add all new properties |
| `SettingsPage.xaml` | Add UI controls for each setting |
| `SettingsPage.xaml.cs` | Event handlers for buttons |
| `MainViewModel.cs` | Expose new settings, add commands |
| `ThemeService.cs` | Accent color application |
| `HardwareMonitorService.cs` | Configurable interval |
| `DiagnosticLogger.cs` | Respect logging level |
| `CleanupService.cs` | Respect exclude paths, file age, secure delete |
| `App.xaml.cs` | Handle startup args, tray icon, exit cleanup |
