# Bugfix Requirements Document

## Introduction

When a user opens a large file (e.g., 2 million lines), scrolls to the middle (around line 1,000,000), and then opens a new small file (e.g., 40 lines), the application hangs and text is not updated. The root cause is that the frontend buffer state (`lines`, `linesStartLine`) is not reset when a new file is opened. The stale scroll position from the large file causes either: (a) the LinesResponse merge logic to create a massive sparse array spanning from line 0 to line ~1,000,000, exhausting memory, or (b) ContentArea's scroll handler to never trigger new line fetches because the stale buffer position exceeds the new file's total lines.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a new file is opened while the previous file's buffer starts at a high line number (e.g., linesStartLine=1,000,000) THEN the system does not reset `lines` and `linesStartLine` state before requesting lines for the new file

1.2 WHEN the LinesResponse for the new file (startLine=0) arrives and is merged with the stale buffer (linesStartLine=1,000,000) THEN the system creates a merged array of ~1,000,000 elements, causing memory exhaustion and application hang

1.3 WHEN ContentArea renders with stale buffer (start=1,000,000) and new fileMeta (totalLines=40) THEN the scroll handler's edge detection fails because bufferEnd (1,000,400) exceeds totalLines (40), preventing any new line requests

1.4 WHEN the scrollbar position remains at ~1,000,000 after opening a 40-line file THEN the system displays no visible text content because the buffer range does not intersect with the valid line range of the new file

### Expected Behavior (Correct)

2.1 WHEN a new file is opened (different from the current file) THEN the system SHALL immediately reset `lines` to null and `linesStartLine` to 0 before requesting lines for the new file

2.2 WHEN the LinesResponse for the new file arrives after buffer reset THEN the system SHALL populate the buffer starting from line 0 without merging with any stale data from the previous file

2.3 WHEN ContentArea receives the new fileMeta with totalLines=40 and a reset buffer THEN the scroll handler SHALL correctly detect edge proximity and allow line fetches within the valid range [0, totalLines)

2.4 WHEN a new small file is opened after viewing a large file at a high scroll position THEN the system SHALL display the new file's content starting from line 0 with the scrollbar position reset to 0

### Additional Defects (Discovered During Fix)

4.1 WHEN the user presses Ctrl-O to open the file dialog THEN the system sets `isLoading=true` immediately, causing ContentArea to hide text and show a loading state before any file operation begins

4.2 WHEN the user cancels the file-open dialog after `isLoading` was set to true THEN nothing resets `isLoading` back to false, leaving the UI permanently in loading state with text hidden

4.3 WHEN the native file dialog is open (blocking the backend thread) for more than 5 seconds THEN the interop timeout fires an INTEROP_FAILURE error because `sendOpenFileRequest()` starts a 5-second response timer, but the backend cannot respond while the modal dialog is displayed

### Additional Expected Behavior

5.1 WHEN the user presses Ctrl-O THEN the system SHALL NOT set `isLoading=true` — loading state is only set when a file is actually being opened (managed by `onFileOpened` callback)

5.2 WHEN the user cancels the file-open dialog THEN the system SHALL keep the current file content visible and unchanged (no loading state, no error)

5.3 WHEN `sendOpenFileRequest()` is called THEN the system SHALL NOT start an interop timeout, because the native file dialog can take arbitrarily long and does not indicate backend failure

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a file is opened for the first time (no previous file loaded) THEN the system SHALL CONTINUE TO display content starting from line 0

3.2 WHEN scrolling within a single open file triggers edge-proximity line fetches THEN the system SHALL CONTINUE TO merge new lines into the existing buffer correctly

3.3 WHEN a large file emits partial metadata and the user scrolls during scanning THEN the system SHALL CONTINUE TO serve lines from the partial index and merge responses into the buffer

3.4 WHEN a file refresh occurs (same file, external modification) THEN the system SHALL CONTINUE TO preserve the current scroll position and re-request the current buffer range

3.5 WHEN the user cancels a file-open dialog THEN the system SHALL CONTINUE TO keep the current file and buffer state unchanged

---

## Bug Condition (Formal)

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type FileOpenEvent
  OUTPUT: boolean
  
  // Bug triggers when opening a new file while buffer holds lines
  // from a previous file at a position beyond the new file's total lines
  RETURN X.previousBufferStartLine > 0
     AND X.newFileTotalLines < X.previousBufferStartLine
END FUNCTION
```

```pascal
// Property: Fix Checking — Buffer Reset on New File Open
FOR ALL X WHERE isBugCondition(X) DO
  state ← openNewFile'(X)
  ASSERT state.linesStartLine = 0
     AND (state.lines = null OR state.lines.startLine = 0)
     AND no_hang(state)
     AND no_memory_exhaustion(state)
END FOR
```

```pascal
// Property: Preservation Checking — Normal operations unaffected
FOR ALL X WHERE NOT isBugCondition(X) DO
  ASSERT F(X) = F'(X)
END FOR
```
