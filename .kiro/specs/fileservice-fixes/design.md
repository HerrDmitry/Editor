# FileService Fixes — Bugfix Design

## Overview

Eight defects in `FileService.cs` and `CompressedLineIndex.cs` degrade performance, leak memory, and risk data corruption. The fixes span three categories:

1. **Performance & correctness** — cache encoding to eliminate redundant I/O (1.1), detect stale files to prevent corrupt reads (1.3)
2. **Resource management** — add `CloseFile` + `IDisposable` to stop unbounded cache growth and dispose `ReaderWriterLockSlim` (1.2, 1.4), switch to `await using` (1.6)
3. **API & cleanup** — add `CancellationToken` to `ReadLinesAsync` (1.5), replace manual lock with `ConcurrentDictionary` (1.7), remove or document dead `CountLines` (1.8)

All fixes are additive or refactoring — no change to the line-scanning algorithm, delta-encoded compression, or partial-metadata callback behavior.

## Glossary

- **Bug_Condition (C)**: The set of conditions under which the eight defects manifest — redundant I/O, unbounded cache, stale reads, handle leaks, missing cancellation, inconsistent disposal, unnecessary locking complexity, dead code
- **Property (P)**: The desired correct behavior after fixes — cached encoding reuse, bounded cache with eviction, stale-file detection, deterministic disposal, cancellable reads, consistent patterns, simplified concurrency, no dead code
- **Preservation**: Existing behaviors that must remain unchanged — line index accuracy, read correctness, partial metadata callbacks, scan cancellation, encoding detection, compression efficiency, clamping, display names
- **CompressedLineIndex**: Block-based delta-encoded line offset index in `Services/CompressedLineIndex.cs`
- **FileService**: File I/O service in `Services/FileService.cs` that builds line indices and serves line reads
- **_lineIndexCache**: Internal dictionary mapping file paths to their `CompressedLineIndex` instances

## Bug Details

### Bug Condition

The bugs manifest across multiple operations in `FileService` and `CompressedLineIndex`. They share a common theme: resources are acquired but never properly released, and redundant work is performed where caching would suffice.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type FileServiceOperation
  OUTPUT: boolean

  // Defect 1.1: Redundant encoding detection
  IF input.operation == "ReadLinesAsync"
    RETURN true  // Always re-detects encoding

  // Defect 1.2: Unbounded cache growth
  IF input.operation == "OpenFileAsync" AND input.previousFilesOpened > 0
    AND input.noCloseFileCalled == true
    RETURN true  // Cache grows without bound

  // Defect 1.3: Stale offset read
  IF input.operation == "ReadLinesAsync"
    AND input.fileModifiedSinceOpen == true
    RETURN true  // Reads from stale offsets silently

  // Defect 1.4: RWLS not disposed
  IF input.operation == "DisposeIndex"
    AND input.concurrentAccessEnabled == true
    RETURN true  // ReaderWriterLockSlim leaked

  // Defect 1.5: No cancellation on ReadLinesAsync
  IF input.operation == "ReadLinesAsync"
    AND input.callerWantsCancel == true
    RETURN true  // No CancellationToken parameter

  // Defect 1.6: Inconsistent stream disposal
  IF input.operation == "OpenFileAsync"
    RETURN true  // Uses manual finally instead of await using

  // Defect 1.7: Manual lock complexity
  IF input.operation IN ["ReadLinesAsync", "OpenFileAsync"]
    AND input.accessesCache == true
    RETURN true  // Uses lock + Dictionary instead of ConcurrentDictionary

  // Defect 1.8: Dead code
  IF input.operation == "CountLines"
    RETURN true  // Unused method in production

  RETURN false
