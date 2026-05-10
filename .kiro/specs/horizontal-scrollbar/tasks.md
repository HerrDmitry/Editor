# Implementation Plan: Horizontal Scrollbar

## Overview

Replace the hardcoded `H_VIEWPORT_CHARS`-based horizontal scrollbar (`<input type="range">`) with a proper `CustomScrollbar` component in horizontal orientation. Implementation proceeds: CustomScrollbar orientation support → dynamic viewport column measurement → ContentArea scroll state + clamping → wheel/drag input → integration with existing large-line chunk loading. No backend changes needed.

## Tasks

- [x] 1. CustomScrollbar: Add horizontal orientation support
  - [x] 1.1 Add `orientation` prop to `CustomScrollbar` component
    - Add optional `orientation?: 'vertical' | 'horizontal'` prop (default `'vertical'`) to `CustomScrollbarProps` interface in `src/EditorApp/src/CustomScrollbar.tsx`
    - When `orientation === 'horizontal'`: render track horizontally (height: 14px, width: 100%), thumb uses `left`/`width` instead of `top`/`height`
    - Mouse tracking uses `clientX` and horizontal offset instead of `clientY`/vertical offset
    - Add CSS classes: `.custom-scrollbar--horizontal`, `.custom-scrollbar--horizontal .custom-scrollbar__track`, `.custom-scrollbar--horizontal .custom-scrollbar__thumb`
    - Enforce minimum thumb width of 20px for horizontal orientation
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 1.2 Write unit tests for CustomScrollbar horizontal orientation
    - Test: horizontal orientation renders correct CSS classes
    - Test: thumb width matches proportional formula `(viewportSize / (range + viewportSize)) * trackWidth`
    - Test: minimum thumb width enforced at 20px
    - Test: mouse drag uses clientX for horizontal, clientY for vertical
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 2. ContentArea: Dynamic viewport column measurement
  - [x] 2.1 Add character cell width measurement probe
    - Add hidden `<span>` ref (`charProbeRef`) containing a single monospace character to `ContentArea.tsx`
    - Measure `charCellWidth` from probe element's `getBoundingClientRect().width` on mount
    - Store in state with fallback default of 7.2px if measurement returns 0
    - _Requirements: 5.1_

  - [x] 2.2 Add ResizeObserver for dynamic `viewportColumns` calculation
    - Add `contentMeasureRef` to the content column container div
    - Attach `ResizeObserver` to measure pixel width of content area
    - Compute `viewportColumns = Math.floor(pixelWidth / charCellWidth)` on each resize callback
    - Clamp `viewportColumns` to minimum 1 (collapsed window edge case)
    - Remove hardcoded `H_VIEWPORT_CHARS` constant usage for horizontal scrolling
    - Fall back to `window.resize` event with debounce if ResizeObserver unavailable
    - _Requirements: 5.1, 5.2_

  - [x] 2.3 Write property test for viewport column calculation (Property 5)
    - **Property 5: Viewport column calculation**
    - Generate random `(pixelWidth: positive number, charCellWidth: positive number > 0)`. Verify `viewportColumns === Math.floor(pixelWidth / charCellWidth)`.
    - **Validates: Requirements 5.1**

- [x] 3. ContentArea: Scroll column state and clamping
  - [x] 3.1 Implement `clampScrollColumn` utility function
    - Create pure function: `clampScrollColumn(col: number, maxLineLength: number, viewportColumns: number): number`
    - Logic: `Math.max(0, Math.min(col, Math.max(0, maxLineLength - viewportColumns)))`
    - Export from a shared utility or define in `ContentArea.tsx`
    - _Requirements: 3.5, 4.3, 5.3, 5.4_

  - [x] 3.2 Replace `hScrollCol` state with clamped `scrollColumn`
    - Replace existing `hScrollCol` state with `scrollColumn` state
    - Apply `clampScrollColumn` on every state update (drag, wheel, resize, file open)
    - When `viewportColumns >= maxLineLength` after resize: reset `scrollColumn` to 0
    - When `scrollColumn + viewportColumns > maxLineLength` after resize: clamp to `maxLineLength - viewportColumns`
    - _Requirements: 3.5, 5.3, 5.4_

  - [x] 3.3 Write property test for scrollColumn clamping invariant (Property 2)
    - **Property 2: ScrollColumn clamping invariant**
    - Generate random `(scrollColumn: integer including negative/large, maxLineLength: non-negative integer, viewportColumns: positive integer)`. Verify result equals `Math.max(0, Math.min(scrollColumn, Math.max(0, maxLineLength - viewportColumns)))`.
    - **Validates: Requirements 3.5, 4.3, 5.3, 5.4**

