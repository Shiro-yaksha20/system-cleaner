# SystemCleaner

SystemCleaner is a Windows desktop utility built with .NET 9 and WPF that combines disk cleanup, system monitoring, and a modern uninstaller so users can review and remove clutter safely.

Table of contents
- [Product requirements (PRD)](#product-requirements-prd)
- [Features](#features)
- [Requirements](#requirements)
- [Quickstart](#quickstart)
  - [Clone, build, run](#clone-build-run)
  - [Run tests](#run-tests)
- [Repository tour (where things live)](#repository-tour-where-things-live)
- [Publish / Release (maintainers)](#publish--release-maintainers)
- [Configuration & logs](#configuration--logs)
- [Architecture & project layout](#architecture--project-layout)
- [Contributing](#contributing)
  - [Onboarding checklist](#onboarding-checklist)
  - [PR checklist for contributors](#pr-checklist-for-contributors)
  - [Documentation responsibilities](#documentation-responsibilities)
- [CI & release automation](#ci--release-automation)
- [Security](#security)
- [Troubleshooting & known limitations](#troubleshooting--known-limitations)
- [Changelog & docs](#changelog--docs)
- [License](#license)

## Product requirements (PRD)

Purpose
- Problem statement: Users need a single trustworthy Windows tool to find and remove disk clutter, inspect installed software, and monitor system health without accidentally deleting important data.
- Target users: Power users and technicians who maintain Windows desktops and want a safe, auditable cleanup and uninstall experience.

Goals
- Provide safe, auditable cleanup modules (temp, browser caches, logs, duplicates).
- Present a clear uninstaller experience with residual scanning and safe removal options.
- Offer lightweight hardware monitoring and a VirusTotal workspace for suspicious files.
- Make the app easy to build, test, and contribute to (clean repo layout, clear docs, CI).

Scope
- In scope: Directory cleanup, large/duplicate detection, startup manager, uninstaller + residual scans, hardware telemetry (LibreHardwareMonitor), VirusTotal integration, restore-point creation for safety.
- Out of scope: Cross-platform UI, mobile clients, online auto-updates (unless added via release process later).

Non-goals / Constraints
- Windows-only due to WPF and system APIs.
- .NET 9 SDK pinned by `global.json` (9.0.304).
- Some hardware sensors or uninstall operations require elevated privileges.

Success metrics (examples contributors can help measure)
- User-visible: Average bytes freed per Quick Clean, number of successful uninstall + residual-cleans, reduction in user-reported clobbered files (regressions).
- Developer-focused: CI passing rate, test coverage for core modules, PR review/merge times.

Privacy & security constraints
- VirusTotal API key is currently stored in plaintext settings.json — prefer Windows Credential Locker for secure storage.
- Do not commit secrets to the repo. Follow SECURITY.md for reporting vulnerabilities.

## Features
- Cleanup modules: remove Windows, user and application temporary data; browser caches (Edge, Chrome, Firefox, Brave); diagnostic logs and crash dumps.
- Large file and duplicate file discovery with safe deletion options.
- Uninstaller: review installed software with metadata, launch uninstalls, and scan for residual files/registry entries.
- Startup manager: enumerate and toggle autorun entries.
- Hardware monitoring: CPU, GPU, memory and disk usage (LibreHardwareMonitor).
- VirusTotal workspace: submit files/URLs, persist scan history and quota info.
- Productivity: Quick Clean (safe modules + restore point), keyboard shortcuts, and activity log.

## Requirements
- Windows 10 (1903+) or Windows 11 — WPF UI and system APIs are Windows-only.
- .NET 9.0 SDK (global.json pins SDK to 9.0.304). Install matching SDK: https://dotnet.microsoft.com/
- Visual Studio 2022/2023 or dotnet CLI for building.
- Elevated privileges are required for some operations (hardware sensors, uninstall/residual cleanup).

## Quickstart

### Clone, build, run
Open PowerShell or a terminal on Windows:

```powershell
git clone https://github.com/Shiro-yaksha20/system-cleaner.git
cd system-cleaner

# Restore and build the solution
dotnet restore SystemCleaner.sln
dotnet build SystemCleaner.sln

# Run the WPF app from source
dotnet run --project SystemCleaner.App/SystemCleaner.App.csproj
```

Notes:
- The UI uses WPF and requires a desktop session. Run elevated when you need hardware sensors or uninstall tasks to avoid permission prompts.
- The app targets `net9.0-windows` per SystemCleaner.App.csproj.

### Run tests
The solution includes a small xUnit test project:

```powershell
dotnet test SystemCleaner.sln
```

CI collects code coverage on Windows runners.

## Repository tour (where things live)
To help contributors quickly find what they need, here are the primary files and folders:

- Solution and projects
  - SystemCleaner.sln — Visual Studio solution
  - SystemCleaner.App/ — WPF client (Views, ViewModels, services) — targets net9.0-windows
    - SystemCleaner.App/SystemCleaner.App.csproj
    - Themes/, Views/, ViewModels/, App.xaml, MainWindow.xaml
  - SystemCleaner.Core/ — core domain logic, cleanup modules — targets net9.0
    - SystemCleaner.Core/SystemCleaner.Core.csproj
  - SystemCleaner.Tests/ — xUnit tests for core logic

- Documentation & policies
  - README.md — this file (overview, PRD, quickstart)
  - CONTRIBUTING.md — contributor workflow and guidelines
  - CHANGELOG.md — keep-a-changelog scaffold
  - docs/system-overview.md — architecture reference and detailed component descriptions
  - docs/RELEASE_PROCESS.md — release checklist and steps
  - SECURITY.md — security policy and reporting instructions

- CI / Release automation
  - .github/workflows/ci.yml — CI build/test workflow
  - .github/workflows/release.yml — release workflow triggered by tag pushes

- Config & metadata
  - global.json — SDK pin (9.0.304)
  - publish/ — default publish artifacts (generated by dotnet publish)
  - scripts/ or tools/ (if present) — helper scripts for maintainers

- Runtime files (created at runtime)
  - %LOCALAPPDATA%\SystemCleaner\settings.json — persisted app settings (theme, VirusTotal key)
  - %LOCALAPPDATA%\SystemCleaner\logs\ — diagnostic logs

If you add a new feature, please:
- Update docs/system-overview.md if architecture changes
- Add or update unit tests under SystemCleaner.Tests
- Update CHANGELOG.md and any relevant docs in docs/

## Publish / Release (maintainers)
To create a distributable self-contained Windows publish (single-file):

```powershell
dotnet publish SystemCleaner.App/SystemCleaner.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish/win-x64
```

Recommended release process (see docs/RELEASE_PROCESS.md for full steps):
1. Update CHANGELOG.md and bump version metadata as appropriate.
2. Run formatting, tests and a release build:
   - dotnet format SystemCleaner.sln
   - dotnet build SystemCleaner.sln -c Release
   - dotnet test SystemCleaner.sln -c Release
3. Publish artifacts, sign binaries (Authenticode) if applicable, tag the release (e.g. `v1.2.3`) and push tags.
4. The repository contains a GitHub Actions workflow (release.yml) that will build, publish and attach artifacts for tag pushes.

## Configuration & logs
- Settings: %LOCALAPPDATA%\SystemCleaner\settings.json
- Logs: %LOCALAPPDATA%\SystemCleaner\logs\ (daily log files written by DiagnosticLogger)

VirusTotal API key: set in the app under Settings → VirusTotal Integration. Storing sensitive keys in settings is currently plaintext; see docs for suggestions to improve this (prefer Windows Credential Locker).

## Architecture & project layout
High-level project structure:

- SystemCleaner.App/ — WPF client (Views, ViewModels, services, converters) — target: net9.0-windows
- SystemCleaner.Core/ — cleanup modules, startup/uninstall domain logic — target: net9.0
- SystemCleaner.Tests/ — xUnit tests

For a thorough architecture reference and component descriptions, see:
- docs/system-overview.md

## Contributing

Contributions are welcome. Please follow the repository guidelines:

- Create topic branches off `main` (prefix: feature/, bugfix/, chore/).
- Keep commits focused and rebase/squash before merging to keep history linear.
- Run the pre-commit checklist found in CONTRIBUTING.md:
  - dotnet restore
  - dotnet format
  - dotnet build
  - dotnet test
  - Manual verification of UI when changing Views/ViewModels

Open a PR with a description, tests, and screenshots for UI changes. See CONTRIBUTING.md for full details.

### Onboarding checklist
A short checklist to get new contributors productive quickly:

1. Setup
   - Install Windows 10/11 with a desktop session.
   - Install .NET SDK 9.0.x (global.json pins 9.0.304).
   - (Optional) Visual Studio 2022/2023 with WPF workload for the best editing experience.

2. Get the code
   - git clone https://github.com/Shiro-yaksha20/system-cleaner.git
   - cd system-cleaner

3. Build & run
   - dotnet restore SystemCleaner.sln
   - dotnet build SystemCleaner.sln
   - dotnet run --project SystemCleaner.App/SystemCleaner.App.csproj

4. Tests & formatting
   - dotnet test SystemCleaner.sln
   - dotnet format SystemCleaner.sln --verify-no-changes (CI enforces formatting)

5. Start a branch
   - git checkout -b feature/your-feature-name

6. When ready to push
   - Ensure tests & formatting pass locally
   - Update CHANGELOG.md under Unreleased
   - Open a PR linking related issues, include screenshots for UI changes

### PR checklist for contributors
Before opening a PR, verify:

- [ ] Branch is based on latest main
- [ ] Code builds and tests pass locally
- [ ] dotnet format has been applied (CI also checks)
- [ ] Unit tests added/updated for new behavior
- [ ] Docs updated where behavior or architecture changes (README, docs/, CHANGELOG.md)
- [ ] Screenshots/GIFs attached for UI changes
- [ ] PR description explains motivation, changes, and testing steps

### Documentation responsibilities
When changing or adding features, update the relevant documentation:

- Functional changes: README.md (overview & quickstart) and CHANGELOG.md
- Architecture or design changes: docs/system-overview.md
- Release related changes: docs/RELEASE_PROCESS.md
- Security-sensitive changes: SECURITY.md (and coordinate with maintainers)
- Add screenshots to PRs and (if merged) to README or docs for demo purposes

If you add API keys, secrets, or credentials to local development docs, include guidance on how to store them securely (do not commit).

## CI & release automation
- CI: .github/workflows/ci.yml — runs on Windows: checkout, setup .NET 9, dotnet format (verify no changes), build, test (collect coverage), and checks for vulnerable packages.
- Releases: .github/workflows/release.yml — triggers on tag push (`v*`), builds and tests, publishes a self-contained win-x64 artifact, archives to a ZIP and creates a GitHub Release.

Adjust workflow files to change runner versions, additional matrix targets, or signing steps.

## Security
See SECURITY.md for the security policy. To report vulnerabilities:
- Do not open a public issue for security bugs.
- Follow the private reporting instructions in SECURITY.md (update with a contact address if you are a maintainer).

Security notes for contributors:
- Do not store API keys in plain text in the repo.
- Prefer secure stores (Windows Credential Locker) for persistent keys in future PRs.
- Add tests for failure cases where external integrations are unavailable or rate-limited.

## Troubleshooting & known limitations
- AMD Ryzen Mobile telemetry: Some ASUS ROG laptops expose temperature and clock sensors only through proprietary Armoury Crate interfaces. LibreHardwareMonitor cannot read those buses; affected metrics may report "N/A".
- Elevated permissions: Some operations (hardware sensors, uninstall or residual cleanup, restore point creation) require elevated process privileges. If a sensor or operation reports insufficient permission, restart the app as Administrator.
- No license file present: See the note below.

## Changelog & docs
- CHANGELOG.md contains the changelog scaffold and follows Keep a Changelog.
- Developer and architectural documentation lives in the docs/ directory (including RELEASE_PROCESS.md and system-overview.md).

## License
This repository does not include a LICENSE file. Please verify licensing with the project owner before using or redistributing the source or binaries.

---

If you'd like, I can:
- Add a badge section (CI / release) if you want status badges,
- Draft a suggested LICENSE file (MIT/Apache) for the project,
- Extract quickstart troubleshooting tips into a Troubleshooting.md,
- Create a contributor-first checklist or a PR template file for the repository.

Happy to further refine the PRD, onboarding checklist, or add example contribution issues to help new contributors get started.