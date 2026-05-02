# Implementation Plan

- [x] 1. Write bug condition exploration tests
  - **Property 1: Bug Condition** — FileService Defects Exploration
  - **CRITICAL**: Write these tests BEFORE implementing any fixes
  - **GOAL**: Surface counterexamples that demonstrate the 8 defects exist on unfixed code
  - **Scoped PBT Approach**: Each sub-property targets a concrete defect condition
  - Tests to write (all in a new `FileServiceBugConditionTests.cs`):
    - **1.1 Redundant encoding I/O**: Open file via `OpenFileAsync`, then call `ReadLinesAsync` — use reflection to confirm `DetectEncoding` is called again (cache stores `CompressedLineIndex` only, no `Encoding`). Assert that cache value type does NOT contain encoding info → confirms bug exists
    - **1.2 Unbounded cache**: Open N files, verify `_lineIndexCache.Count == N` with no removal mechanism. Attempt to find a `CloseFile` method via reflection → assert it does not exist → confirms unbounded growth
    - **1.3 Stale offset read**: Open file, modify file externally (append content), call `ReadLinesAsync` — observe no exception thrown despite stale offsets → confirms silent corruption
    - **1.4 RWLS not disposed**: Create `CompressedLineIndex`, call `EnableConcurrentAccess()`, assert class does NOT implement `IDisposable` → confirms leak
    - **1.5 Missing CancellationToken**: Assert `ReadLinesAsync` on `IFileService` has no `CancellationToken` parameter via reflection → confirms missing cancellation
    - **1.6 Inconsistent disposal**: Inspect `OpenFileAsync` source via reflection for `await using` pattern — this is a code-level observation (document in test as known)
    - **1.7 Manual lock**: Assert `_lineIndexCache` field type is `Dictionary<string, CompressedLineIndex>` not `ConcurrentDictionary` via reflection → confirms manual locking
    - **1.8 Dead code**: Assert `CountLines` method exists on `FileService` via reflection → confirms dead code present
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests FAIL (this confirms the bugs exist — e.g., stale read returns no exception, no IDisposable, no CloseFile, no CancellationToken)
  - Document counterexamples found to understand root causes
  - Mark task complete when tests are written, run, and failures documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** — FileService Existing Behavior Preservation
  - **IMPORTANT**: Follow observation-first methodology
  - **IMPORTANT**: Run on UNFIXED code to establish baseline
  - Tests to write (in a new `FileServicePreservationProperties.cs`):
    - **Line round-trip**: For any file with random content and mixed line endings (`\n`, `\r\n`, `\r`), `OpenFileAsync` + `ReadLinesAsync` returns exact original lines (property-based with FsCheck, random content generation)
    - **Encoding detection accuracy**: For files with various BOMs (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE, no BOM), `DetectEncoding` returns correct encoding and `GetEncodingDisplayName` returns same human-readable names
    - **Clamping correctness**: For any file with N lines and any (startLine, lineCount) pair including negatives and beyond-end values, `ReadLinesAsync` clamps without throwing and returns valid results with correct `TotalLines`
    - **Partial metadata callback**: For files > 256KB, `onPartialMetadata` fires exactly once during `OpenFileAsync`
    - **Scan cancellation**: `OpenFileAsync` with cancelled token throws `OperationCanceledException`
  - Observe behavior on UNFIXED code for non-buggy inputs
  - Write property-based tests capturing observed behavior patterns from Preservation Requirements
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.7, 3.8_