- [x] 4. Checkpoint — Core infrastructure complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. ContentArea: Horizontal scrollbar rendering and visibility
  - [x] 5.1 Render horizontal `CustomScrollbar` with visibility logic
    - Render `<CustomScrollbar orientation="horizontal" ... />` below content area
    - Show when `!wrapMode && maxLineLength > viewportColumns`
    - Hide when `wrapMode === true` OR `maxLineLength <= viewportColumns`
    - Props: `range = Math.max(0, maxLineLength - viewportColumns)`, `position = scrollColumn`, `viewportSize = viewportColumns`
    - Remove old `<input type="range">` horizontal scrollbar element
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1_

  - [x] 5.2 Handle `onPositionChange` from horizontal scrollbar thumb drag
    - Wire `onPositionChange` callback to update `scrollColumn` via `clampScrollColumn`
    - Re-render content at new `scrollColumn` offset
    - When `maxLineLength <= viewportColumns`: scrollbar stays at 0, does not respond to drag
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 5.3 Write property test for scrollbar visibility determination (Property 1)
    - **Property 1: Scrollbar visibility determination**
    - Generate random `(wrapMode: boolean, maxLineLength: non-negative integer, viewportColumns: positive integer)`. Verify scrollbar visible iff `wrapMode === false && maxLineLength > viewportColumns`.
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4**

  - [x] 5.4 Write property test for scrollbar prop computation (Property 3)
    - **Property 3: Scrollbar prop computation**
    - Generate valid `(maxLineLength, viewportColumns, scrollColumn)` where `maxLineLength > viewportColumns`. Verify `range === maxLineLength - viewportColumns`, `position === scrollColumn`, `viewportSize === viewportColumns`.
    - **Validates: Requirements 2.1, 3.2**

- [x] 6. ContentArea: Shift+wheel horizontal scrolling
  - [x] 6.1 Implement Shift+wheel event handler for horizontal scroll
    - On `wheel` event with `shiftKey === true` (or `deltaX !== 0`): adjust `scrollColumn` by `±3 * wheelTicks`
    - Wheel-up/wheel-left → scroll left (decrease), wheel-down/wheel-right → scroll right (increase)
    - Apply `clampScrollColumn` after adjustment
    - Update scrollbar thumb position to reflect new `scrollColumn`
    - In wrap mode: ignore Shift+wheel, do not change `scrollColumn`
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 6.2 Write property test for wheel scroll behavior (Property 4)
    - **Property 4: Wheel scroll behavior**
    - Generate random `(wrapMode: boolean, currentScrollCol: integer, wheelTicks: integer, maxLineLength: non-negative, viewportColumns: positive)`. If `wrapMode`: verify scrollColumn unchanged. If `!wrapMode`: verify scrollColumn changes by `3 × wheelTicks` then clamped to `[0, maxLineLength - viewportColumns]`.
    - **Validates: Requirements 4.1, 4.3, 4.4**

