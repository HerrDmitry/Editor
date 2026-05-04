# Implementation Plan

## Overview

Fix 8 thread safety issues in PhotinoHostService.cs using minimal synchronization: lock for CTS operations, volatile for path field, delegate storage for event unsubscribe, timer replace-then-dispose pattern, path clear-at-start pattern, and platform-safe FSW config.

**Bug Condition**: Concurrent access to shared mutable state without proper synchronization, or resources not properly cleaned up.

**Expected Behavior**: Thread-safe operations with no exceptions, leaks, or stale data.

**Preservation**: Existing file open, refresh, shutdown, and line request behavior unchanged.

---

## Phase 1: Bug Condition Exploration Tests

- [x] 1. Write CTS race condition exploration test
  - **Property 1: Bug Condition** - CTS Race Condition
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples demonstrating ObjectDisposedException/NullReferenceException on concurrent CTS access
  - **Scoped PBT Approach**: Simulate concurrent threads: one disposes `_refreshCts`, another cancels it
  - Test: Create service, spawn Task A that calls OpenFileByPathAsync (disposes _refreshCts), Task B that triggers OnDebouncedFileChange (cancels _refreshCts)
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS with ObjectDisposedException or NullReferenceException (proves bug exists)
  - Document counterexamples found (e.g., "ObjectDisposedException thrown when Cancel() called after Dispose()")
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 2.1_

- [x] 2. Write path race condition exploration test
  - **Property 1: Bug Condition** - Path Race Condition
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples demonstrating stale path reads
  - **Scoped PBT Approach**: Writer thread updates `_currentFilePath`, reader thread reads in HandleRequestLinesAsync
  - Test: Create service, set _currentFilePath via reflection, spawn concurrent reader/writer tasks, verify consistent reads
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test may FAIL intermittently (race condition non-deterministic) - document any observed inconsistency
  - Document counterexamples found (e.g., "Read returned null while write in progress")
  - Mark task complete when test is written, run, and behavior documented
  - _Requirements: 1.2, 2.2_

- [x] 3. Write event subscription leak exploration test
  - **Property 1: Bug Condition** - Event Subscription Leak
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples demonstrating memory leak from unsubscribed event
  - **Scoped PBT Approach**: Create service, shutdown, create new service instance, trigger OnStaleFileDetected, count handlers
  - Test: Create FileService mock with handler count, create PhotinoHostService, shutdown, create new instance, verify handler count = 1 (not 2)
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS - handler count = 2 (duplicate subscription proves leak)
  - Document counterexamples found (e.g., "After shutdown and recreation, OnStaleFileDetected has 2 handlers")
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.3, 2.3_

- [x] 4. Write timer disposal race exploration test
  - **Property 1: Bug Condition** - Timer Disposal Race
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples demonstrating timer callback race condition
  - **Scoped PBT Approach**: Rapidly trigger OnFileChanged, verify no NullReferenceException or callback on disposed timer
  - Test: Create service, trigger multiple OnFileChanged calls in rapid succession, verify no exceptions
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test may FAIL intermittently - race condition non-deterministic
  - Document counterexamples found (e.g., "NullReferenceException when callback fires after dispose")
  - Mark task complete when test is written, run, and behavior documented
  - _Requirements: 1.4, 2.4_

---

## Phase 2: Preservation Property Tests

- [x] 5. Write file open preservation test
  - **Property 2: Preservation** - File Open Behavior
  - **IMPORTANT**: Follow observation-first methodology
  - Observe: Successful file open sends FileOpenedResponse with correct metadata, starts file watching
  - Write property-based test: For any valid file path, OpenFileByPathAsync sends same metadata and starts watcher
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1_

- [x] 6. Write refresh preservation test
  - **Property 2: Preservation** - File Change Detection
  - **IMPORTANT**: Follow observation-first methodology
  - Observe: File change triggers debounce, then refresh sends FileOpenedResponse with IsRefresh=true
  - Write property-based test: For any file change event, refresh behavior matches original
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.2_

- [x] 7. Write shutdown preservation test
  - **Property 2: Preservation** - Shutdown Behavior
  - **IMPORTANT**: Follow observation-first methodology
  - Observe: Shutdown cancels all CTS, disposes resources, stops watcher
  - Write property-based test: For any state, shutdown cleans up all resources
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.3_

- [x] 8. Write line request preservation test
  - **Property 2: Preservation** - Line Request Behavior
  - **IMPORTANT**: Follow observation-first methodology
  - Observe: RequestLinesMessage reads lines from current file, returns LinesResponse
  - Write property-based test: For any valid line range request, response matches original behavior
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.4_

---

## Phase 3: Implementation

