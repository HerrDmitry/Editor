# Unified Scrolling and Rendering Refactor

## Overview

**Status: COMPLETED**

This task refactored ContentArea.tsx to implement a sliding-window virtual scrolling architecture, replacing the original spacer-based approach that was planned and partially implemented.

### What was originally planned
The original plan was to unify the dual-branch rendering into a single path using a spacer-based virtual scroll (fixed-height spacer div with proportional mapping between scrollTop and line numbers, absolute positioning of rendered lines within the spacer). This approach was implemented but broke in wrap mode because wrapped lines are taller than LINE_HEIGHT, causing the proportional mapping to be inaccurate.

### What was actually implemented
The spacer-based approach was replaced with a **sliding window** architecture:
- Container holds up to WINDOW_SIZE (400) rendered lines as real DOM
- Viewport clips container with `overflow-y: auto` → native pixel-smooth scroll
- When scroll approaches container edge (EDGE_THRESHOLD = 600px), request more lines from backend (FETCH_SIZE = 200)
- App merges new lines into buffer (not replace) for edge-proximity fetches
- `useLayoutEffect` trims buffer when > WINDOW_SIZE, adjusting `scrollTop` by exact measured height of removed lines to prevent visual jumps
- Scrollbar thumb drag: if target in buffer → local scroll via DOM measurement; if outside → debounced jump request replaces buffer entirely
- No spacer div, no fake height, no proportional mapping
- Works identically for wrap and non-wrap — same DOM, just CSS

### Key architectural differences from original plan
| Aspect | Original Plan (Spacer) | Actual Implementation (Sliding Window) |
|--------|----------------------|---------------------------------------|
| Scroll container | Spacer div with `totalLines * LINE_HEIGHT` height (capped at 10M px) | Real DOM lines, no spacer |
| Line positioning | Absolute positioning within spacer | Normal document flow |
| Line → scrollTop | Proportional mapping (`lineToScrollTop`) | DOM height measurement |
| scrollTop → line | Proportional mapping (`scrollTopToLine`) | Walk DOM, accumulate heights |
| Wrap mode | Broke (wrapped lines taller than LINE_HEIGHT) | Works naturally (real DOM heights) |
| Buffer management | Replace on every request | Merge on append, replace on jump |
| Scroll adjustment | Not needed (absolute positioning) | `useLayoutEffect` adjusts scrollTop on trim/prepend |
| Scrollbar drag | `suppressScrollRef` + `lineToScrollTop` | Local scroll (in buffer) or debounced jump (outside buffer) |

## Current Problems in ContentArea.tsx

*All resolved by the sliding window implementation.*

1. ~~**Two separate render branches**: `if (wrapLines) { ... } else { ... }`~~ → Single unified path
2. ~~**Wrap mode uses `overflowY: 'hidden'`** with manual `onWheel` handler~~ → `overflow-y: auto` for all modes
3. ~~**Two scrollbar handlers**: `handleScrollbarWrap` and `handleScrollbarNoWrap`~~ → Single `handleScrollbarDrag`
4. ~~**No scroll suppression mechanism**~~ → Not needed in sliding window (local scroll or jump, no feedback loop)
5. ~~**Separate line numbers column**~~ → Unified `line-container` flex row
6. ~~**No `scrollbarPosition` state**~~ → `scrollbarPosition` state driven by DOM measurement

## Tasks

- [x] 1. Unify the rendering path
  - [x] 1.1 Merge the two render branches into a single DOM structure
    - Remove the `if (wrapLines) { ... } else { ... }` branching in the content state render
    - Use a single `<div className="content-area content-area--virtual">` with one scroll container
    - Render line numbers inline with content in a `line-container` flex row (same as current wrap mode structure) for BOTH modes
    - Remove the separate `line-numbers-column` div used in non-wrap mode
    - Remove the `lineNumbersRef` and its scroll syncing logic (no longer needed with unified structure)
    - _Requirements: 3.10, 11.8_

  - [x] 1.2 Apply CSS-only differences for wrap/non-wrap toggle
    - Set `overflowX: wrapLines ? 'hidden' : 'auto'` on the content column
    - Set `whiteSpace: wrapLines ? 'pre-wrap' : 'pre'` on `<pre>` content lines
    - Set `wordBreak: wrapLines ? 'break-all' : 'normal'` on `<pre>` content lines
    - Line number uses `alignSelf: 'flex-start'` so it stays at top when content wraps
    - _Requirements: 11.2, 11.3, 11.4, 11.5_

