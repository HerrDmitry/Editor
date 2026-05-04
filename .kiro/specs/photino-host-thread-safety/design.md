# Photino Host Thread Safety Bugfix Design

## Overview

PhotinoHostService.cs has 8 thread safety issues causing race conditions, memory leaks, and cross-platform failures. Fix uses minimal synchronization primitives: `lock` for CTS operations, `volatile` for path field, delegate storage for event unsubscribe, timer replace-then-dispose pattern, path clear-at-start pattern, and platform-safe FSW config.

## Glossary

- **Bug_Condition (C)**: Concurrent access to shared mutable state in PhotinoHostService
- **Property (P)**: Thread-safe operations with no exceptions, leaks, or stale data
- **Preservation**: Existing file open, refresh, shutdown, and line request behavior unchanged
- **_refreshCts**: CancellationTokenSource for refresh operations, disposed/recreated concurrently
- **_currentFilePath**: File path field read/written from multiple threads without synchronization
- **OnStaleFileDetected**: Event on FileService that lambda subscribes to without unsubscribe
- **_debounceTimer**: Timer disposed and recreated in OnFileChanged without synchronization

## Bug Details

### Bug Condition

Bug manifests when concurrent threads access shared mutable state without proper synchronization, or when resources are not properly cleaned up.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type ThreadOperation
  OUTPUT: boolean
  
  // CTS race condition
  IF input.operation = "DisposeRefreshCts" AND input.concurrentOperation = "CancelRefreshCts" THEN
    RETURN true
  END IF
  
  // Path race condition
  IF input.operation = "WritePath" AND input.concurrentOperation = "ReadPath" THEN
    RETURN true
  END IF
  
  // Event leak
  IF input.operation = "Shutdown" AND NOT input.eventUnsubscribed THEN
    RETURN true
  END IF
  
  // Timer race
  IF input.operation = "DisposeTimer" AND input.concurrentOperation = "TimerCallback" THEN
    RETURN true
  END IF
  
  // Stale path
  IF input.operation = "SetPathInCallback" AND input.fileScanFails THEN
    RETURN true
  END IF
  
  // FSW platform issue
  IF input.platform NOT SUPPORTS "NotifyFilters.Size" AND input.operation = "CreateFSW" THEN
    RETURN true
  END IF
  
  RETURN false