- [x] 9. Fix thread safety issues

  - [x] 9.1 Add CTS synchronization with lock
    - Add `private readonly object _ctsLock = new();` field
    - Wrap all `_refreshCts` and `_scanCts` dispose/create operations with `lock (_ctsLock)`
    - In OpenFileByPathAsync: lock around `_refreshCts?.Cancel(); _refreshCts?.Dispose(); _refreshCts = null;`
    - In OnDebouncedFileChange: lock around `_refreshCts?.Dispose(); _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);`
    - _Bug_Condition: isBugCondition(input) where input.operation = "DisposeRefreshCts" AND input.concurrentOperation = "CancelRefreshCts"_
    - _Expected_Behavior: No ObjectDisposedException or NullReferenceException on concurrent CTS access_
    - _Preservation: Existing CTS cancellation and disposal behavior unchanged_
    - _Requirements: 1.1, 2.1_

  - [x] 9.2 Mark _currentFilePath as volatile
    - Change `private string? _currentFilePath;` to `private volatile string? _currentFilePath;`
    - Ensures visibility across threads for read/write operations
    - _Bug_Condition: isBugCondition(input) where input.operation = "WritePath" AND input.concurrentOperation = "ReadPath"_
    - _Expected_Behavior: Consistent reads without torn values_
    - _Preservation: Path storage and retrieval behavior unchanged_
    - _Requirements: 1.2, 2.2_

  - [x] 9.3 Store _staleFileHandler delegate and unsubscribe in Shutdown
    - Add `private EventHandler<string>? _staleFileHandler;` field
    - In RegisterMessageHandlers: `_staleFileHandler = (path) => { ... }; fs.OnStaleFileDetected += _staleFileHandler;`
    - In Shutdown: `if (_fileService is FileService fs && _staleFileHandler is not null) { fs.OnStaleFileDetected -= _staleFileHandler; }`
    - _Bug_Condition: isBugCondition(input) where input.operation = "Shutdown" AND NOT input.eventUnsubscribed_
    - _Expected_Behavior: No memory leak, no duplicate subscriptions on service recreation_
    - _Preservation: Event handling during normal operation unchanged_
    - _Requirements: 1.3, 2.3_

  - [x] 9.4 Add timer synchronization with replace-then-dispose pattern
    - Add `private readonly object _timerLock = new();` field
    - In OnFileChanged: `System.Threading.Timer? oldTimer; lock (_timerLock) { oldTimer = _debounceTimer; _debounceTimer = new System.Threading.Timer(...); } oldTimer?.Dispose();`
    - In StopWatching: dispose timer with lock if needed
    - _Bug_Condition: isBugCondition(input) where input.operation = "DisposeTimer" AND input.concurrentOperation = "TimerCallback"_
    - _Expected_Behavior: No race condition between timer disposal and callback_
    - _Preservation: Debounce timing behavior unchanged_
    - _Requirements: 1.4, 2.4_

  - [x] 9.5 Clear _currentFilePath at start of OpenFileByPathAsync
    - Add `_currentFilePath = null;` at start of OpenFileByPathAsync try block
    - Remove `_currentFilePath = filePath;` from partial metadata callback (onPartialMetadata)
    - Keep `_currentFilePath = filePath;` only on success path after file scan
    - _Bug_Condition: isBugCondition(input) where input.operation = "SetPathInCallback" AND input.fileScanFails_
    - _Expected_Behavior: Path null on failure, set only on success_
    - _Preservation: Path set correctly on successful file open_
    - _Requirements: 1.5, 2.5_

  - [x] 9.6 Add thread-safety comment for Progress callback
    - Add comment: `// Progress<T> callback runs on thread pool. SendToUIAsync is thread-safe (stateless, delegates to Photino native SendWebMessage).`
    - No code change required - MessageRouter.SendToUIAsync verified thread-safe
    - _Bug_Condition: isBugCondition(input) where Progress<T> callback invokes SendToUIAsync from thread pool_
    - _Expected_Behavior: Thread-safe SendToUIAsync calls_
    - _Preservation: Progress reporting behavior unchanged_
    - _Requirements: 1.6, 2.6_

  - [x] 9.7 Remove NotifyFilters.Size from FileSystemWatcher
    - Change `NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size` to `NotifyFilter = NotifyFilters.LastWrite`
    - Ensures cross-platform compatibility (Size unsupported on some platforms)
    - _Bug_Condition: isBugCondition(input) where input.platform NOT SUPPORTS "NotifyFilters.Size" AND input.operation = "CreateFSW"_
    - _Expected_Behavior: No platform exception on FSW creation_
    - _Preservation: File change detection via LastWrite unchanged_
    - _Requirements: 1.7, 2.7_

  - [x] 9.8 Verify bug condition exploration tests now pass
    - **Property 1: Expected Behavior** - Thread-Safe Operations
    - **IMPORTANT**: Re-run the SAME tests from tasks 1-4 - do NOT write new tests
    - The tests from tasks 1-4 encode the expected behavior
    - When these tests pass, it confirms the expected behavior is satisfied
    - Run all exploration tests (CTS race, path race, event leak, timer race)
    - **EXPECTED OUTCOME**: All tests PASS (confirms bugs are fixed)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 9.9 Verify preservation tests still pass
    - **Property 2: Preservation** - Unchanged Behavior
    - **IMPORTANT**: Re-run the SAME tests from tasks 5-8 - do NOT write new tests
    - Run all preservation tests (file open, refresh, shutdown, line request)
    - **EXPECTED OUTCOME**: All tests PASS (confirms no regressions)
    - Confirm all tests still pass after fix (no regressions)

---

## Phase 4: Checkpoint

- [x] 10. Checkpoint - Ensure all tests pass
  - Run full test suite: `dotnet test tests/EditorApp.Tests`
  - Verify all exploration tests pass (bugs fixed)
  - Verify all preservation tests pass (no regressions)
  - If any tests fail, investigate and fix
  - Ask the user if questions arise
