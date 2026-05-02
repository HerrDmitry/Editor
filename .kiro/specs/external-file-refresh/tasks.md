# Implementation Plan: External File Refresh

## Overview

Implement transparent file refresh when externally modified: FileSystemWatcher + debounce in PhotinoHostService, RefreshFileAsync in FileService, stale detection replacement, IsRefresh flag on FileOpenedResponse, and frontend scroll-preserving refresh handler.

## Tasks

- [x] 1. Add IsRefresh field to FileOpenedResponse and FileMeta
  - [x] 1.1 Add `IsRefresh` property to `FileOpenedResponse` in `Messages.cs`
    - Add `[JsonPropertyName("isRefresh")] public bool IsRefresh { get; set; }` to `FileOpenedResponse`
    - _Requirements: 4.2_
  - [x] 1.2 Add `isRefresh` field to `FileMeta` interface in `InteropService.ts`
    - Add `isRefresh: boolean` to the `FileMeta` interface
    - _Requirements: 4.2_

- [x] 2. Implement RefreshFileAsync in FileService
  - [x] 2.1 Add `RefreshFileAsync` method to `IFileService.cs`
    - Add method signature: `Task<FileOpenMetadata> RefreshFileAsync(string filePath, IProgress<FileLoadProgress>? progress = null, CancellationToken cancellationToken = default)`
    - _Requirements: 3.1, 3.2_
  - [x] 2.2 Implement `RefreshFileAsync` in `FileService.cs`
    - Delegate to `OpenFileAsync` with `onPartialMetadata: null`
    - Old CacheEntry serves reads via ConcurrentDictionary atomic swap until new entry replaces it
    - _Requirements: 3.1, 3.2, 3.3_
  - [x] 2.3 Add `OnStaleFileDetected` event to `FileService.cs`
    - Add `public event Action<string>? OnStaleFileDetected;`
    - _Requirements: 7.1_
  - [x] 2.4 Modify `ReadLinesAsync` stale detection in `FileService.cs`
    - Remove `InvalidOperationException` throw for stale file
    - Fire `OnStaleFileDetected(filePath)` when stale detected
    - Serve read from existing (stale) cache entry instead of throwing
    - _Requirements: 7.1, 7.3_
  - [x] 2.5 Write property test: Refresh produces correct index (P3)
    - **Property 3: Refresh produces correct index matching modified file**
    - Generate random file content, open, modify with new random content, refresh. Verify index matches modified content.
    - **Validates: Requirements 3.1, 3.2**
  - [ ]* 2.6 Write property test: Old cache serves reads during refresh (P4)
    - **Property 4: Old cache serves reads during refresh**
    - Open file, start refresh with artificial delay, call ReadLinesAsync concurrently. Verify returns old data without exception.
    - **Validates: Requirements 3.3**
  - [ ]* 2.7 Write property test: Stale detection triggers event instead of exception (P7)
    - **Property 7: Stale detection triggers event instead of exception**
    - Open file, modify externally (change size/timestamp), call ReadLinesAsync. Verify no exception + event fired.
    - **Validates: Requirements 7.1, 7.3**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement FileSystemWatcher and debounce in PhotinoHostService
  - [x] 4.1 Add FileSystemWatcher fields and StartWatching/StopWatching methods
    - Add `_fileWatcher`, `_debounceTimer`, `DebounceMs` constant (500)
    - Implement `StartWatching(string filePath)` — create watcher for file, subscribe Changed + Error events
    - Implement `StopWatching()` — dispose watcher + timer
    - _Requirements: 1.1, 1.4_
  - [x] 4.2 Add `OnFileChanged` and `OnDebouncedFileChange` methods
    - Add `_refreshInProgress` field (`int`, uses `Interlocked.CompareExchange` guard)
    - Add `_pendingRefresh` field (`volatile bool`)
    - `OnFileChanged` resets debounce timer to 500ms
    - `OnDebouncedFileChange` uses `Interlocked.CompareExchange(ref _refreshInProgress, 1, 0)` — if refresh already running, set `_pendingRefresh = true` and return (do NOT cancel `_scanCts`)
    - When no refresh in progress: set guard, call `RefreshFileAsync`, send `FileOpenedResponse` with `IsRefresh = true`
    - In `finally` block: `Interlocked.Exchange(ref _refreshInProgress, 0)`, then check `_pendingRefresh` — if true, clear flag and start new debounce timer (500ms) to trigger another cycle
    - Only `OpenFileByPathAsync` (different file) cancels `_scanCts` — same-file changes never cancel in-progress refresh
    - Handle `OperationCanceledException` silently, `FileNotFoundException` → ErrorResponse
    - _Requirements: 2.1, 2.2, 4.1, 8.1, 8.2, 8.3_
  - [x] 4.3 Add `OnWatcherError` handler
    - Log warning, rely on stale detection as fallback
    - _Requirements: 1.5_
  - [x] 4.4 Wire StartWatching into `OpenFileByPathAsync`
    - Call `StartWatching(filePath)` after successful file open
    - Previous watcher disposed automatically by StartWatching
    - _Requirements: 1.1, 1.3_
  - [x] 4.5 Wire StopWatching into `Shutdown`
    - Call `StopWatching()` in Shutdown method
    - _Requirements: 1.4_
  - [x] 4.6 Subscribe to `OnStaleFileDetected` event
    - In constructor or RegisterMessageHandlers, subscribe to FileService.OnStaleFileDetected
    - Route through OnFileChanged to trigger debounced refresh
    - _Requirements: 7.1_
  - [ ]* 4.7 Write property test: Debounce coalesces rapid events (P1)
    - **Property 1: Debounce coalesces rapid events into single refresh**
    - Generate random list of timestamps within 500ms window (1-50 events). Feed to debounce logic. Verify exactly 1 output trigger.
    - **Validates: Requirements 2.1**
  - [ ]* 4.8 Write property test: Non-interruptible refresh with pending flag (P2)
    - **Property 2: Non-interruptible refresh with pending flag**
    - Generate random delay before interrupting change event. Verify old CTS NOT cancelled, `_pendingRefresh` set to true, current refresh completes normally, then new debounce cycle starts after completion.
    - **Validates: Requirements 2.2, 8.2**