- [x] 7. ContentArea: Viewport request integration
  - [x] 7.1 Pass `scrollColumn` as `startColumn` in viewport requests
    - Update all viewport request calls to pass `scrollColumn` as `startColumn` parameter (was `0`)
    - Pass `viewportColumns` as `columnCount` parameter (was large constant)
    - Ensure vertical scroll does not reset `scrollColumn`
    - _Requirements: 6.1, 6.2_

  - [x] 7.2 Write property test for vertical scroll preserving horizontal position (Property 6)
    - **Property 6: Vertical scroll preserves horizontal position**
    - Generate sequence of vertical scroll actions with initial `scrollColumn`. Verify `scrollColumn` unchanged after each vertical scroll, and viewport requests include `startColumn === scrollColumn`.
    - **Validates: Requirements 6.1, 6.2**

- [x] 8. Checkpoint — Scrollbar interaction complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. ContentArea: Large-line chunk loading integration
  - [x] 9.1 Integrate horizontal scroll with large-line chunk requests
    - On `scrollColumn` change with visible large lines (> 65,536 chars): request chunks covering column window (3× viewportColumns centered on scrollColumn)
    - Normal lines (≤ 65,536 chars): render from loaded buffer via substring, no backend request
    - Large lines shorter than `scrollColumn`: render as empty space, no chunk request
    - _Requirements: 7.1, 7.2, 7.5_

  - [x] 9.2 Handle stale chunk responses
    - On chunk response arrival: store in cache regardless of current scroll position
    - Only render chunk if its range intersects current visible viewport at current `scrollColumn`
    - Display monospace blank placeholder while chunk request pending
    - _Requirements: 7.3, 7.4_

  - [x] 9.3 Write property test for selective chunk requesting (Property 7)
    - **Property 7: Selective chunk requesting for large lines**
    - Generate random `scrollColumn` change with array of visible lines (mixed lengths). Verify chunk requests issued only for lines with `length > 65_536` AND `length > scrollColumn`. Normal lines never trigger requests.
    - **Validates: Requirements 7.1, 7.2, 7.5**

  - [x] 9.4 Write property test for stale chunk response caching (Property 8)
    - **Property 8: Stale chunk response caching**
    - Generate chunk responses arriving for positions where `scrollColumn` has moved past the chunk range. Verify chunk stored in cache but viewport renders at current `scrollColumn` (not stale position).
    - **Validates: Requirements 7.4**

- [x] 10. ContentArea: Resize re-evaluation and file open
  - [x] 10.1 Re-evaluate scrollbar visibility and clamp on resize
    - When `viewportColumns` changes: re-check `maxLineLength > viewportColumns` for visibility
    - If `viewportColumns >= maxLineLength`: hide scrollbar, reset `scrollColumn` to 0
    - If `scrollColumn + viewportColumns > maxLineLength`: clamp `scrollColumn`
    - Update scrollbar `range` and thumb size on every `viewportColumns` change
    - _Requirements: 1.5, 5.2, 5.3, 5.4_

  - [x] 10.2 Show/hide scrollbar on file open
    - When file opens in no-wrap mode: evaluate `maxLineLength > viewportColumns` immediately
    - Display scrollbar if condition met, hide otherwise
    - Reset `scrollColumn` to 0 on new file open
    - _Requirements: 1.4_

- [x] 11. Final checkpoint — All features integrated
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (fast-check)
- Unit tests validate specific examples and edge cases (vitest)
- No backend changes needed — ViewportService already supports startColumn/columnCount
- Property test files go in `tests/frontend/properties/` following existing naming convention
- All 8 correctness properties from the design are covered by PBT sub-tasks

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "3.1"] },
    { "id": 1, "tasks": ["1.2", "2.1", "3.2", "3.3"] },
    { "id": 2, "tasks": ["2.2", "5.1"] },
    { "id": 3, "tasks": ["2.3", "5.2", "5.3", "5.4"] },
    { "id": 4, "tasks": ["6.1", "7.1"] },
    { "id": 5, "tasks": ["6.2", "7.2"] },
    { "id": 6, "tasks": ["9.1", "10.1", "10.2"] },
    { "id": 7, "tasks": ["9.2", "9.3", "9.4"] }
  ]
}
```