END FUNCTION
```

### Examples

- Thread A disposes `_refreshCts` while Thread B calls `_refreshCts.Cancel()` → ObjectDisposedException
- Thread A writes `_currentFilePath = "/path/a"` while Thread B reads it in HandleRequestLinesAsync → stale or torn read
- Service shutdown without unsubscribing from `OnStaleFileDetected` → memory leak, duplicate event handling on restart
- Dispose `_debounceTimer` while callback executing → callback may use disposed timer state
- Partial metadata callback sets `_currentFilePath`, then file scan fails → path points to non-loaded file
- FSW created with `NotifyFilters.Size` on macOS/Linux → platform exception

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- File open operation sends metadata to UI and starts file watching (3.1)
- File change detection debounces and triggers refresh cycle (3.2)
- Shutdown cancels all background operations and disposes resources (3.3)
- Line range request reads and returns requested lines from current file (3.4)
- External file deletion/move reports file not found error to UI (3.5)

**Scope:**
All inputs that do NOT involve concurrent access patterns should produce identical results after fix. Single-threaded execution paths unchanged. Public API contracts preserved.

## Hypothesized Root Cause

Based on code analysis, issues stem from:

1. **CTS Synchronization Gap**: `_refreshCts.Dispose()` and `_refreshCts = new CancellationTokenSource()` executed without lock while `_refreshCts.Cancel()` may run concurrently on another thread
   - OnDebouncedFileChange disposes/recreates `_refreshCts`
   - OpenFileByPathAsync cancels/disposes `_refreshCts` and `_scanCts`
   - No synchronization primitive guards these operations

2. **Path Field Volatility Gap**: `_currentFilePath` read/written without `volatile` or lock
   - Set in partial metadata callback, set again on success
   - Read in HandleRequestLinesAsync
   - Torn reads possible on some architectures

3. **Event Subscription Leak**: Lambda subscribes to `OnStaleFileDetected` without storage or unsubscribe
   - `fs.OnStaleFileDetected += (path) => { ... }` in RegisterMessageHandlers
   - Shutdown does not unsubscribe
   - Service recreation = duplicate subscriptions

4. **Timer Dispose Race**: `_debounceTimer?.Dispose()` followed by new timer creation
   - Callback may still be queued/running
   - No synchronization with callback completion

5. **Early Path Commitment**: `_currentFilePath = filePath` in partial metadata callback before scan completes
   - If scan fails, path remains set to failed file
   - HandleRequestLinesAsync uses stale path

6. **Progress Thread Safety**: `Progress<T>` callback invokes `SendToUIAsync` from thread pool
   - Need to verify MessageRouter.SendToUIAsync is thread-safe
   - Document if assumption holds

7. **FSW Platform Incompatibility**: `NotifyFilters.Size` unsupported on some platforms
   - `NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size`
   - macOS/Linux may throw

## Correctness Properties

Property 1: Bug Condition - Thread-Safe CTS Operations

_For any_ concurrent access to `_refreshCts` where one thread disposes while another cancels, the fixed code SHALL use lock synchronization preventing ObjectDisposedException and NullReferenceException.

**Validates: Requirements 2.1**

Property 2: Bug Condition - Volatile Path Field

_For any_ concurrent read/write of `_currentFilePath` from multiple threads, the fixed code SHALL use `volatile` or Interlocked ensuring consistent reads without torn values.

**Validates: Requirements 2.2**

Property 3: Bug Condition - Event Unsubscription

_For any_ call to `Shutdown()`, the fixed code SHALL unsubscribe from `OnStaleFileDetected` event preventing memory leaks and duplicate subscriptions.

**Validates: Requirements 2.3**

Property 4: Bug Condition - Timer Synchronization

_For any_ disposal and recreation of `_debounceTimer`, the fixed code SHALL use proper synchronization preventing callback race conditions.

**Validates: Requirements 2.4**

Property 5: Bug Condition - Stale Path Rollback

_For any_ partial metadata callback that sets `_currentFilePath`, the fixed code SHALL clear path at start of `OpenFileByPathAsync` and set only on success path, preventing stale path on failure.

**Validates: Requirements 2.5**

Property 6: Bug Condition - Progress Thread Safety

_For any_ `Progress<T>` callback invoking `SendToUIAsync` from thread pool, `SendToUIAsync` SHALL be thread-safe (verified: stateless, delegates to Photino native interop).

**Validates: Requirements 2.6**

Property 7: Bug Condition - Cross-Platform FSW

_For any_ `FileSystemWatcher` creation, the fixed code SHALL handle platform-specific `NotifyFilters.Size` unsupported exception gracefully.

**Validates: Requirements 2.7**

Property 8: Preservation - File Open Behavior

_For any_ successful file open operation, the fixed code SHALL produce same metadata, progress, and file watching behavior as original code.

**Validates: Requirements 3.1**

Property 9: Preservation - File Change Detection

_For any_ file change detection, the fixed code SHALL produce same debounce and refresh behavior as original code.

**Validates: Requirements 3.2**

Property 10: Preservation - Shutdown Behavior

_For any_ shutdown request, the fixed code SHALL cancel all background operations and dispose resources as original code.

**Validates: Requirements 3.3**

Property 11: Preservation - Line Request Behavior

_For any_ `RequestLinesMessage`, the fixed code SHALL read and return requested lines as original code.

**Validates: Requirements 3.4**

Property 12: Preservation - File Not Found Error

_For any_ external file deletion or move, the fixed code SHALL report file not found error to UI as original code.

**Validates: Requirements 3.5**

## Fix Implementation

### Changes Required

**File**: `src/EditorApp/Services/PhotinoHostService.cs`

#### 1. CTS Synchronization (Fixes 1.1, 2.1)

Add lock object for CTS operations:
```csharp
private readonly object _ctsLock = new();
```

Wrap all `_refreshCts` and `_scanCts` dispose/create operations with lock:
```csharp
lock (_ctsLock)
{
    _refreshCts?.Cancel();
    _refreshCts?.Dispose();
    _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
}
```

Alternatively, use Interlocked.Exchange pattern:
```csharp
var oldCts = Interlocked.Exchange(ref _refreshCts, new CancellationTokenSource());
oldCts?.Cancel();
oldCts?.Dispose();
```

**Choice**: Lock approach — clearer, handles linked CTS creation correctly.

#### 2. Path Volatility (Fixes 1.2, 2.2)

Mark `_currentFilePath` as `volatile`:
```csharp
private volatile string? _currentFilePath;
```

Or use Interlocked for writes and read into local:
```csharp
Interlocked.Exchange(ref _currentFilePath, filePath);
var path = Volatile.Read(ref _currentFilePath);
```

**Choice**: `volatile` for simplicity — string references are atomic on .NET, volatile ensures visibility.

#### 3. Event Unsubscription (Fixes 1.3, 2.3)

Store delegate reference:
```csharp
private EventHandler<string>? _staleFileHandler;

// In RegisterMessageHandlers:
_staleFileHandler = (path) => { ... };
fs.OnStaleFileDetected += _staleFileHandler;

// In Shutdown:
if (_fileService is FileService fs && _staleFileHandler is not null)
{
    fs.OnStaleFileDetected -= _staleFileHandler;
}
```

#### 4. Timer Synchronization (Fixes 1.4, 2.4)

Use replace-then-dispose pattern:
```csharp
private readonly object _timerLock = new();