END FUNCTION
```

### Examples

- **1.1**: `ReadLinesAsync("file.txt", 0, 10)` opens `file.txt` a second time just to read 4 BOM bytes, even though `OpenFileAsync` already detected UTF-8
- **1.2**: Opening 1000 files sequentially without closing any → `_lineIndexCache` holds 1000 `CompressedLineIndex` instances with no way to free them
- **1.3**: `OpenFileAsync("log.txt")` builds index, external process appends to `log.txt`, `ReadLinesAsync("log.txt", 500, 10)` reads from offset that now points mid-line → garbled content returned silently
- **1.4**: `CompressedLineIndex` calls `EnableConcurrentAccess()` creating a `ReaderWriterLockSlim`, index is later replaced in cache → old RWLS handle leaked
- **1.5**: User navigates away from a file being read over NFS; `ReadLinesAsync` blocks indefinitely with no way to cancel
- **1.6**: `OpenFileAsync` uses `try/finally { await stream.DisposeAsync(); }` while `ReadLinesAsync` uses `await using` — inconsistent patterns
- **1.7**: Every cache access wraps `lock (_indexLock) { _lineIndexCache[path] = ... }` when `ConcurrentDictionary` handles this natively
- **1.8**: `CountLines(string)` exists in `FileService` but `OpenFileAsync` counts lines via byte scanning — method appears unused

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Line index construction with correct byte offsets for `\n`, `\r\n`, `\r` line endings
- `ReadLinesAsync` returning correct lines with proper encoding for unmodified files
- Partial metadata callback firing exactly once for files > 256KB
- `OpenFileAsync` cancellation support during scan phase
- `DetectEncoding` correctly identifying UTF-8, UTF-16 LE/BE, UTF-32 LE/BE, and no-BOM files
- Delta-encoded block compression memory efficiency in `CompressedLineIndex`
- `ReadLinesAsync` clamping out-of-range `startLine`/`lineCount` without throwing
- `GetEncodingDisplayName` returning same human-readable names

**Scope:**
All inputs that do NOT involve the eight defect conditions should be completely unaffected by these fixes. This includes:
- Normal file open → read → close lifecycle on unmodified files
- Encoding detection logic (algorithm unchanged, just cached)
- `CompressedLineIndex` block finalization and offset retrieval
- Progress reporting and throttling during large file scans

## Hypothesized Root Cause

Based on the bug descriptions and code analysis:

1. **Redundant encoding detection (1.1)**: `ReadLinesAsync` calls `DetectEncoding(filePath)` on every invocation because encoding was never stored alongside the line index. The cache only maps `string → CompressedLineIndex`, not `string → (CompressedLineIndex, Encoding)`.

2. **Unbounded cache (1.2)**: No `CloseFile` or eviction method exists. `_lineIndexCache` is append-only — entries are added in `OpenFileAsync` but never removed. `CompressedLineIndex` also lacks `IDisposable`, so even if entries were removed, the `ReaderWriterLockSlim` would leak.

3. **Stale offset reads (1.3)**: `ReadLinesAsync` trusts the cached index unconditionally. No file metadata (size, last-write time) is stored during `OpenFileAsync`, so there is no way to detect that the file changed between open and read.

4. **RWLS leak (1.4)**: `CompressedLineIndex` creates a `ReaderWriterLockSlim` in `EnableConcurrentAccess()` but does not implement `IDisposable`. When the index is replaced in the cache or garbage collected, the OS handle leaks.

5. **Missing CancellationToken (1.5)**: `ReadLinesAsync` signature lacks a `CancellationToken` parameter. The `IFileService` interface also omits it. Neither `ReadLineAsync` nor `stream.Seek` receive a token.

6. **Inconsistent disposal (1.6)**: `OpenFileAsync` was written before `ReadLinesAsync` and uses the older `try/finally` pattern. Simple oversight — both should use `await using`.

7. **Manual locking (1.7)**: `_lineIndexCache` is a `Dictionary<string, CompressedLineIndex>` guarded by `lock (_indexLock)`. This was the initial implementation; `ConcurrentDictionary` would simplify the code. Note: the cache value type will change to a tuple/record holding both index and encoding, so the switch to `ConcurrentDictionary` should use the new value type.

8. **Dead code (1.8)**: `CountLines(string)` was likely written for early prototyping or testing. `OpenFileAsync` counts lines via byte scanning, making this method unused in production. It is `internal static`, so it may be used in tests — needs verification before removal.

## Correctness Properties

Property 1: Bug Condition — Cached Encoding Eliminates Redundant I/O

_For any_ file opened via `OpenFileAsync` and subsequently read via `ReadLinesAsync`, the encoding used for reading SHALL be the same encoding detected during `OpenFileAsync`, and no additional file open for BOM detection SHALL occur during `ReadLinesAsync`.

**Validates: Requirements 2.1**

Property 2: Bug Condition — Cache Eviction via CloseFile

_For any_ sequence of `OpenFileAsync` and `CloseFile` calls, calling `CloseFile(path)` SHALL remove the cache entry for that path, dispose the associated `CompressedLineIndex`, and cause subsequent `ReadLinesAsync(path, ...)` to throw `InvalidOperationException`.

**Validates: Requirements 2.2**

Property 3: Bug Condition — Stale File Detection

_For any_ file opened via `OpenFileAsync` where the file is subsequently modified (size or last-write time changed), calling `ReadLinesAsync` SHALL throw an `InvalidOperationException` with a message indicating the file has been modified since it was opened.

**Validates: Requirements 2.3**

Property 4: Bug Condition — CompressedLineIndex Disposes RWLS

_For any_ `CompressedLineIndex` instance where `EnableConcurrentAccess()` was called, invoking `Dispose()` SHALL dispose the internal `ReaderWriterLockSlim`, and subsequent operations on the disposed lock SHALL throw `ObjectDisposedException`.

**Validates: Requirements 2.4**

Property 5: Bug Condition — ReadLinesAsync Supports CancellationToken

_For any_ call to `ReadLinesAsync` with a pre-cancelled `CancellationToken`, the method SHALL throw `OperationCanceledException` without performing file I/O.

**Validates: Requirements 2.5**

Property 6: Preservation — Line Index Accuracy and Read Correctness

_For any_ file with arbitrary content and line endings (`\n`, `\r\n`, `\r`), `OpenFileAsync` followed by `ReadLinesAsync` on an unmodified file SHALL return the exact same lines as the original content, preserving the existing round-trip correctness.

**Validates: Requirements 3.1, 3.2**

Property 7: Preservation — Large File Partial Metadata and Progress

_For any_ file larger than 256KB, `OpenFileAsync` with an `onPartialMetadata` callback SHALL continue to invoke that callback exactly once, and progress reporting SHALL continue to function identically.

**Validates: Requirements 3.3, 3.4**

Property 8: Preservation — Encoding Detection Accuracy

_For any_ file with a BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE) or without a BOM, `DetectEncoding` SHALL continue to return the correct encoding, and `GetEncodingDisplayName` SHALL return the same human-readable names.

**Validates: Requirements 3.5, 3.8**

Property 9: Preservation — ReadLinesAsync Clamping

_For any_ out-of-range `startLine` or `lineCount` values passed to `ReadLinesAsync`, the method SHALL continue to clamp them and return valid results without throwing exceptions.

**Validates: Requirements 3.7**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `src/EditorApp/Services/CompressedLineIndex.cs`

**Changes**:
1. **Implement `IDisposable`**: Add `IDisposable` to `CompressedLineIndex` class declaration. In `Dispose()`, call `_rwLock?.Dispose()` and set `_rwLock = null`.

**File**: `src/EditorApp/Services/FileService.cs`

**Changes**:
1. **Introduce cache entry record**: Create an internal record `CacheEntry(CompressedLineIndex Index, Encoding Encoding, long FileSize, DateTime LastWriteTimeUtc)` to store all metadata alongside the index.

2. **Replace `Dictionary` + lock with `ConcurrentDictionary`**: Change `_lineIndexCache` from `Dictionary<string, CompressedLineIndex>` to `ConcurrentDictionary<string, CacheEntry>`. Remove `_indexLock` field. Update all access sites to use `ConcurrentDictionary` APIs (`TryGetValue`, `TryAdd`, indexer assignment, `TryRemove`).

3. **Cache encoding during `OpenFileAsync`**: Store the detected `Encoding` in the `CacheEntry` alongside the index. Also store `fileInfo.Length` and `fileInfo.LastWriteTimeUtc` for stale detection.

4. **Reuse cached encoding in `ReadLinesAsync`**: Instead of calling `DetectEncoding(filePath)`, retrieve encoding from the `CacheEntry`.

5. **Add stale file detection in `ReadLinesAsync`**: Before reading, check current file size and last-write time against cached values. If different, throw `InvalidOperationException` with descriptive message.

6. **Add `CancellationToken` to `ReadLinesAsync`**: Add optional `CancellationToken cancellationToken = default` parameter. Pass it to `ReadLineAsync()` calls. Check cancellation before I/O.

7. **Switch to `await using` in `OpenFileAsync`**: Replace `try/finally { await stream.DisposeAsync(); }` with `await using var stream = ...`.

8. **Add `CloseFile` method**: Add `CloseFile(string filePath)` that removes the entry from `ConcurrentDictionary` via `TryRemove` and calls `Dispose()` on the removed `CompressedLineIndex`.

9. **Handle `CountLines`**: Add XML doc comment marking it as a test utility, or move to test project if unused in production.

**File**: `src/EditorApp/Services/IFileService.cs`

**Changes**:
1. **Update `ReadLinesAsync` signature**: Add `CancellationToken cancellationToken = default` parameter.
2. **Add `CloseFile` method**: Add `void CloseFile(string filePath)` to interface.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fixes work correctly and preserve existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests targeting each defect on the UNFIXED code to observe failures and confirm root causes.

**Test Cases**:
1. **Redundant Encoding I/O Test**: Open a file, then call `ReadLinesAsync` — observe that `DetectEncoding` is called again (will show redundant file open on unfixed code)
2. **Cache Growth Test**: Open N files, verify `_lineIndexCache.Count == N` with no way to reduce it (will confirm unbounded growth on unfixed code)
3. **Stale Read Test**: Open file, modify file externally, call `ReadLinesAsync` — observe garbled/incorrect content returned silently (will fail on unfixed code)
4. **RWLS Leak Test**: Create `CompressedLineIndex`, enable concurrent access, attempt to dispose — observe no `Dispose` method exists (will fail on unfixed code)
5. **Missing CancellationToken Test**: Attempt to pass `CancellationToken` to `ReadLinesAsync` — observe signature doesn't accept it (will fail to compile on unfixed code)

**Expected Counterexamples**:
- `ReadLinesAsync` opens file twice (once for BOM, once for content)
- Cache count only increases, never decreases
- Modified file returns wrong line content without error
- `CompressedLineIndex` does not implement `IDisposable`

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := fixedFileService(input)
  ASSERT expectedBehavior(result)
END FOR
```

