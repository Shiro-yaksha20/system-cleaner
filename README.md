# SystemCleaner

A Windows system cleaner application built with .NET 9 and WPF. The solution consists of:

- `SystemCleaner.Core`: Core cleanup engine providing cleanup modules and services.
- `SystemCleaner.App`: WPF front-end for scanning and cleaning temporary files, browser cache, and diagnostic data.
- `SystemCleaner.Tests`: xUnit test project covering core functionality.

## Prerequisites

- .NET SDK 9.0
- Windows 10/11

## Getting Started

1. Build the solution:
   ```powershell
   dotnet build SystemCleaner.sln
   ```

2. Run tests:
   ```powershell
   dotnet test SystemCleaner.sln
   ```

3. Launch the WPF application:
   ```powershell
   dotnet run --project SystemCleaner.App/SystemCleaner.App.csproj
   ```

4. Create a packaged build:
   ```powershell
   dotnet publish SystemCleaner.App/SystemCleaner.App.csproj -c Release -r win-x64 --self-contained false -o publish
   ```
   The packaged binaries will be available under `publish/`; run `SystemCleaner.App.exe` from that directory to test the Release build.

## Features

- Overview dashboard surfaces last scan results, quick clean history, and module health summaries at a glance.
- Quick Clean workflow that creates a restore point, scans safe modules, and removes clutter in one click.
- Cleanup modules for temporary files, browser caches, diagnostic data, large files, and duplicate files.
- Detailed per-item review with size summaries, selection controls, Explorer shortcuts, and warnings for sensitive modules.
- Activity log, toast-style status messaging, and persistent size totals to track what was removed.
- Theme switching (Light/Dark/System) and a read-only startup manager with Explorer shortcuts for auditing login apps.
- VirusTotal integration for file and URL analysis with history, engine verdicts, and live quota popover fed by the `groups/self` API.

## VirusTotal Usage Notes

- **API Key** – Each user supplies their own VT API key under Settings. Without a key the VirusTotal tab stays read-only.
- **Quota Display** – The bottom-right “Quota” chip hovers a detailed tooltip showing current rate limits using live data from `groups/self`. Headers still update the summary between refreshes.
- **Upload Optimizations** – The app hashes files before upload to re-use existing analyses when VirusTotal already knows the sample.
- **Offline Mode** – VirusTotal requires network access. For offline malware scanning consider integrating a local AV engine (e.g., Defender CLI) and swap to it when no connectivity is detected.

## Project Structure

```
SystemCleaner.sln
├── SystemCleaner.Core
├── SystemCleaner.App
└── SystemCleaner.Tests
```

## Development Workflow

- VS Code tasks (`.vscode/tasks.json`):
   - `build` – runs `dotnet build SystemCleaner.sln`
   - `test` – runs `dotnet test SystemCleaner.sln`
- Git version control is initialized. Create feature branches for changes, merge to `main` after successful builds, and tag releases when ready (e.g., `v0.1.0`).
