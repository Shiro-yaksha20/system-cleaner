# Release Process

1. Ensure the working tree is clean and up to date with `origin/main`.
2. Update `CHANGELOG.md` with user-facing changes and bump the version in app metadata if necessary.
3. Run quality gates:
   - `dotnet restore SystemCleaner.sln`
   - `dotnet format SystemCleaner.sln`
   - `dotnet build SystemCleaner.sln -c Release`
   - `dotnet test SystemCleaner.sln -c Release`
   - `dotnet publish SystemCleaner.App/SystemCleaner.App.csproj -c Release -r win-x64 --self-contained false -o publish`
4. Sign release binaries (Authenticode) once certificates are available.
5. Create a version tag: `git tag vX.Y.Z`
6. Push changes and tag: `git push origin main --follow-tags`
7. Let the `release.yml` workflow run; download the generated artifacts, verify signatures, and attach to the GitHub release if not done automatically.
8. Draft release notes summarizing highlights, bug fixes, known issues, and VirusTotal quota considerations.