Specifically:
- For encoding caching: verify `ReadLinesAsync` never calls `DetectEncoding`
- For cache eviction: verify `CloseFile` removes entry and disposes index
- For stale detection: verify modified file → exception
- For RWLS disposal: verify `Dispose()` cleans up lock
- For cancellation: verify cancelled token → `OperationCanceledException`

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT originalFileService(input) = fixedFileService(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many file contents with random line endings and lengths
- It catches edge cases in offset calculation that manual tests miss
- It provides strong guarantees that line reading behavior is unchanged

**Test Plan**: Existing property tests (`FileServiceProperties.cs`, `CompressedLineIndexProperties.cs`) already cover core preservation. Run them after fixes to confirm no regressions. Add targeted preservation tests for new code paths.

**Test Cases**:
1. **Line Round-Trip Preservation**: For any file with random content, `OpenFileAsync` + `ReadLinesAsync` returns exact original lines (existing `LineIndexRoundTrip` property)
2. **Total Lines Preservation**: For any file, `OpenFileAsync.TotalLines` matches actual line count (existing `TotalLinesAccuracy` property)
3. **Range Read Preservation**: For any valid range, `ReadLinesAsync` returns correct slice (existing `ReadLinesRangeCorrectness` property)
4. **Partial Metadata Preservation**: For large files, callback fires exactly once (existing `PartialMetadataCallbackFiresExactlyOnceForLargeFiles` property)
5. **Clamping Preservation**: For out-of-range inputs, results are clamped correctly (existing `PartialIndexReadClampingAndTotalLinesAccuracy` property)
6. **Encoding Round-Trip Preservation**: For files with various BOMs, cached encoding produces same read results as direct detection

### Unit Tests

- Test `CloseFile` removes cache entry and disposes index
- Test `CloseFile` on non-existent path is no-op
- Test `ReadLinesAsync` after `CloseFile` throws `InvalidOperationException`
- Test stale file detection throws when file size changes
- Test stale file detection throws when last-write time changes
- Test `ReadLinesAsync` with pre-cancelled token throws `OperationCanceledException`
- Test `CompressedLineIndex.Dispose()` disposes `ReaderWriterLockSlim`
- Test `CompressedLineIndex.Dispose()` is safe when no RWLS was created
- Test `CacheEntry` stores correct encoding, file size, and last-write time

### Property-Based Tests

- Generate random file contents → verify `OpenFileAsync` + `ReadLinesAsync` round-trip still works with cached encoding (preservation of Property 6)
- Generate random sequences of open/close → verify cache size equals expected count (Property 2)
- Generate files with various encodings → verify cached encoding matches direct detection (Property 1)
- Generate random line ranges → verify clamping still works (Property 9)

### Integration Tests

- Full lifecycle: open file → read lines → close file → verify cleanup
- Stale detection: open file → modify externally → read → verify exception → reopen → read succeeds
- Cancellation: open file → start read with cancellation → cancel → verify exception
- Large file: open large file → verify partial metadata → read → close → verify disposal