- [x] 3. Fix FileService defects

  - [x] 3.1 Add `IDisposable` to `CompressedLineIndex`
    - Add `: IDisposable` to class declaration
    - Implement `Dispose()`: call `_rwLock?.Dispose()`, set `_rwLock = null`
    - Safe no-op when no RWLS was created
    - _Bug_Condition: isBugCondition(input) where input.operation == "DisposeIndex" AND input.concurrentAccessEnabled == true_
    - _Expected_Behavior: Dispose() disposes ReaderWriterLockSlim, subsequent ops on disposed lock throw ObjectDisposedException_
    - _Preservation: CompressedLineIndex block finalization, offset retrieval, delta encoding unchanged_
    - _Requirements: 2.4, 3.6_

  - [x] 3.2 Introduce `CacheEntry` record and switch to `ConcurrentDictionary`
    - Create `internal record CacheEntry(CompressedLineIndex Index, Encoding Encoding, long FileSize, DateTime LastWriteTimeUtc)`
    - Replace `Dictionary<string, CompressedLineIndex> _lineIndexCache` → `ConcurrentDictionary<string, CacheEntry>`
    - Remove `_indexLock` field
    - Update `OpenFileAsync`: store `CacheEntry` with index, encoding, fileInfo.Length, fileInfo.LastWriteTimeUtc
    - Update `ReadLinesAsync`: use `TryGetValue` on `ConcurrentDictionary`, extract index + encoding from `CacheEntry`
    - _Bug_Condition: isBugCondition(input) where input.accessesCache == true — manual lock + Dictionary_
    - _Expected_Behavior: ConcurrentDictionary handles thread safety natively, no manual lock needed_
    - _Preservation: All cache access patterns produce same results_
    - _Requirements: 2.7, 2.1_

  - [x] 3.3 Cache encoding and add stale file detection in `ReadLinesAsync`
    - Reuse `CacheEntry.Encoding` instead of calling `DetectEncoding(filePath)` in `ReadLinesAsync`
    - Before reading, check current `FileInfo.Length` and `FileInfo.LastWriteTimeUtc` against `CacheEntry.FileSize` and `CacheEntry.LastWriteTimeUtc`
    - If different → throw `InvalidOperationException` with message indicating file modified since open
    - _Bug_Condition: isBugCondition(input) where input.operation == "ReadLinesAsync" AND (redundant encoding OR fileModifiedSinceOpen)_
    - _Expected_Behavior: Cached encoding reused, stale file → exception_
    - _Preservation: Unmodified files read identically_
    - _Requirements: 2.1, 2.3_

  - [x] 3.4 Add `CancellationToken` to `ReadLinesAsync`
    - Update `IFileService.ReadLinesAsync` signature: add `CancellationToken cancellationToken = default`
    - Update `FileService.ReadLinesAsync` implementation: add parameter, check `cancellationToken.ThrowIfCancellationRequested()` before I/O, pass token to `ReadLineAsync` calls
    - _Bug_Condition: isBugCondition(input) where input.operation == "ReadLinesAsync" AND input.callerWantsCancel == true_
    - _Expected_Behavior: Pre-cancelled token → OperationCanceledException without file I/O_
    - _Preservation: Calls without token behave identically (default parameter)_
    - _Requirements: 2.5_

  - [x] 3.5 Switch `OpenFileAsync` to `await using` and add `CloseFile`
    - Replace `try/finally { await stream.DisposeAsync(); }` with `await using var stream = ...` in `OpenFileAsync`
    - Add `CloseFile(string filePath)` to `IFileService` interface
    - Implement `CloseFile` in `FileService`: `TryRemove` from `ConcurrentDictionary`, call `Dispose()` on removed `CompressedLineIndex`
    - No-op if path not in cache
    - _Bug_Condition: isBugCondition(input) where input.operation == "OpenFileAsync" (inconsistent disposal) OR input.previousFilesOpened > 0 AND noCloseFileCalled (unbounded cache)_
    - _Expected_Behavior: await using for consistent disposal, CloseFile removes + disposes entry_
    - _Preservation: OpenFileAsync scan behavior unchanged, just disposal pattern_
    - _Requirements: 2.2, 2.6_

  - [x] 3.6 Handle `CountLines` dead code
    - Add XML doc comment marking `CountLines` as test utility, or remove if confirmed unused
    - Verify no production callers exist
    - _Bug_Condition: isBugCondition(input) where input.operation == "CountLines" — dead code_
    - _Expected_Behavior: Method documented or removed_
    - _Requirements: 2.8_

  - [x] 3.7 Verify bug condition exploration tests now pass
    - **Property 1: Expected Behavior** — FileService Defects Fixed
    - **IMPORTANT**: Re-run the SAME tests from task 1 — do NOT write new tests
    - The tests from task 1 encode expected behavior (IDisposable exists, CloseFile exists, CancellationToken accepted, ConcurrentDictionary used, stale detection throws, encoding cached)
    - Run bug condition exploration tests from step 1
    - **EXPECTED OUTCOME**: Tests PASS (confirms all 8 defects are fixed)
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_

  - [x] 3.8 Verify preservation tests still pass
    - **Property 2: Preservation** — FileService Existing Behavior Preserved
    - **IMPORTANT**: Re-run the SAME tests from task 2 — do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm line round-trip, encoding detection, clamping, partial metadata, scan cancellation all unchanged
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.7, 3.8_

- [x] 4. Checkpoint — Ensure all tests pass
  - Run full test suite: `dotnet test tests/EditorApp.Tests`
  - Verify build: `dotnet build src/EditorApp`
  - Ensure all bug condition tests pass (defects fixed)
  - Ensure all preservation tests pass (no regressions)
  - Ensure all existing tests pass (CompressedLineIndexProperties, CompressedLineIndexTests, integration tests, unit tests)
  - Ask user if questions arise