- [x] 5. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement frontend refresh handler
  - [x] 6.1 Add refresh handling to `onFileOpened` callback in `App.tsx`
    - Check `data.isRefresh` flag first in the callback
    - Preserve `linesStartLine` and scroll offset
    - Update `fileMeta` with new metadata (totalLines may change)
    - Clear error state
    - Handle file emptied case (totalLines === 0)
    - Clamp `linesStartLine` if file shrunk past current position: `newStart = max(0, newTotalLines - bufferLen)`
    - Re-request current buffer range via `sendRequestLines`
    - Set `isJumpRequestRef.current = true` to replace buffer entirely
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 6.1_
  - [ ]* 6.2 Write property test: Frontend preserves scroll position on refresh (P5)
    - **Property 5: Frontend preserves scroll position and re-requests buffer on refresh**
    - Generate random linesStartLine, buffer length, totalLines (>= start+length). Simulate refresh handler. Verify start preserved and request matches.
    - **Validates: Requirements 5.1, 5.2**
  - [ ]* 6.3 Write property test: Clamp on file shrink (P6)
    - **Property 6: Clamp on file shrink**
    - Generate random linesStartLine, bufferLength, newTotalLines (< linesStartLine). Verify clamped start = max(0, newTotalLines - bufferLength).
    - **Validates: Requirements 5.4**

- [x] 7. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Bug fixes: memory leak, process exit, small file refresh
  - [x] 8.1 Use `FileShare.ReadWrite` in `OpenFileAsync` and `ReadLinesAsync`
    - Change `FileShare.Read` to `FileShare.ReadWrite` in both FileStream constructors
    - Prevents blocking/failure when another process is writing to the file
    - _Requirements: 3.5_
  - [x] 8.2 Dispose old `CompressedLineIndex` on cache swap in `OpenFileAsync`
    - Before overwriting `_lineIndexCache[filePath]`, check if old entry exists with different index and dispose it
    - Prevents memory leak from accumulating orphaned indexes during repeated refreshes
    - _Requirements: 3.2_
  - [x] 8.3 Add `_shutdownCts` master cancellation and `_refreshCts` separation
    - Add `_shutdownCts` (readonly, cancelled in Shutdown) and `_refreshCts` (linked to shutdown)
    - Separate refresh CTS from file-open `_scanCts` to prevent interference
    - `Shutdown()` cancels all CTS + disposes all resources
    - Guard `OnFileChanged` and `OnDebouncedFileChange` with `_shutdownCts.IsCancellationRequested`
    - `OpenFileByPathAsync` cancels both `_scanCts` and `_refreshCts`, clears `_pendingRefresh`
    - _Requirements: 8.4, 8.5, 8.6_
  - [x] 8.4 Use `Math.max(currentCount, APP_FETCH_SIZE)` for refresh buffer size in `App.tsx`
    - Changed from `currentCount || APP_FETCH_SIZE` to `Math.max(currentCount, APP_FETCH_SIZE)`
    - Ensures growing small files (e.g. 3-line log) request at least 200 lines on refresh
    - _Requirements: 5.2_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- C# property tests use FsCheck 3.1.0 in `tests/EditorApp.Tests/`
- Frontend property tests use fast-check + vitest in `tests/frontend/`
- Checkpoints ensure incremental validation
- ConcurrentDictionary atomic swap means no explicit lock needed for cache updates during refresh