// In OnFileChanged:
System.Threading.Timer? oldTimer;
lock (_timerLock)
{
    oldTimer = _debounceTimer;
    _debounceTimer = new System.Threading.Timer(...);
}
oldTimer?.Dispose();
```

Alternatively, use `Timer.Change(Timeout.Infinite, Timeout.Infinite)` to stop, then dispose.

**Choice**: Lock with replace-then-dispose — clearer intent.

#### 5. Stale Path Rollback (Fixes 1.5, 2.5)

Clear `_currentFilePath` at start of `OpenFileByPathAsync`, set only on success:
```csharp
internal async Task OpenFileByPathAsync(string filePath)
{
    _currentFilePath = null;  // Clear at start
    try
    {
        // ... existing code, but remove _currentFilePath = filePath from callback
        // ... on success:
        _currentFilePath = filePath;
    }
    catch
    {
        // _currentFilePath remains null on failure
    }
}
```

#### 6. Progress Thread Safety (Fixes 1.6, 2.6)

**Verified**: `SendToUIAsync` is thread-safe.

MessageRouter.SendToUIAsync:
- Stateless operation (no shared mutable state)
- Creates new `MessageEnvelope` per call
- Serializes to JSON locally
- Delegates to `_window.SendWebMessage(json)` (Photino native interop)

No code change required. Add documentation comment:
```csharp
// Progress<T> callback runs on thread pool. SendToUIAsync is thread-safe
// (stateless, delegates to Photino native SendWebMessage).
```

#### 7. Cross-Platform FSW (Fixes 1.7, 2.7)

Remove `NotifyFilters.Size`, keep only `LastWrite`:
```csharp
_fileWatcher = new FileSystemWatcher(dir, name)
{
    NotifyFilter = NotifyFilters.LastWrite,  // Remove Size for cross-platform
    EnableRaisingEvents = true
};
```

Or wrap in try-catch with fallback:
```csharp
try
{
    _fileWatcher = new FileSystemWatcher(dir, name)
    {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        EnableRaisingEvents = true
    };
}
catch (ArgumentException)
{
    // Fallback without Size
    _fileWatcher = new FileSystemWatcher(dir, name)
    {
        NotifyFilter = NotifyFilters.LastWrite,
        EnableRaisingEvents = true
    };
}
```

**Choice**: Remove `NotifyFilters.Size` — simpler, LastWrite sufficient for file changes.

## Testing Strategy

### Validation Approach

Two-phase: surface counterexamples demonstrating bug on unfixed code, then verify fix works and preserves behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples BEFORE fix. Confirm or refute root cause analysis.

**Test Plan**: Write tests simulating concurrent access patterns. Run on UNFIXED code to observe failures.

**Test Cases**:
1. **CTS Race Test**: Spin up 2 threads, one disposes _refreshCts, other cancels → ObjectDisposedException
2. **Path Race Test**: Writer thread updates path, reader thread reads → verify consistent reads
3. **Event Leak Test**: Create service, shutdown, recreate → verify single subscription
4. **Timer Race Test**: Trigger file change, immediately trigger another → verify no timer race
5. **Stale Path Test**: Trigger partial metadata then fail → verify path cleared
6. **FSW Platform Test**: Create FSW on Linux/macOS → verify no exception

**Expected Counterexamples**:
- CTS race: ObjectDisposedException
- Path race: Stale/torn reads (hard to reproduce reliably)
- Event leak: Duplicate event firings
- Timer race: NullReferenceException or callback on disposed timer

### Fix Checking

**Goal**: Verify for all inputs where bug condition holds, fixed function produces expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := FixedPhotinoHostService(input)
  ASSERT expectedBehavior(result)
END FOR
```

### Preservation Checking

**Goal**: Verify for all inputs where bug condition does NOT hold, fixed function produces same result as original.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT OriginalPhotinoHostService(input) = FixedPhotinoHostService(input)
END FOR
```

**Test Plan**: Observe behavior on UNFIXED code for single-threaded operations, then write property-based tests capturing that behavior.

**Test Cases**:
1. **File Open Preservation**: Open file successfully → same metadata sent
2. **Refresh Preservation**: File change detected → same refresh behavior
3. **Shutdown Preservation**: Shutdown called → same cleanup behavior
4. **Line Request Preservation**: Request lines → same response

### Unit Tests

- Test CTS dispose/create under concurrent access
- Test path read/write under concurrent access
- Test event subscription/unsubscription
- Test timer dispose/create race
- Test path cleared on failure
- Test FSW creation without Size filter

### Property-Based Tests

- Generate concurrent operation sequences, verify no exceptions
- Generate file open scenarios with various failure modes, verify path handling
- Generate timer operation sequences, verify no race conditions

### Integration Tests

- Test full file open flow with thread safety
- Test file change detection with concurrent changes
- Test shutdown during ongoing operations
- Test cross-platform FSW behavior
