# Line Wrapping

## Overview
Add a line wrapping feature that allows users to toggle between wrapped and unwrapped line display modes. When enabled, long lines wrap to fit within the visible width of the content area, eliminating the need for horizontal scrolling. The line numbering system preserves logical line numbers regardless of how many visual rows a wrapped line occupies.

## Architecture

**Wrapping Disabled (default)**:
```
┌────────┬──────────────────────────────────────────┬───────────┐
│   35   │ This is a very long line that extends... │ Custom    │
│   36   │ Short line                                │ Scrollbar │
│   37   │ Another long line that extends beyond... │           │
└────────┴──────────────────────────────────────────┴───────────┘
         └─ Horizontal scrollbar appears ─────────────┘
```

**Wrapping Enabled**:
```
┌────────┬──────────────────────────────────────────┬───────────┐
│   35   │ This is a very long line that            │ Custom    │
│        │ extends beyond the visible width and     │ Scrollbar │
│        │ wraps to multiple visual rows            │           │
│   36   │ Short line                                │           │
│   37   │ Another long line that extends           │           │
│        │ beyond the visible width                 │           │
└────────┴──────────────────────────────────────────┴───────────┘
         └─ No horizontal scrollbar ──────────────────┘
```

Key behaviors:
- Line numbers appear only on the first visual row of each logical line
- Vertical scrollbar represents logical lines (not visual rows)
- Horizontal scrolling is disabled when wrapping is enabled
- `white-space: pre-wrap` preserves whitespace while allowing wrapping

## Tasks

- [x] 1. Add wrapLines state to App component
  - [x] 1.1 Add wrapLines state to App.tsx
    - Add `const [wrapLines, setWrapLines] = React.useState(false);` to App component
    - Default value is `false` (wrapping disabled)
    - _Requirements: 11.7_
  
  - [x] 1.2 Add handleWrapLinesChange callback
    - Create `handleWrapLinesChange` callback that updates `wrapLines` state
    - Pass this callback to StatusBar component
    - _Requirements: 11.2, 11.3_
  
  - [x] 1.3 Pass wrapLines prop to ContentArea and StatusBar
    - Update ContentArea props to include `wrapLines`
    - Update StatusBar props to include `wrapLines` and `onWrapLinesChange`
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 2. Update StatusBar to include Wrap Lines checkbox
  - [x] 2.1 Update StatusBar interface and props
    - Add `wrapLines: boolean` to StatusBarProps
    - Add `onWrapLinesChange: (enabled: boolean) => void` to StatusBarProps
    - _Requirements: 11.1_
  
  - [x] 2.2 Add checkbox control to StatusBar rendering
    - Add a checkbox input element labeled "Wrap Lines"
    - Set `checked` attribute to `wrapLines` prop value
    - Call `onWrapLinesChange` with the new value when checkbox changes
    - Position the checkbox on the right side of the status bar
    - _Requirements: 11.1, 11.2, 11.3_
  
  - [x] 2.3 Style the checkbox control
    - Add CSS for the checkbox and label
    - Ensure proper spacing and alignment with existing status bar items
    - Use consistent styling with the rest of the status bar

- [x] 3. Update ContentArea to handle line wrapping behavior
  - [x] 3.1 Add wrapLines prop to ContentArea interface
    - Add `wrapLines: boolean` to ContentAreaProps
    - _Requirements: 11.2, 11.3_
  
  - [x] 3.2 Update content column overflow behavior
    - Set `overflowX: 'hidden'` when `wrapLines` is true
    - Set `overflowX: 'auto'` when `wrapLines` is false
    - _Requirements: 11.3, 11.6_
  
  - [x] 3.3 Update line rendering with conditional white-space
    - Change `<pre>` elements to use `white-space: pre-wrap` when `wrapLines` is true
    - Use `white-space: pre` when `wrapLines` is false
    - _Requirements: 11.2, 11.3_
  
  - [x] 3.4 Restructure line rendering for wrapped line number display
    - Refactor line rendering to use a container structure (e.g., `<div className="line-container">`)
    - Within each container, render line number and line content side-by-side
    - Line number should have `align-self: flex-start` to stay at the top when line wraps
    - This ensures line numbers appear only on the first visual row of wrapped lines
    - _Requirements: 11.4, 11.5_
  
  - [x] 3.5 Update line numbers column rendering
    - Ensure line numbers column uses the same container structure
    - Line numbers should align with the first visual row of each logical line
    - _Requirements: 11.4, 11.5_
  
  - [x] 3.6 Verify scrollbar behavior with wrapping
    - Ensure the custom scrollbar continues to represent logical lines (totalLines)
    - Verify that `range` prop passed to CustomScrollbar is always based on `fileMeta.totalLines`
    - No changes needed to CustomScrollbar component itself
    - _Requirements: 11.6_

- [ ]* 4. Add property-based tests for line wrapping
  - [ ]* 4.1 Write property test for wrapping state toggle idempotence
    - **Property 14: Line Wrapping State Toggle Idempotence**
    - **Validates: Requirements 11.2, 11.3**
    - Test that toggling wrapping twice returns to original state
    - Verify no other state is affected (file metadata, visible lines, scroll position)
  
  - [ ]* 4.2 Write property test for logical line numbering preservation
    - **Property 15: Logical Line Numbering Preservation with Wrapping**
    - **Validates: Requirements 11.4**
    - For any file content and visible line range, verify line numbers equal 1-based position
    - Test with various line lengths and wrapping states
  
  - [ ]* 4.3 Write property test for vertical scrollbar logical line representation
    - **Property 16: Vertical Scrollbar Logical Line Representation**
    - **Validates: Requirements 11.6**
    - Verify scrollbar range equals totalLines regardless of wrapping state
    - Test with files of various sizes and wrapping enabled/disabled

- [ ]* 5. Add unit tests for line wrapping behavior
  - [ ]* 5.1 Test StatusBar checkbox rendering
    - Verify checkbox is rendered with correct label
    - Verify checkbox reflects wrapLines prop value
    - Verify onChange handler is called with correct value
  
  - [ ]* 5.2 Test ContentArea overflow behavior
    - Verify `overflowX` is 'hidden' when wrapLines is true
    - Verify `overflowX` is 'auto' when wrapLines is false
  
  - [ ]* 5.3 Test ContentArea white-space behavior
    - Verify line content uses `white-space: pre-wrap` when wrapLines is true
    - Verify line content uses `white-space: pre` when wrapLines is false
  
  - [ ]* 5.4 Test line number rendering with wrapped lines
    - Verify line numbers appear only once per logical line
    - Verify line numbers align with first visual row of wrapped lines

- [x] 6. Checkpoint - Verify line wrapping functionality
  - Build and run the application
  - Open a file with long lines
  - Toggle the "Wrap Lines" checkbox and verify:
    - Lines wrap when enabled, unwrap when disabled
    - Horizontal scrollbar appears/disappears correctly
    - Line numbers appear only on first visual row of wrapped lines
    - Vertical scrollbar continues to represent logical lines
    - Scroll position is preserved when toggling wrapping
  - Test with files of various sizes and line lengths
  - Ensure all tests pass, ask the user if questions arise

## Notes
- The line wrapping feature is purely a UI concern — no backend changes are required
- The custom scrollbar continues to operate in logical line space regardless of wrapping state
- Line numbers must be rendered in a way that allows them to stay at the top of wrapped lines (flexbox or similar layout)
- The default state is wrapping disabled to match traditional text editor behavior
- Property tests validate the formal correctness properties defined in the design document
