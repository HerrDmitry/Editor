# Implementation Plan: Early Content Display for Large Files

## Overview

Add partial metadata emission to `FileService` so large files (>256KB) show content after first 256KB scanned. Scan continues in background → progress bar updates → scrollbar range updates on completion. Backend changes: `isPartial` field, `onPartialMetadata` callback, lock-based thread safety. Frontend changes: dual `onFileOpened` handling, StatusBar shows progress alongside metadata.

## Tasks

- [x] 1. Extend backend models and interface for partial metadata support
  - [x] 1.1 Add `IsPartial` boolean property to `FileOpenedResponse` in `src/EditorApp/Models/Messages.cs`
    - Add `[JsonPropertyName("isPartial")] public bool IsPartial { get; set; }` field
    - _Requirements: 4.1_
  - [x] 1.2 Add `onPartialMetadata` parameter to `IFileService.OpenFileAsync` in `src/EditorApp/Services/IFileService.cs`
    - Insert `Action<FileOpenMetadata>? onPartialMetadata = null` as second parameter, before `progress`
    - Update XML doc to describe callback behavior
    - _Requirements: 10.1_

- [x] 2. Implement partial metadata emission and thread safety in FileService
  - [x] 2.1 Add `_indexLock` field and modify `OpenFileAsync` in `src/EditorApp/Services/FileService.cs`
    - Add `private readonly object _indexLock = new();` field
    - Add `onPartialMetadata` parameter matching new interface signature
    - Add `bool partialEmitted = false` tracking variable in scanning loop
    - After each buffer read, check `totalBytesRead >= SizeThresholdBytes && !partialEmitted`
    - When threshold crossed: acquire `_indexLock` → store partial `lineOffsets` in `_lineIndexCache` → release lock → invoke `onPartialMetadata` with provisional `FileOpenMetadata` → set `partialEmitted = true`
    - Wrap all subsequent `lineOffsets.Add()` calls in `lock(_indexLock)` after threshold
    - Wrap final `_lineIndexCache[filePath] = lineOffsets` assignment in lock
    - _Requirements: 1.1, 1.3, 2.1, 8.2, 10.1, 10.2, 10.3_
  - [x] 2.2 Modify `ReadLinesAsync` for lock-based snapshot reads and range clamping in `src/EditorApp/Services/FileService.cs`
    - Acquire `_indexLock` → snapshot `lineOffsets.Count` and copy needed offset values → release lock
    - Use snapshot for file I/O (no lock held during reads)
    - Clamp `startLine` and `lineCount` to snapshot count
    - Return snapshot count as `TotalLines` in `LinesResult`
    - _Requirements: 3.1, 3.2, 3.3, 8.1_
  - [x] 2.3 Write property test: Partial index read clamping and totalLines accuracy
    - **Property 1: Partial index read clamping and totalLines accuracy**
    - Generate random `List<long>` (partial index), random `startLine`/`lineCount`. Call `ReadLinesAsync` against mock file with that index. Verify clamping and totalLines.
    - Create test in `tests/EditorApp.Tests/Properties/FileServiceProperties.cs`
    - **Validates: Requirements 1.1, 3.1, 3.2, 3.3**
  - [x] 2.4 Write property test: Partial metadata callback fires exactly once for large files
    - **Property 2: Partial metadata callback fires exactly once for large files**
    - Generate random file content > 256KB with varying line lengths. Call `OpenFileAsync`. Count callback invocations. Also test files ≤ 256KB → zero invocations.
    - Create test in `tests/EditorApp.Tests/Properties/FileServiceProperties.cs`
    - **Validates: Requirements 1.3, 10.2**
  - [x] 2.5 Write property test: Index available when partial callback fires
    - **Property 3: Index available when partial callback fires**
    - Generate random large file content. In callback, call `ReadLinesAsync(path, 0, 1)`. Verify success.
    - Create test in `tests/EditorApp.Tests/Properties/FileServiceProperties.cs`
    - **Validates: Requirements 10.3**
  - [x] 2.6 Write property test: Provisional totalLines matches indexed line count at threshold
    - **Property 4: Provisional totalLines matches indexed line count at threshold**
    - Generate random file content with known line positions. Compute expected line count at 256KB. Verify callback's `totalLines` matches.
    - Create test in `tests/EditorApp.Tests/Properties/FileServiceProperties.cs`
    - **Validates: Requirements 4.2**
  - [x] 2.7 Write property test: Thread-safe concurrent index access
    - **Property 5: Thread-safe concurrent index access**
    - Generate random index size. Spawn concurrent append + read tasks. Verify no exceptions and consistent results.
    - Create test in `tests/EditorApp.Tests/Properties/FileServiceProperties.cs`
    - **Validates: Requirements 8.1**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Wire partial metadata callback in PhotinoHostService
  - [x] 4.1 Modify `OpenFileByPathAsync` in `src/EditorApp/Services/PhotinoHostService.cs`
    - Create `Action<FileOpenMetadata> onPartialMetadata` lambda that sets `_currentFilePath` and sends `FileOpenedResponse` with `IsPartial = true`
    - Pass `onPartialMetadata` to `_fileService.OpenFileAsync` call
    - After `OpenFileAsync` returns, send final `FileOpenedResponse` with `IsPartial = false`
    - _Requirements: 1.2, 2.3, 10.1_
  - [x] 4.2 Write unit tests for PhotinoHostService partial + final response flow
    - Test large file → sends partial then final `FileOpenedResponse`
    - Test small file → sends only final `FileOpenedResponse`
    - Test cancellation during scan → no final response sent
    - Create tests in `tests/EditorApp.Tests/Unit/PhotinoHostService/`
    - _Requirements: 1.2, 1.3, 2.3, 9.1_

