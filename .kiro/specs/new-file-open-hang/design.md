# New File Open Hang Bugfix Design

## Overview

Opening a small file after scrolling deep into a large file causes the app to hang. The frontend (`App.tsx`) does not reset buffer state (`lines`, `linesStartLine`) when a different file is opened. The stale buffer at line ~1M merges with the new LinesResponse at line 0, creating a million-element sparse array that exhausts memory. The fix adds two `setLines(null); setLinesStartLine(0);` calls in the `onFileOpened` callback — one in the "different file" branch and one in the `isPartial` branch (when it's a different file).

## Glossary

- **Bug_Condition (C)**: The condition that triggers the bug — opening a new file while the buffer holds lines from a previous file at a high start position
- **Property (P)**: The desired behavior — buffer is reset to null/0 before requesting lines for the new file, preventing sparse array creation
- **Preservation**: Existing merge behavior for same-file scrolling, refresh behavior, and partial-metadata scrolling must remain unchanged
- **`lines`**: React state in `App.tsx` holding the current line buffer (string array)
- **`linesStartLine`**: React state in `App.tsx` — the 0-based line number of the first element in `lines`
- **`onFileOpened`**: Callback registered via `interop.onFileOpened()` that handles `FileMeta` messages from the backend
- **Merge logic**: Code in `onLinesResponse` that combines new lines with existing buffer by computing merged range

## Bug Details

### Bug Condition

The bug manifests when a user opens a new file (different from the currently loaded file) while the existing buffer holds lines from a high position in the previous file. The `onFileOpened` handler in `App.tsx` resets metadata and UI state but does NOT reset `lines` and `linesStartLine`. When the subsequent `LinesResponse` arrives (startLine=0), the merge logic in `onLinesResponse` computes `mergedStart = Math.min(prevStart, 0)` = 0 and `mergedEnd = Math.max(prevStart + prevLines.length, data.lines.length)` ≈ 1,000,200, creating a massive sparse array.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type FileOpenEvent
  OUTPUT: boolean
  
  RETURN input.isNewFile = true
         AND input.previousLinesStartLine > 0
         AND input.previousLines IS NOT null
         AND input.previousLinesStartLine > input.newFileTotalLines
END FUNCTION
```

### Examples

- **Example 1**: Open 2M-line file → scroll to line 1,000,000 → open 40-line file → `onLinesResponse` merges startLine=0 with prevStart=1,000,000 → creates 1,000,200-element array → hang
- **Example 2**: Open 500K-line file → scroll to line 400,000 → open 10-line file → same merge creates 400,200-element sparse array → hang or severe lag
- **Example 3**: Open 2M-line file → scroll to line 1,000,000 → open another 2M-line file → merge creates 1,000,200-element array (even though new file is large, the merge is wasteful and the first 1M entries are empty strings)
- **Edge case**: Open 100-line file → scroll to line 50 → open 200-line file → merge creates ~250-element array → no hang (small buffer, no issue, but still technically incorrect state)

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Scrolling within a single file must continue to merge new lines into the buffer correctly (edge-proximity fetches)
- File refresh (same file, external modification) must preserve scroll position and re-request current buffer range
- Partial metadata during large-file scan must continue to allow immediate content display and scrolling
- Scan-complete metadata for the same file must re-request current buffer range (not reset)
- Cancelling the file-open dialog must leave current state unchanged
- Jump requests (scrollbar drag) must continue to replace buffer entirely

**Scope:**
All inputs that do NOT involve opening a different file should be completely unaffected by this fix. This includes:
- Same-file scroll fetches (merge logic)
- Same-file refresh events (`isRefresh = true`)
- Same-file scan-complete events (final metadata after partial)
- Scrollbar jump requests
- First file open (no previous buffer)

## Hypothesized Root Cause

Based on code analysis of `App.tsx` lines 127-162, the confirmed root causes are:

1. **Missing buffer reset in "different file" branch (line 155-162)**: When `fileMetaRef.current.fileName !== data.fileName`, the handler resets `fileMeta`, `isLoading`, `error`, `loadProgress`, and `titleBarText`, but does NOT call `setLines(null)` or `setLinesStartLine(0)`. The subsequent `interop.sendRequestLines(0, 200)` triggers `onLinesResponse` which merges with the stale buffer.

2. **Missing buffer reset in `isPartial` branch (line 127-133)**: When partial metadata arrives for a NEW file (different from current), the handler similarly omits buffer reset. The `interop.sendRequestLines(0, 200)` call will merge with stale buffer from the previous file.

3. **Merge logic amplifies the problem**: `onLinesResponse` (lines 170-198) computes `mergedStart = Math.min(prevStart, data.startLine)` and `mergedEnd = Math.max(prevEnd, newEnd)`. With prevStart=1,000,000 and data.startLine=0, this creates a `new Array(1,000,200)` filled with empty strings — memory exhaustion.

## Correctness Properties

Property 1: Bug Condition - Buffer Reset on New File Open

_For any_ file open event where a different file is opened (isBugCondition returns true — previous buffer exists at a high start line), the fixed `onFileOpened` handler SHALL reset `lines` to null and `linesStartLine` to 0 before requesting lines for the new file, ensuring no merge with stale buffer data occurs.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

Property 2: Preservation - Same-File Operations Unchanged

_For any_ event that is NOT a new-file-open (same-file scroll fetches, refresh events, scan-complete events, scrollbar jumps), the fixed code SHALL produce exactly the same behavior as the original code, preserving buffer merge logic, scroll position preservation on refresh, and scan-complete re-request behavior.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `src/EditorApp/src/App.tsx`

**Function**: `onFileOpened` callback (inside `React.useEffect`)

**Specific Changes**:

1. **"Different file" branch (line ~155-162)**: Add `setLines(null); setLinesStartLine(0);` after `setLoadProgress(null)` and before `lastRequestedStartRef.current = 0`:
   ```typescript
   // Different file (small file or first open) — full reset
   setFileMeta(data);
   setIsLoading(false);
   setError(null);
   setLoadProgress(null);
   setLines(null);          // ← ADD
   setLinesStartLine(0);    // ← ADD
   setTitleBarText(`${data.fileName} - Editor`);
   lastRequestedStartRef.current = 0;
   interop.sendRequestLines(0, 200);
   ```

2. **`isPartial` branch (line ~127-133)**: Add buffer reset when partial metadata is for a DIFFERENT file:
   ```typescript
   if (data.isPartial) {
     // Partial metadata — show content immediately
     // Reset buffer if this is a different file
     if (!fileMetaRef.current || fileMetaRef.current.fileName !== data.fileName) {
       setLines(null);
       setLinesStartLine(0);
     }
     setFileMeta(data);
     setIsLoading(false);
     setError(null);
     setTitleBarText(`${data.fileName} - Editor`);
     lastRequestedStartRef.current = 0;
     interop.sendRequestLines(0, 200);
   }
   ```

3. **No changes to `onLinesResponse`**: The merge logic is correct for its intended use case (same-file scrolling). The fix prevents stale data from reaching the merge.

4. **No backend changes**: The bug is purely in frontend state management.

5. **No changes to ContentArea**: Once buffer is properly reset, ContentArea receives correct props.

### Additional Fixes (Ctrl-O Dialog Issues)

**File**: `src/EditorApp/src/App.tsx`

**Change**: Remove `setIsLoading(true)` from the Ctrl-O keydown handler. Loading state should only be set when a file is actually being opened (managed by `onFileOpened` callback), not when the dialog is merely shown.

```typescript
// BEFORE (broken):
function handleKeyDown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && (e.key === 'o' || e.key === 'O')) {
    e.preventDefault();
    setIsLoading(true);  // ← REMOVE
    setError(null);
    interop.sendOpenFileRequest();
  }
}