- [x] 2. Replace wrap-mode scrolling with native browser scrolling
  - [x] 2.1 Remove the manual `onWheel` handler
    - Delete `handleWheelWrap` callback entirely
    - Remove `onWheel={handleWheelWrap}` from the scroll container
    - _Requirements: 12.5_

  - [x] 2.2 Use `overflow-y: auto` with spacer for both modes
    - Set `overflowY: 'auto'` on the scroll container (was `'hidden'` in wrap mode)
    - Add a spacer `<div>` inside the scroll container with `height: totalHeight` (capped at MAX_SCROLL_HEIGHT)
    - Position rendered lines absolutely within the spacer using `clampedOffset`
    - Both modes now use the same native scroll mechanism
    - _Requirements: 3.5, 12.3, 12.4_

  - [x] 2.3 Attach `onScroll` handler to the unified scroll container
    - Use a single `onScroll={handleScroll}` on the content column for both modes
    - The handler uses `scrollTopToLine` proportional mapping to compute the current line
    - Debounce `onRequestLines` calls (16ms) to avoid flooding the backend
    - _Requirements: 12.6, 12.7_

- [x] 3. Implement scroll suppression mechanism
  - [x] 3.1 Add `suppressScrollRef` React ref
    - Add `const suppressScrollRef = React.useRef(false);` to the component
    - _Requirements: 12.8_

  - [x] 3.2 Update `handleScroll` to check suppression
    - At the top of `handleScroll`, check `if (suppressScrollRef.current) { suppressScrollRef.current = false; return; }`
    - This skips scrollbar position updates when the scroll was caused by a programmatic `scrollTop` set from scrollbar drag
    - _Requirements: 12.8_

- [x] 4. Unify scrollbar handlers
  - [x] 4.1 Add `scrollbarPosition` state
    - Add `const [scrollbarPosition, setScrollbarPosition] = React.useState(0);` to track the scrollbar thumb position
    - Update `handleScroll` to call `setScrollbarPosition(line)` after computing the current line from scrollTop
    - Pass `scrollbarPosition` as the `position` prop to CustomScrollbar (instead of `linesStartLine`)
    - _Requirements: 12.7_

  - [x] 4.2 Replace dual handlers with single `handleScrollbarDrag`
    - Delete `handleScrollbarWrap` and `handleScrollbarNoWrap` callbacks
    - Create a single `handleScrollbarDrag` callback:
      ```
      const handleScrollbarDrag = (pos) => {
        if (!containerRef.current || !fileMeta) return;
        const targetLine = Math.max(0, Math.min(Math.round(pos), fileMeta.totalLines - 1));
        suppressScrollRef.current = true;
        containerRef.current.scrollTop = lineToScrollTop(targetLine, containerRef.current.clientHeight);
        onRequestLines(targetLine, visibleLineCount + BUFFER_LINES);
      };
      ```
    - Pass `handleScrollbarDrag` as `onPositionChange` to CustomScrollbar
    - _Requirements: 12.8, 12.9_

  - [x] 4.3 Remove `handleScrollToLine` callback
    - The old `handleScrollToLine` is no longer needed — `handleScrollbarDrag` replaces it
    - Clean up any references to the removed callback
    - _Requirements: 12.9_

- [x] 5. Verify unidirectional flow
  - [x] 5.1 Verify Direction 1: wheel scroll → scrollbar update (no callback)
    - Confirm that when the user scrolls via wheel/trackpad, `handleScroll` fires, computes the line, and calls `setScrollbarPosition(line)`
    - Confirm that CustomScrollbar receives the new `position` prop and moves its thumb WITHOUT calling `onPositionChange`
    - _Requirements: 10.11, 12.7_

  - [x] 5.2 Verify Direction 2: thumb drag → set scrollTop + suppress (no echo)
    - Confirm that when the user drags the scrollbar thumb, `handleScrollbarDrag` sets `suppressScrollRef = true`, then sets `containerRef.current.scrollTop`
    - Confirm that the resulting `onScroll` event is suppressed (handler returns early)
    - Confirm that no feedback loop occurs
    - _Requirements: 10.12, 12.8_

