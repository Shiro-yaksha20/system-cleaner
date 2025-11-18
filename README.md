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

## Project Structure

```
SystemCleaner.sln
├── SystemCleaner.Core
├── SystemCleaner.App
└── SystemCleaner.Tests
```

## Tasks

VS Code tasks are configured in `.vscode/tasks.json`:

- `build` – runs `dotnet build SystemCleaner.sln`
- `test` – runs `dotnet test SystemCleaner.sln`
