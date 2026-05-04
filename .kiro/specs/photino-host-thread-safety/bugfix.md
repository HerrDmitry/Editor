# Bugfix Requirements Document

## Introduction

PhotinoHostService.cs has 8 thread safety issues identified in code review: race conditions in cancellation token disposal, file path field access, event subscription leaks, timer disposal races, and cross-platform compatibility gaps. These can cause ObjectDisposedException, NullReferenceException, stale data reads, memory leaks, and platform-specific failures under concurrent access.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN Thread A calls `_refreshCts?.Dispose()` followed by `_refreshCts = new CancellationTokenSource()` WHILE Thread B is calling `_refreshCts.Cancel()` THEN the system throws ObjectDisposedException or NullReferenceException

1.2 WHEN Thread A reads `_currentFilePath` in `HandleRequestLinesAsync` WHILE Thread B writes to it in `OpenFileByPathAsync` or the partial metadata callback THEN the system reads stale path value leading to wrong file being read

1.3 WHEN service is recreated after `Shutdown()` THEN the system has duplicate event subscriptions because `OnStaleFileDetected` lambda was never unsubscribed, causing memory leak and duplicate event handling

1.4 WHEN `_debounceTimer?.Dispose()` is called in `OnFileChanged` WHILE the timer callback is executing or about to execute THEN the system experiences race condition where callback may fire after disposal but before new timer creation

1.5 WHEN partial metadata callback sets `_currentFilePath` THEN the system stores the path before file scan completes, leaving stale value if second file open fails

1.6 WHEN `Progress<T>` callback invokes `SendToUIAsync` from thread pool thread THEN the system may have thread-safety issues if `SendToUIAsync` implementation is not thread-safe

1.7 WHEN `FileSystemWatcher` is created with `NotifyFilters.Size` on unsupported platform THEN the system throws platform-specific exception

### Expected Behavior (Correct)

2.1 WHEN concurrent access to `_refreshCts` occurs THEN the system SHALL use proper synchronization (lock or Interlocked) to prevent ObjectDisposedException and NullReferenceException

2.2 WHEN `_currentFilePath` is read or written from multiple threads THEN the system SHALL use volatile, lock, or Interlocked to ensure consistent reads

2.3 WHEN `Shutdown()` is called THEN the system SHALL unsubscribe from `OnStaleFileDetected` event to prevent memory leak and duplicate subscriptions

2.4 WHEN disposing and recreating `_debounceTimer` THEN the system SHALL use proper synchronization to prevent callback race condition

2.5 WHEN partial metadata callback sets `_currentFilePath` THEN the system SHALL ensure path is only committed after successful scan completion or handle rollback on failure

2.6 WHEN `Progress<T>` callback runs on thread pool THEN the system SHALL verify and ensure `SendToUIAsync` is thread-safe

2.7 WHEN `FileSystemWatcher` is created THEN the system SHALL handle platform-specific `NotifyFilters.Size` unsupported exception gracefully

### Unchanged Behavior (Regression Prevention)

3.1 WHEN file open operation completes successfully THEN the system SHALL CONTINUE TO send metadata to UI and start file watching

3.2 WHEN file change is detected THEN the system SHALL CONTINUE TO debounce and trigger refresh cycle

3.3 WHEN shutdown is requested THEN the system SHALL CONTINUE TO cancel all background operations and dispose resources

3.4 WHEN line range is requested via `RequestLinesMessage` THEN the system SHALL CONTINUE TO read and return the requested lines from current file

3.5 WHEN file is deleted or moved externally THEN the system SHALL CONTINUE TO report file not found error to UI