- [x] 6. Ensure buffer management works with unified path
  - [x] 6.1 Verify buffer request logic
    - Confirm that `handleScroll` requests lines with count = `visibleLineCount + BUFFER_LINES` (buffer exceeds viewport)
    - Confirm that `handleScrollbarDrag` also requests lines with the same buffer formula
    - Confirm that `lastRequestedRef` prevents duplicate requests for the same range
    - _Requirements: 12.1, 12.2_

- [x] 7. Write property-based tests for unified scrolling
  - [x] 7.1 Write property test for unified rendering path
    - **Property 17: Unified Rendering Path**
    - **Validates: Requirements 3.10, 11.8**
    - Verify that DOM structure (element count, types, class names) is identical for wrap=true and wrap=false

  - [x] 7.2 Write property test for native scrolling in all modes
    - **Property 18: Native Scrolling in All Modes**
    - **Validates: Requirements 3.5, 12.3, 12.4, 12.5**
    - Verify scroll container uses `overflow-y: auto` in both modes and has no `onWheel` handler

  - [x] 7.3 Write property test for scroll-to-scrollbar unidirectional flow
    - **Property 19: Scroll-to-Scrollbar Unidirectional Flow**
    - **Validates: Requirements 10.11, 12.7**
    - Verify that native scroll events update scrollbar position prop without triggering onPositionChange

  - [x] 7.4 Write property test for scrollbar-drag scroll suppression
    - **Property 20: Scrollbar-Drag Scroll Suppression**
    - **Validates: Requirements 10.12, 12.8**
    - Verify that programmatic scrollTop from scrollbar drag suppresses the onScroll handler

  - [x] 7.5 Write property test for proportional mapping round-trip
    - **Property 21: Proportional Mapping Round-Trip**
    - **Validates: Requirements 12.6**
    - Verify `lineToScrollTop` then `scrollTopToLine` returns original line within ±1

  - [x] 7.6 Write property test for buffer exceeds viewport
    - **Property 22: Buffer Exceeds Viewport**
    - **Validates: Requirements 12.1, 12.2**
    - Verify requested line count always exceeds visible line count

- [x] 8. Final checkpoint - Verify unified scrolling
  - Build and run the application
  - Open a file and verify:
    - Single DOM structure in both wrap and non-wrap modes (inspect elements)
    - Native pixel-smooth scrolling works in both modes (no line-jump behavior)
    - Custom scrollbar thumb follows scroll position in both modes
    - Dragging scrollbar thumb navigates to correct position without feedback loop
    - No `onWheel` handler preventing default in any mode
    - Line numbers stay at top of wrapped lines
    - Horizontal scrollbar appears only in non-wrap mode
    - Buffer management requests lines proactively
  - Ensure all tests pass, ask the user if questions arise

## Notes

- This refactoring replaced the scrolling/rendering logic implemented in tasks 12 and 13
- The CustomScrollbar component itself (CustomScrollbar.tsx) did NOT need changes — its interface was already correct (range, position, viewportSize, onPositionChange)
- The original spacer-based approach was replaced with a sliding window during implementation because the spacer broke in wrap mode (wrapped lines taller than LINE_HEIGHT invalidated proportional mapping)
- The sliding window approach eliminated the need for `suppressScrollRef`, `lineToScrollTop`, `scrollTopToLine`, `MAX_SCROLL_HEIGHT`, and `clampedOffset`
- New callbacks added to ContentArea props: `onJumpToLine` (scrollbar jump outside buffer), `onTrimBuffer` (buffer trim after DOM measurement)
- App.tsx now implements merge logic for `onLinesResponse` (merge on append, replace on jump via `isJumpRequestRef`)
- Property tests for the old proportional mapping (Property 21) and spacer height (Property 6) are no longer applicable
- All tasks completed successfully — the sliding window works identically for wrap and non-wrap modes
