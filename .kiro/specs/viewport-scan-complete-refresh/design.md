# Viewport Scan-Complete Refresh Bugfix Design

## Overview

After a large file scan completes, `PhotinoHostService.OpenFileByPathAsync` sends a final `FileOpenedResponse` (with `isPartial: false`) but does not push updated viewport content. The frontend remains showing only the lines fetched during the partial scan phase.

The fix is dual-layered:
1. **Frontend (primary fix)**: `App.tsx` — when the final `FileOpenedResponse` arrives for the same file (scan complete), re-request the current buffer range via `sendRequestLines` so `ContentArea` displays refreshed content.
2. **Backend (safety net)**: `PhotinoHostService.cs` — push a `ViewportResponse` after the final `FileOpenedResponse` for large files, serving viewport-protocol consumers (e.g., `ViewportRenderer`).

## Glossary

- **Bug_Condition (C)**: A large file scan completes (partial metadata was emitted, then final metadata is sent) without a subsequent viewport content push
- **Property (P)**: After scan completion for a large file, the backend SHALL push a `ViewportResponse` so the frontend displays up-to-date content
- **Preservation**: Small file opens, mid-scan viewport requests, external refresh cycles, and scan cancellation must remain unchanged
- **`OpenFileByPathAsync`**: Method in `PhotinoHostService.cs` that orchestrates file scanning and sends metadata/viewport messages to the frontend
- **`onPartialMetadata`**: Callback fired once when the scan crosses the 256 KB threshold, sending partial `FileOpenedResponse` to the frontend
- **`ViewportResponse`**: Message containing the rectangular slice of file content the frontend renders

## Bug Details

### Bug Condition

The bug manifests when a large file (>256 KB) finishes scanning. The `OpenFileByPathAsync` method sends the final `FileOpenedResponse` with `isPartial: false`, but no `ViewportResponse` follows. The frontend received viewport content during the partial phase (via its own `RequestViewport` after the partial `FileOpenedResponse`), but that content reflects the partially-indexed state. After scan completion, the total line count and max line length may have changed, yet the frontend has no trigger to re-request or receive updated viewport content.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type FileOpenEvent
  OUTPUT: boolean
  
  RETURN input.fileSize > SizeThresholdBytes (256_000)
         AND scanCompleted(input.filePath)
         AND partialMetadataWasEmitted(input.filePath)
         AND NOT viewportResponseSentAfterScanComplete(input.filePath)
