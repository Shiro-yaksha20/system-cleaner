# SystemCleaner

SystemCleaner is a Windows desktop utility built with .NET 9 and WPF. It combines disk cleanup, system monitoring, and a modern uninstaller so users can review everything in one place before removing it.

---

## Capabilities

### Cleanup and storage
- Remove Windows, user, and application temporary data
- Clear browser caches for Edge, Chrome, Firefox, and Brave
- Clean diagnostic logs, crash dumps, and other telemetry artifacts
- Locate large or duplicate files that can be archived or deleted

### Monitoring
- Track CPU, GPU, memory, and disk usage in real time
- Visualize per-core load and VRAM consumption for quick debugging

### Uninstaller and residual scan
- Review installed software with vendor and install-date metadata
- Remove applications and immediately scan for leftover files, folders, and registry keys
- Inspect browser extensions to understand what auto-installs alongside software

### Productivity
- Quick Clean runs safe modules, creates a restore point, and removes clutter with one click
- Keyboard shortcuts (F5 refresh, Delete uninstall) streamline repetitive flows
- Activity log maintains a record of every cleanup action

### VirusTotal workspace
- Submit file hashes or uploads plus URLs to VirusTotal
- Persist scan history with engine verdicts and quota information so users can see how much of the hourly allowance remains

---

## Build and run

Install prerequisites:
- Windows 10 version 1903 or later (Windows 11 recommended)
- .NET 9.0 SDK (includes the runtime)

Clone and run:

```powershell
git clone https://github.com/Shiro-yaksha20/New-folder--2-.git
cd New-folder--2-

dotnet build SystemCleaner.sln
dotnet run --project SystemCleaner.App/SystemCleaner.App.csproj
```

Execute the automated tests:

```powershell

```

Publish a release build that you can distribute for manual install:

```powershell
dotnet publish SystemCleaner.App/SystemCleaner.App.csproj -c Release -r win-x64 --self-contained false -o publish
```

---

## Working with the app

1. Start SystemCleaner with elevated permissions so hardware sensors and uninstall operations can run without prompts.
2. Press **Scan** to populate each module (cleanup, residuals, browser extensions, etc.).
3. Review detections in the grid and toggle the items you want to remove.
4. Press **Clean** to execute the selected operations. Status updates stream into the activity log.

### Quick Clean
Quick Clean is meant for routine maintenance. It focuses on low-risk modules, sets a restore point, and performs the scan and clean in one action.

### VirusTotal
Provide a VirusTotal API key under **Settings → VirusTotal Integration**. Once configured, drag files or paste URLs into the VirusTotal tab to submit them, then monitor the verdicts and quota chip.

---

## Project layout

```
SystemCleaner/
├── SystemCleaner.App/         # WPF client (Views, ViewModels, services, converters)
├── SystemCleaner.Core/        # Cleanup modules, startup manager, uninstall services
└── SystemCleaner.Tests/       # xUnit tests covering core logic
```

Supporting documentation lives under `docs/`, and publish artifacts default to `publish/`.

---

## Configuration and logs

- Settings: `%LOCALAPPDATA%\SystemCleaner\settings.json`
- Diagnostic logs: `%LOCALAPPDATA%\SystemCleaner\logs\`

---

## Known limitations

**AMD Ryzen Mobile CPU telemetry** – Some ASUS ROG laptops expose temperature and clock sensors only through proprietary Armoury Crate interfaces. LibreHardwareMonitor cannot read those buses, so SystemCleaner reports "N/A" for the affected metrics even though CPU load remains available.

---

## Credits

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for sensor access
- [VirusTotal](https://www.virustotal.com/) for file and URL scanning
- [Icons8](https://icons8.com/) for UI iconography used inside the application

