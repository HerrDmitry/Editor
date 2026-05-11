# Tasks: CLI File Open

## Task Dependency Graph

```
T1 → T2 → T3 → T4
```

---

## Task 1: Add argument parsing in Program.cs

### Description
Extract the first positional (non-flag) argument from `args` and resolve it to an absolute path.

### Files to modify
- `src/EditorApp/Program.cs`

### Implementation details
1. After `var app = builder.Build();`, add argument extraction:
   ```csharp
   string? initialFilePath = null;
   if (args.Length > 0 && !args[0].StartsWith('-'))
   {
       var raw = args[0];
       initialFilePath = Path.IsPathRooted(raw)
           ? raw
           : Path.GetFullPath(raw, Environment.CurrentDirectory);
   }
   ```
2. Pass `initialFilePath` to `PhotinoHostService` constructor (Task 2 adds the parameter).

### Acceptance criteria
- [x] Relative paths resolved against CWD
- [x] Absolute paths used as-is
- [x] Args starting with `-` are ignored
- [x] No argument → `initialFilePath` is null

---

## Task 2: Add initialFilePath support to PhotinoHostService

### Description
Accept an optional file path in the constructor and trigger `OpenFileByPathAsync` after the window is created.

### Files to modify
- `src/EditorApp/Services/PhotinoHostService.cs`

### Implementation details
1. Add `string? initialFilePath = null` parameter to the public constructor.
2. Store in private field `private readonly string? _initialFilePath;`.
3. In `Run()`, after `_messageRouter.StartListening()`, if `_initialFilePath` is not null, register a window-created callback that calls `OpenFileByPathAsync(_initialFilePath)`.
4. If Photino doesn't expose a window-created event, use `Task.Run` with a small delay or invoke directly after `StartListening()` but before `_app.Run()` — the message router is already listening so the UI will receive the response once it connects.

### Acceptance criteria
- [x] Constructor accepts optional initialFilePath
- [x] File open triggered after window ready
- [x] Null initialFilePath → no auto-open (existing behavior preserved)
- [x] Uses existing OpenFileByPathAsync (same code path as dialog)

---

## Task 3: Update window title on file open

### Description
Set the Photino window title to include the file name after a successful file open (applies to both CLI and dialog opens).

### Files to modify
- `src/EditorApp/Services/PhotinoHostService.cs`

### Implementation details
1. In `OpenFileByPathAsync`, after sending the final (non-partial) `FileOpenedResponse`, add:
   ```csharp
   _app?.MainWindow.SetTitle($"{metadata.FileName} — Editor");
   ```
2. Guard with null check on `_app` for the test constructor path.

### Acceptance criteria
- [x] Window title shows file name after successful open
- [x] Title format: `{fileName} — Editor`
- [x] Works for both CLI and dialog file opens
- [x] No crash in test constructor (null _app)

---

## Task 4: Add tests for argument parsing and initial file open

### Description
Add unit tests verifying argument parsing logic and that PhotinoHostService triggers file open when initialFilePath is provided.

### Files to modify
- `tests/EditorApp.Tests/` (new test file)

### Implementation details
1. Test argument parsing scenarios:
   - Relative path → resolved to absolute
   - Absolute path → unchanged
   - Flag argument (starts with `-`) → ignored, no file open
   - No arguments → null
2. Test PhotinoHostService with initialFilePath:
   - Mock IFileService and IMessageRouter
   - Verify OpenFileByPathAsync called with correct path
3. Property-based test: for any valid relative path string, `Path.GetFullPath(path, cwd)` produces a rooted path.

### Acceptance criteria
- [x] All argument parsing cases covered
- [x] Integration test verifies file open triggered
- [x] Property test for path resolution correctness
- [x] All tests pass