END FUNCTION
```

### Examples

- **1 MB text file**: Partial metadata sent at 256 KB (200 lines indexed). Frontend requests viewport lines 0-50. Scan completes with 5000 total lines. Frontend still shows lines 0-50 from partial phase — metadata updated but content stale.
- **500 KB log file**: Partial metadata sent. Frontend displays first 40 lines. Scan completes revealing maxLineLength changed from 80 to 2000. Frontend doesn't know to update horizontal scroll bounds.
- **300 KB file (just over threshold)**: Partial metadata sent with 100 lines. Scan completes with 102 lines. Difference is small but frontend still shows stale viewport without the final 2 lines' metadata.
- **200 KB file (below threshold)**: No partial metadata emitted. Single `FileOpenedResponse` sent. Frontend requests viewport normally. No bug — this path is unaffected.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Small file opens (≤256 KB) must continue to send a single `FileOpenedResponse` followed by normal request/response viewport flow
- Mid-scan viewport requests (`RequestViewport` messages during scanning) must continue to be served from the partially-indexed lines
- External file modification refresh cycles must continue to send `FileOpenedResponse` with `isRefresh: true` — the frontend already re-requests viewport on refresh
- Scan cancellation (new file opened) must continue to cancel cleanly without sending viewport updates for the cancelled file
- The `HandleRequestViewportAsync` handler must continue to work identically for on-demand viewport requests

**Scope:**
All inputs that do NOT involve a large-file scan completion should be completely unaffected by this fix. This includes:
- Small file opens (no partial phase)
- On-demand `RequestViewport` messages from the frontend
- External file refresh cycles
- Scan cancellation scenarios

## Hypothesized Root Cause

Based on the code analysis, the root cause is straightforward:

1. **Frontend does not re-request on non-refresh, same-file metadata**: The frontend (`App.tsx`) re-requests lines when it receives a `FileOpenedResponse` with `isRefresh: true`, but does NOT re-request when it receives a final (non-partial, non-refresh) `FileOpenedResponse` for the same file. This is the scan-complete message. The handler only updates `fileMeta` and clears `loadProgress`.

2. **Missing viewport push after scan completion (backend)**: In `OpenFileByPathAsync`, after `_fileService.OpenFileAsync` returns and the final `FileOpenedResponse` is sent, there is no code to push a `ViewportResponse`. The method simply ends after sending metadata.

3. **No stored viewport parameters**: The system does not track the frontend's last viewport request parameters (startLine, lineCount, startColumn, columnCount, wrapMode, viewportColumns). Without these, the backend cannot replay a viewport response after scan completion.

4. **Design gap in early-content-display feature**: The partial metadata feature was added to show content early, but the "scan complete" transition was not designed to push updated content — only updated metadata.

## Correctness Properties

Property 1: Bug Condition - Viewport refresh after large file scan completion

_For any_ large file open (fileSize > 256 KB) where partial metadata was emitted and the scan completes successfully, the system SHALL send a `ViewportResponse` after the final `FileOpenedResponse`, containing the initial viewport region (startLine=0) computed from the fully-indexed file data.

**Validates: Requirements 2.1, 2.2**

Property 2: Preservation - Small file and non-scan-complete flows unchanged

_For any_ file open where the file is ≤256 KB (no partial metadata emitted), or for any on-demand `RequestViewport`, external refresh, or scan cancellation scenario, the system SHALL produce exactly the same messages as the original code, with no additional `ViewportResponse` pushed.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4**

## Fix Implementation

### Changes Required

The fix addresses both the frontend (primary) and backend (safety net):

---

**File**: `src/EditorApp/src/App.tsx`

**Location**: `onFileOpened` callback, the `else` branch handling final (non-partial) `FileOpenedResponse`

**Change**: When the final `FileOpenedResponse` arrives for the same file (scan complete), re-request the current buffer range instead of only updating metadata.

**Before:**
```typescript
if (fileMetaRef.current && fileMetaRef.current.fileName === data.fileName) {
  // Same file — update totalLines only, preserve scroll/buffer
  setFileMeta(data);
  setLoadProgress(null);
}
```

**After:**
```typescript
if (fileMetaRef.current && fileMetaRef.current.fileName === data.fileName) {
  // Same file scan complete — update metadata and re-request current buffer
  // so displayed content reflects the fully-indexed file.
  const currentStart = linesStartRef.current;
  const currentCount = linesRef.current ? linesRef.current.length : 0;
  setFileMeta(data);
  setLoadProgress(null);

  // Re-request current buffer range to refresh content
  const bufferLen = Math.max(currentCount, APP_FETCH_SIZE);
  const newStart = Math.min(currentStart, Math.max(0, data.totalLines - bufferLen));
  const count = Math.min(bufferLen, data.totalLines - newStart);
  lastRequestedStartRef.current = newStart;
  isJumpRequestRef.current = true;
  interop.sendRequestLines(newStart, count);
}
```

This mirrors the pattern used in the `isRefresh` handler: update metadata, then re-request lines for the current scroll position.

---

**File**: `src/EditorApp/Services/PhotinoHostService.cs`

**Method**: `OpenFileByPathAsync`

**Specific Changes**:

1. **Track whether partial metadata was emitted**: Add a boolean flag (`partialWasEmitted`) in `OpenFileByPathAsync` that is set to `true` inside the `onPartialMetadata` callback. This distinguishes large-file scans from small-file opens.

2. **Store last viewport request parameters**: Add a field `_lastViewportRequest` (type `RequestViewport?`) to `PhotinoHostService`. Update `HandleRequestViewportAsync` to store the request before processing it. This allows replaying the viewport after scan completion.

3. **Push viewport after final FileOpenedResponse for large files**: After sending the final `FileOpenedResponse` (when `partialWasEmitted` is true), call `_viewportService.GetViewportAsync` using either the stored `_lastViewportRequest` parameters or a sensible default (startLine=0, lineCount from last request or a default like 100, full column range). Send the resulting `ViewportResponse` to the UI.

4. **Guard against null viewport service**: The viewport push should only occur if `_viewportService` is not null (same guard as `HandleRequestViewportAsync`).

5. **Guard against cancellation**: Check `scanToken` cancellation before pushing the viewport response, since the scan could be cancelled between the final metadata send and the viewport push.

**Pseudocode for the fix in `OpenFileByPathAsync`:**
```
// After sending final FileOpenedResponse:
if (partialWasEmitted && _viewportService is not null)
{
    var vp = _lastViewportRequest;
    var startLine = vp?.StartLine ?? 0;
    var lineCount = vp?.LineCount ?? 100;
    var startColumn = vp?.StartColumn ?? 0;
    var columnCount = vp?.ColumnCount ?? 200;
    var wrapMode = vp?.WrapMode ?? false;
    var viewportColumns = vp?.ViewportColumns ?? 200;

    var result = await _viewportService.GetViewportAsync(
        filePath, startLine, lineCount, startColumn, columnCount, wrapMode, viewportColumns, scanToken);

    await _messageRouter.SendToUIAsync(new ViewportResponse { ... });
}
```

**File**: `src/EditorApp/Services/PhotinoHostService.cs`

**Method**: `HandleRequestViewportAsync`

**Change**: Store the incoming `RequestViewport` in `_lastViewportRequest` field before processing.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write integration tests that mock `IMessageRouter` and `IFileService`, open a large file through `OpenFileByPathAsync`, and assert that a `ViewportResponse` is sent after the final `FileOpenedResponse`. Run these tests on the UNFIXED code to observe failures.

**Test Cases**:
1. **Large file scan complete — no viewport push**: Open a >256 KB file, verify that after `FileOpenedResponse(isPartial: false)` no `ViewportResponse` is sent (will fail on unfixed code — confirms bug)
2. **Large file with stored viewport request**: Open large file, simulate a `RequestViewport` during scan, then verify scan completion pushes viewport (will fail on unfixed code)
3. **Partial metadata emitted flag**: Verify that the partial callback fires for large files and not for small files (baseline behavior check)

**Expected Counterexamples**:
- After scan completion, message log shows `FileOpenedResponse` but no `ViewportResponse`
- Possible causes: no viewport push code exists after scan completion

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := OpenFileByPathAsync_fixed(input.filePath)
  messages := capturedMessages()
  ASSERT messages CONTAINS ViewportResponse AFTER FileOpenedResponse(isPartial: false)
  ASSERT ViewportResponse.totalPhysicalLines == finalTotalLines
  ASSERT ViewportResponse.startLine >= 0
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT messagesFrom(OpenFileByPathAsync_original(input)) == messagesFrom(OpenFileByPathAsync_fixed(input))
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain (varying file sizes around the threshold)
- It catches edge cases that manual unit tests might miss (e.g., exactly 256,000 bytes)
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for small file opens and refresh cycles, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Small file open preservation**: Verify that opening files ≤256 KB produces exactly one `FileOpenedResponse` and zero pushed `ViewportResponse` messages — same before and after fix
2. **On-demand viewport preservation**: Verify that `HandleRequestViewportAsync` continues to respond identically to `RequestViewport` messages
3. **Refresh cycle preservation**: Verify that external file refresh sends `FileOpenedResponse(isRefresh: true)` without an additional viewport push
4. **Scan cancellation preservation**: Verify that cancelling a scan does not send any viewport response for the cancelled file

### Unit Tests

- Test that `OpenFileByPathAsync` sends `ViewportResponse` after final `FileOpenedResponse` for large files
- Test that `OpenFileByPathAsync` does NOT send extra `ViewportResponse` for small files
- Test that `_lastViewportRequest` is stored when `HandleRequestViewportAsync` is called
- Test that viewport push uses stored parameters when available
- Test that viewport push uses defaults when no prior viewport request exists
- Test that viewport push is skipped when `_viewportService` is null

### Property-Based Tests

- Generate random file sizes around the 256 KB threshold and verify correct message sequences (viewport push only for large files)
- Generate random viewport request parameters, store them, trigger scan completion, and verify the pushed viewport uses the stored parameters
- Generate random sequences of open/cancel/refresh operations and verify no extra viewport messages leak through for non-large-file scenarios

### Integration Tests

- End-to-end test: open a real large file, verify message sequence includes viewport push after scan
- Test: open large file, send viewport request during scan, verify scan-complete viewport uses last request params
- Test: open large file then immediately open another file (cancel), verify no viewport push for cancelled file
