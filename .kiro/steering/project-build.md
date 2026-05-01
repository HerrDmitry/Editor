# Project Build & Test

## Solution Structure

- .NET solution: `EditorApp.slnx`
- Main project: `src/EditorApp/EditorApp.csproj` (Blazor + Photino desktop app, .NET 10)
- Test project: `tests/EditorApp.Tests/` (xUnit + FsCheck 3.1.0)
- Frontend tests: `tests/frontend/` (vitest + fast-check + @testing-library/react)

## TypeScript Compilation

TypeScript is compiled as part of `dotnet build` via a custom MSBuild target in `EditorApp.csproj`.

The command is:
```
node scripts/tsc.js -p tsconfig.json
```
Run from `src/EditorApp/` directory.

To compile TypeScript standalone (without full dotnet build):
```bash
node scripts/tsc.js -p tsconfig.json
```
with cwd = `src/EditorApp`

## Running Tests

### C# tests (backend)
```bash
dotnet test tests/EditorApp.Tests
```

### Frontend tests
```bash
npm test
```
with cwd = `tests/frontend`

## Build
```bash
dotnet build src/EditorApp
```
This also compiles TypeScript automatically.

## Key Constants
- `SizeThresholdBytes = 256_000` in `FileService.cs` — large file threshold
- `ProgressThrottleMs = 50` in `FileService.cs`
- `WINDOW_SIZE = 400`, `FETCH_SIZE = 200` in `ContentArea.tsx`

## Git Commit Messages

The shell is `fish`. Multiline strings with `\n` or heredocs don't work reliably.

Use **multiple `-m` flags** for multi-paragraph commits:
```bash
git commit -m "feat: subject line" -m "Body paragraph one." -m "Body paragraph two."
```

Each `-m` becomes a separate paragraph in the commit message.

Convention: Conventional Commits format. Subject ≤50 chars.