// AFTER (fixed):
function handleKeyDown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && (e.key === 'o' || e.key === 'O')) {
    e.preventDefault();
    setError(null);
    interop.sendOpenFileRequest();
  }
}
```

**File**: `src/EditorApp/src/InteropService.ts`

**Change**: Remove `startTimeout()` from `sendOpenFileRequest()`. The native file dialog blocks the backend thread for an arbitrary duration — this is not an unresponsive backend condition.

```typescript
// BEFORE (broken):
sendOpenFileRequest(): void {
  // ...
  (window as any).external.sendMessage(JSON.stringify(envelope));
  startTimeout();  // ← REMOVE
}

// AFTER (fixed):
sendOpenFileRequest(): void {
  // ...
  (window as any).external.sendMessage(JSON.stringify(envelope));
  // No timeout — native dialog can take arbitrarily long
}
```

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write unit tests that simulate the `onFileOpened` → `onLinesResponse` sequence with stale buffer state. Run on UNFIXED code to observe the sparse array creation.

**Test Cases**:
1. **Large-to-small file switch**: Set buffer at line 1,000,000, trigger onFileOpened with new 40-line file, then onLinesResponse with startLine=0 → observe merged array length (will be ~1M on unfixed code)
2. **Large-to-large file switch**: Set buffer at line 500,000, open different large file → observe merge creates 500K sparse entries (will fail on unfixed code)
3. **Partial metadata for new file**: Set buffer at line 1,000,000, trigger isPartial=true for different file → observe buffer not reset (will fail on unfixed code)
4. **Memory measurement**: Measure array allocation size after merge with stale buffer (will show ~1M elements on unfixed code)

**Expected Counterexamples**:
- `onLinesResponse` creates arrays of 1,000,000+ elements when merging with stale buffer
- Possible causes confirmed: missing `setLines(null)` and `setLinesStartLine(0)` in both branches

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  state := onFileOpened_fixed(input)
  ASSERT state.lines = null
  ASSERT state.linesStartLine = 0
  
  // After subsequent LinesResponse:
  state2 := onLinesResponse_fixed(linesResponse(startLine=0, lines=[...]))
  ASSERT state2.lines.length <= FETCH_SIZE
  ASSERT state2.linesStartLine = 0
  ASSERT no_sparse_array(state2.lines)
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT onFileOpened_original(input) = onFileOpened_fixed(input)
  ASSERT onLinesResponse_original(input) = onLinesResponse_fixed(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for same-file operations (scroll, refresh, scan-complete), then write property-based tests capturing that behavior.

**Test Cases**:
1. **Same-file scroll merge preservation**: Generate random buffer states and LinesResponse payloads for same-file scrolling → verify merge produces identical results before and after fix
2. **Refresh preservation**: Generate random buffer states and refresh events → verify scroll position preserved identically
3. **Scan-complete preservation**: Generate random partial→complete sequences for same file → verify buffer re-request behavior unchanged
4. **First-file-open preservation**: Generate random first-open events (no previous file) → verify behavior identical

### Unit Tests

- Test buffer reset occurs in "different file" branch
- Test buffer reset occurs in `isPartial` branch for different file
- Test buffer NOT reset in `isPartial` branch for same file (scan-complete update)
- Test buffer NOT reset in refresh branch
- Test merged array size after fix is bounded by FETCH_SIZE for new file opens

### Property-Based Tests

- Generate random (previousBufferStart, previousBufferLength, newFileTotalLines) tuples → verify post-fix state always has linesStartLine=0 and lines=null after new-file-open
- Generate random same-file scroll sequences → verify merge logic produces identical results with and without fix
- Generate random file sizes and scroll positions → verify no array exceeds WINDOW_SIZE + FETCH_SIZE elements after any operation

### Integration Tests

- Test full sequence: open large file → scroll deep → open small file → verify content displays from line 0
- Test full sequence: open large file → partial metadata → scroll → open different file → verify clean reset
- Test full sequence: open file → refresh → verify scroll position preserved (regression check)