- [x] 5. Update frontend types and App.tsx for dual FileOpenedResponse handling
  - [x] 5.1 Add `isPartial` field to `FileMeta` interface in `src/EditorApp/src/InteropService.ts` and `src/EditorApp/src/App.tsx`
    - Add `isPartial: boolean` to `FileMeta` interface in both files
    - _Requirements: 4.1_
  - [x] 5.2 Modify `onFileOpened` handler in `src/EditorApp/src/App.tsx`
    - If `data.isPartial`: set fileMeta, clear isLoading + error, set title bar, request initial lines (0, 200), do NOT clear loadProgress
    - If `!data.isPartial` and same fileName as current fileMeta: update fileMeta (totalLines updates), clear loadProgress only
    - If `!data.isPartial` and different/no current file: full reset (existing behavior)
    - Use `fileMetaRef` to access current fileMeta inside callback without stale closure
    - _Requirements: 5.1, 6.1, 6.3, 7.2, 9.2, 9.3_

- [x] 6. Modify StatusBar to show progress bar alongside metadata
  - [x] 6.1 Update `StatusBar` component in `src/EditorApp/src/StatusBar.tsx`
    - Change layout: always render metadata items when `metadata` is present
    - Render progress bar alongside metadata when `showProgress` is true (not replacing metadata)
    - Hide wrap toggle during scan (progress bar takes its space)
    - Show wrap toggle when progress completes
    - _Requirements: 7.1, 7.2_
  - [x] 6.2 Write property test: StatusBar renders progress bar alongside metadata during partial display
    - **Property 6: StatusBar renders progress bar alongside metadata during partial display**
    - Generate random `FileMeta` + random `loadProgress` (percent 0-99). Render StatusBar. Verify both metadata items and progress bar present in DOM.
    - Note: This is a React component test — may use jsdom or similar. If not feasible in current test setup, write as unit test verifying render logic.
    - **Validates: Requirements 7.1**

- [x] 7. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Integration wiring and final verification
  - [x] 8.1 Add `isPartial` to `FileMeta` interface in `src/EditorApp/src/ContentArea.tsx` (if referenced locally)
    - ContentArea's local `FileMeta` interface only has `totalLines` — no change needed unless `isPartial` is used there
    - Verify ContentArea auto-updates scrollbar range when `fileMeta.totalLines` changes via React re-render
    - _Requirements: 6.2_
  - [x] 8.2 Write integration tests for end-to-end partial metadata flow
    - Test: open large file → partial response → lines readable → final response → totalLines updated
    - Test: open large file mid-scan → cancel → new file loads correctly
    - Test: ReadLinesAsync during scan → correct clamped results
    - Create tests in `tests/EditorApp.Tests/Unit/Integration/`
    - _Requirements: 1.1, 1.2, 2.1, 2.3, 3.1, 5.1, 6.1, 9.1_

- [x] 9. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from design doc (Properties 1-6)
- Unit tests validate specific examples and edge cases
- Backend uses C# (xUnit + FsCheck 3.1.0), frontend uses TypeScript/React
- Existing test infrastructure in `tests/EditorApp.Tests/` with Fixtures, Properties, and Unit folders
- `SizeThresholdBytes` constant (256,000) already exists in `FileService.cs` — reuse it
