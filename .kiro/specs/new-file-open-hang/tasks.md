# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Buffer Not Reset on New File Open
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the stale buffer merge creates massive sparse arrays
  - **Scoped PBT Approach**: Scope the property to cases where `previousLinesStartLine > 0 AND previousLines IS NOT null AND newFileTotalLines < previousLinesStartLine` (isBugCondition from design)
  - **Test file**: `tests/frontend/properties/newFileOpenHang.bugCondition.test.ts`
  - **Test setup**: Create a minimal harness that simulates App.tsx state management (useState mocks for `lines`, `linesStartLine`, `fileMeta`) and the `onFileOpened` callback logic
  - **Property**: For all generated `(previousBufferStartLine, previousBufferLength, newFileTotalLines)` tuples where isBugCondition holds, after calling `onFileOpened` with a different file followed by `onLinesResponse(startLine=0, lines=[...])`:
    - Assert `linesStartLine === 0`
    - Assert `lines === null` OR `lines.length <= APP_FETCH_SIZE` (no sparse array)
    - Assert merged array length does NOT exceed `newFileTotalLines + APP_FETCH_SIZE`
  - **Generators**: Use `fc.integer({min: 1000, max: 2_000_000})` for previousBufferStartLine, `fc.integer({min: 1, max: 400})` for previousBufferLength, `fc.integer({min: 1, max: 999})` for newFileTotalLines (must be < previousBufferStartLine)
  - Run test on UNFIXED code: `npm test` with cwd = `tests/frontend`
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - proves the bug exists: merged array length ≈ previousBufferStartLine + 200)
  - Document counterexamples found (e.g., "previousStart=1000000, newTotal=40 → merged array length = 1000200")
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 2.1, 2.2_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Same-File Operations Unchanged
  - **IMPORTANT**: Follow observation-first methodology
  - **Test file**: `tests/frontend/properties/newFileOpenHang.preservation.test.ts`
  - **Test setup**: Same harness as task 1, simulating App.tsx state + onFileOpened + onLinesResponse logic
  - **Observe on UNFIXED code**:
    - Same-file scroll merge: buffer at line 100, LinesResponse at line 300 → merged array spans [100, 500), length=400
    - Refresh event: buffer at line 5000, isRefresh=true → buffer NOT reset, re-request issued at same position
    - Scan-complete (same file): partial metadata then final metadata for same fileName → buffer NOT reset, re-request at current position
    - First file open (no previous file): lines=null → LinesResponse sets buffer directly, no merge
  - **Property-based tests**:
    - **Scroll merge preservation**: For all `(bufferStart, bufferLength, responseStart, responseLength)` where this is a same-file scroll (NOT a new file open), verify merge logic produces `mergedStart = min(bufferStart, responseStart)` and `mergedLength = max(bufferStart+bufferLength, responseStart+responseLength) - mergedStart`
    - **Refresh preservation**: For all `(bufferStart, bufferLength, newTotalLines)` with isRefresh=true, verify buffer start is preserved (or clamped if file shrunk) and lines are NOT reset to null
    - **First-open preservation**: For all LinesResponse with no previous buffer (lines=null), verify buffer is set directly to response data without merge
  - **Generators**: Use `fc.integer({min: 0, max: 100_000})` for buffer positions, `fc.integer({min: 1, max: 400})` for lengths, `fc.integer({min: 1, max: 2_000_000})` for totalLines
  - Run tests on UNFIXED code: `npm test` with cwd = `tests/frontend`
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 3. Fix for new file open hang due to missing buffer reset

  - [x] 3.1 Implement the fix in App.tsx
    - **File**: `src/EditorApp/src/App.tsx`
    - **Change 1 — "Different file" branch** (~line 155): Add `setLines(null); setLinesStartLine(0);` after `setLoadProgress(null)` and before `setTitleBarText(...)`:
      ```typescript
      setLoadProgress(null);
      setLines(null);          // ← ADD
      setLinesStartLine(0);    // ← ADD
      setTitleBarText(`${data.fileName} - Editor`);
      ```
    - **Change 2 — `isPartial` branch** (~line 127): Add buffer reset when partial metadata is for a DIFFERENT file:
      ```typescript
      if (data.isPartial) {
        if (!fileMetaRef.current || fileMetaRef.current.fileName !== data.fileName) {
          setLines(null);
          setLinesStartLine(0);
        }
        setFileMeta(data);
        // ... rest unchanged
      }
      ```
    - _Bug_Condition: isBugCondition(input) where input.isNewFile=true AND input.previousLinesStartLine > 0 AND input.previousLines IS NOT null_
    - _Expected_Behavior: After onFileOpened for different file, lines=null AND linesStartLine=0 before sendRequestLines_
    - _Preservation: Same-file scroll merge, refresh, scan-complete, first-open all unchanged_
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 3.2 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Buffer Reset on New File Open
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior (linesStartLine=0, no sparse array)
    - When this test passes, it confirms the expected behavior is satisfied
    - Run: `npm test` with cwd = `tests/frontend`
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed — buffer resets prevent sparse array creation)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.3 Verify preservation tests still pass
    - **Property 2: Preservation** - Same-File Operations Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - Run: `npm test` with cwd = `tests/frontend`
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions — merge logic, refresh, scan-complete all unchanged)
    - Confirm all tests still pass after fix (no regressions)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 4. Checkpoint - Ensure all tests pass
  - Run full test suite: `npm test` with cwd = `tests/frontend`
  - Verify both bug condition and preservation tests pass
  - Ensure no other existing tests regressed
  - Ask the user if questions arise

- [x] 5. Fix Ctrl-O dialog issues (text disappears on open/cancel)

  - [x] 5.1 Remove premature `setIsLoading(true)` from keydown handler
    - **File**: `src/EditorApp/src/App.tsx`
    - **Change**: Remove `setIsLoading(true)` from the Ctrl-O keydown handler
    - **Reason**: `isLoading=true` causes ContentArea to hide text and show loading state. If user cancels dialog, nothing resets it → text permanently gone
    - _Requirements: 4.1, 4.2, 5.1, 5.2_

  - [x] 5.2 Remove interop timeout from `sendOpenFileRequest()`
    - **File**: `src/EditorApp/src/InteropService.ts`
    - **Change**: Remove `startTimeout()` call from `sendOpenFileRequest()`
    - **Reason**: Native file dialog blocks backend thread for arbitrary duration. 5s timeout fires false INTEROP_FAILURE error, displaying "not responding" message and hiding text
    - _Requirements: 4.3, 5.3_
