# Implementation Plan: Sticky Line Numbers

## Overview

Implement sticky line numbers using CSS `position: sticky` so that when wrap mode is enabled and a long line wraps across multiple visual rows, the line number remains pinned at the top of the viewport while any part of that line is visible. The implementation adds a conditional CSS class in `ContentArea.tsx` and a corresponding CSS rule in `app.css`, plus extracts a pure `determineStickyLine()` function for property-based testing.

## Tasks

- [x] 1. Add sticky CSS class and rule
  - [x] 1.1 Add `.line-number-row--sticky` CSS rule to `app.css`
    - Add rule with `position: sticky`, `top: 0`, `background-color: #1e1e1e`, `z-index: 1`
    - Place after existing `.line-number-row` rule
    - _Requirements: 1.1, 3.1, 3.2_

  - [x] 1.2 Apply conditional sticky class in `ContentArea.tsx`
    - Change `className="line-number-row"` to `` className={`line-number-row${wrapLines ? ' line-number-row--sticky' : ''}`} ``
    - No other changes to the line-number `<div>` element
    - _Requirements: 1.1, 2.1, 2.3_

- [x] 2. Extract `determineStickyLine` pure function
  - [x] 2.1 Create `src/EditorApp/src/determineStickyLine.ts` with the pure function
    - Implement `determineStickyLine(wrapMode, lineHeights, scrollTop, lineHeight)` as specified in design
    - Export the function for testing
    - Expose on `window` for potential runtime use
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 4.1, 4.2_

  - [ ]* 2.2 Write property test: Sticky determination correctness (Property 1)
    - **Property 1: Sticky determination correctness**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
    - Generate random `(lineHeights[], scrollTop)` with `wrapMode=true`
    - Verify returned index satisfies: line is wrapped (height > LINE_HEIGHT) AND containerTop < scrollTop < containerBottom, or -1 if none qualifies

  - [ ]* 2.3 Write property test: No sticky in non-wrap mode (Property 2)
    - **Property 2: No sticky in non-wrap mode**
    - **Validates: Requirements 2.1, 2.3**
    - Generate random `(lineHeights[], scrollTop)` with `wrapMode=false`
    - Assert result is always -1

  - [ ]* 2.4 Write property test: At-most-one sticky invariant (Property 3)
    - **Property 3: At-most-one sticky invariant**
    - **Validates: Requirements 4.1, 4.2, 4.3**
    - Generate random configurations with multiple wrapped lines
    - Assert function returns exactly one index or -1 (never multiple candidates possible — the topmost is always selected)

- [x] 3. Checkpoint - Verify implementation compiles and tests pass
  - Ensure TypeScript compiles without errors (`node scripts/tsc.js -p tsconfig.json` from `src/EditorApp`)
  - Ensure all property tests pass (`npm test` from `tests/frontend`)
  - Ask the user if questions arise.

- [ ] 4. Unit tests for class application and CSS correctness
  - [ ]* 4.1 Write unit tests for sticky class conditional logic
    - Test `.line-number-row--sticky` class is applied when `wrapLines=true`
    - Test `.line-number-row--sticky` class is NOT applied when `wrapLines=false`
    - Test line-number styling consistency (width 60px, color #858585, font-size 14px, padding-right 12px) in both states
    - _Requirements: 2.1, 2.2, 3.1_

- [x] 5. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The CSS `position: sticky` approach requires no scroll-event JavaScript — the browser handles containment within the parent `.line-container` automatically
- Single-row lines (height = 20px) are unaffected by sticky because their container height equals the element height

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "2.2", "2.3", "2.4"] },
    { "id": 2, "tasks": ["4.1"] }
  ]
}
```
