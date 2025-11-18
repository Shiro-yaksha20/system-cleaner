# Contributing Guidelines

Thanks for taking time to improve SystemCleaner! This document captures the development workflow so changes land safely.

## Branch & Commit Flow

- Create a topic branch off `main` for every change. Prefer the prefixes `feature/`, `bugfix/`, or `chore/`.
- Keep commits focused. Use present-tense, descriptive messages (e.g. `Add VirusTotal quota popover`).
- Rebase or squash before merging so `main` stays linear. Avoid merge commits unless reverting.

## Pre-Commit Checklist

Before pushing or opening a pull request:

1. Restore dependencies: `dotnet restore SystemCleaner.sln`
2. Format code (optional for now): `dotnet format SystemCleaner.sln`
3. Build: `dotnet build SystemCleaner.sln`
4. Run tests: `dotnet test SystemCleaner.sln`
5. Manually verify the WPF app still launches when UI changes are involved.
6. Scan releases by running `dotnet publish SystemCleaner.App/SystemCleaner.App.csproj -c Release -o publish` if the change affects packaging.

## Pull Requests

- Link related issues in the PR description.
- Describe the change, testing performed, and any follow-up work.
- Add screenshots/GIFs for UI updates when possible.
- Ensure CI (build & tests) passes before requesting review.

## Code Review Expectations

- At least one reviewer should approve before merging into `main`.
- Address feedback with additional commits or amend/squash as appropriate.
- The author merges once the PR is green and approved.

## Versioning & Releases

- Tag release commits using semantic versioning (e.g. `v0.1.0`).
- Publish signed installers from tagged releases only.
- Maintain release notes summarizing user-facing changes and known issues.

## Reporting Issues

- Use the issue templates (bug/feature) and include environment details.
- Attach relevant logs from `%LOCALAPPDATA%\SystemCleaner\logs` for runtime failures.

## Security

Please do not disclose security issues publicly. Follow the process in `SECURITY.md` for responsible reporting.
