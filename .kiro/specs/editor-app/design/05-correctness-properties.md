# Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

## Property 1: Line Offset Index Correctness

*For any* text file content, when the Backend builds a line offset index during `OpenFileAsync`, seeking to the byte offset recorded for line N and reading one line SHALL produce exactly the Nth line of the original file content.

**Validates: Requirements 6.2**

## Property 2: ReadLinesAsync Round-Trip Correctness

*For any* text file content (including various line endings, whitespace characters, tabs, and special characters) and *for any* valid line range (startLine, lineCount) within the file, `ReadLinesAsync(filePath, startLine, lineCount)` SHALL return an array of lines that exactly matches the corresponding lines from the original file content, preserving all whitespace and characters.

**Validates: Requirements 3.1, 3.7, 6.4**

## Property 3: File Metadata Accuracy

*For any* text file, when `OpenFileAsync` completes, the returned `FileOpenMetadata` SHALL have a `TotalLines` value that exactly matches the number of lines in the file (where lines are delimited by \n, \r\n, or \r) and a `FileSizeBytes` value that exactly matches the file's size on disk.

**Validates: Requirements 2.2, 5.2**

## Property 4: Dialog Cancellation Idempotence

*For any* application state (whether a file is loaded or not, regardless of current scroll position or displayed lines), when the user cancels the File_Picker dialog, the application state SHALL remain identical to the state before the dialog was opened.

**Validates: Requirements 2.3**

## Property 5: Line Number Sequential Generation

*For any* startLine offset and lineCount, the displayed line numbers SHALL form a sequential series from (startLine + 1) to (startLine + lineCount), where each line number corresponds to its 1-based position in the file.

**Validates: Requirements 3.2**

## ~~Property 6: Virtual Scrollbar Height~~ (Removed)

*Removed — the sliding window architecture no longer uses a spacer element. There is no fake height div. The CustomScrollbar range is set to `totalLines` directly.*

## Property 7: Title Bar Format Consistency

*For any* file name, when a file is successfully opened, the Title_Bar SHALL display the title in the format "{fileName} - Editor" where {fileName} is the base name of the file without the full path.

**Validates: Requirements 4.2**

## Property 8: File Size Human-Readable Formatting

*For any* file size in bytes, the Status_Bar SHALL display the size in human-readable format following these rules:
- Sizes < 1024 bytes: display as "X bytes"
- Sizes >= 1024 and < 1048576 bytes: display as "X.Y KB" (rounded to 1 decimal place)
- Sizes >= 1048576 bytes: display as "X.Y MB" (rounded to 1 decimal place)

**Validates: Requirements 5.1**

## Property 9: Encoding Detection Correctness

*For any* file with a detectable BOM (UTF-8 BOM, UTF-16 LE/BE BOM), the Backend's encoding detection SHALL return the correct encoding name. For files without a BOM, the Backend SHALL default to UTF-8.

**Validates: Requirements 5.3**

## Property 10: LinesResponse Message Structure Correctness

*For any* `LinesResult` (containing startLine, lines array, and totalLines), when the Backend serializes it as a `LinesResponse` message, the resulting JSON SHALL conform to the `MessageEnvelope` schema with type `"LinesResponse"` and a payload containing `startLine`, `lines`, and `totalLines` fields matching the original data.

**Validates: Requirements 7.1**

## Property 11: CustomScrollbar External Position Update Correctness

*For any* valid range (> 0), viewportSize (> 0), and position (0 ≤ position ≤ range), when the position prop is updated externally, the thumb SHALL move to the correct proportional location within the track AND the onPositionChange callback SHALL NOT be called.

**Validates: Requirements 10.4**

## Property 12: CustomScrollbar Drag Position Calculation

*For any* valid range (> 0), viewportSize (> 0), and any thumb drag position within the track (0 to scrollableTrack), the reported position SHALL satisfy the linear mapping: `calculatedPosition = (thumbTop / scrollableTrack) * range`, such that top of track reports 0, bottom of track reports range, and center of track reports range / 2.

**Validates: Requirements 10.5, 10.6, 10.7, 10.8**

## Property 13: CustomScrollbar Thumb Size Proportionality

*For any* valid range (> 0) and viewportSize (> 0), the thumb height SHALL equal `max(MIN_THUMB_HEIGHT, (viewportSize / range) * trackHeight)`, ensuring the thumb is proportional to the viewport's share of the total range while remaining at least the minimum grabbable size.

**Validates: Requirements 10.9**

## Property 14: Line Wrapping State Toggle Idempotence

*For any* application state with a file loaded, toggling the "Wrap Lines" checkbox twice (enabled then disabled, or disabled then enabled) SHALL return the wrapping state to its original value without affecting any other application state (file metadata, visible lines, scroll position, or error state).

**Validates: Requirements 11.2, 11.3**

## Property 15: Logical Line Numbering Preservation with Wrapping

*For any* file content and any visible line range, when line wrapping is enabled, the line number displayed for each logical line SHALL equal its 1-based position in the file, regardless of how many visual rows that line occupies when wrapped.

**Validates: Requirements 11.4**

## Property 16: Vertical Scrollbar Logical Line Representation

*For any* file with totalLines logical lines, the vertical scrollbar range SHALL equal totalLines regardless of the wrapping state, ensuring the scrollbar represents logical lines (not visual rows) whether wrapping is enabled or disabled.

**Validates: Requirements 11.6**

## Property 17: Unified Rendering Path

*For any* set of lines and *for any* wrapLines boolean value, the ContentArea SHALL produce the same DOM structure (same element count, same element types, same class names) in both wrapped and non-wrapped modes. Only CSS style properties (whiteSpace, overflowX, wordBreak) SHALL differ between the two modes.

**Validates: Requirements 3.10, 11.8**

## Property 18: Native Scrolling in All Modes (Sliding Window)

*For any* wrapLines boolean value, the ContentArea viewport div SHALL have `overflow-y: auto` and SHALL NOT have any `onWheel` handler that calls `preventDefault` or translates wheel deltas to line jumps. The viewport contains real DOM lines (up to WINDOW_SIZE = 400) — no spacer div, no fake height. This ensures pixel-smooth native browser scrolling in both wrapped and non-wrapped modes.

**Validates: Requirements 3.5, 12.3, 12.4, 12.5**

## Property 19: Scroll-to-Scrollbar Unidirectional Flow

*For any* valid scrollTop value within the scroll container, when a native scroll event occurs (from mouse wheel or trackpad), the ContentArea SHALL update the CustomScrollbar `position` prop to the corresponding line number, and the CustomScrollbar SHALL NOT call `onPositionChange` in response to this prop update.

**Validates: Requirements 10.11, 12.7**

## Property 20: Scrollbar-Drag Scroll Suppression

*For any* valid line position reported by a CustomScrollbar thumb drag via `onPositionChange`, the ContentArea SHALL programmatically set the scroll container's `scrollTop` to the corresponding pixel offset AND suppress the resulting `onScroll` event handler from updating the scrollbar position, preventing a feedback loop.

**Validates: Requirements 10.12, 12.8**

## ~~Property 21: Proportional Mapping Round-Trip~~ (Removed)

*Removed — the sliding window architecture no longer uses proportional mapping (`lineToScrollTop` / `scrollTopToLine`). Line positions are determined by walking DOM elements and measuring actual heights. No mathematical mapping between scrollTop and line numbers exists.*

## Property 22: Buffer Merge on Append

*For any* existing buffer (linesStartLine, lines[]) and *for any* new `LinesResponse` from the Backend triggered by edge-proximity scrolling (not a jump request), the App SHALL merge the new lines into the existing buffer such that the merged buffer covers the union of both ranges. The merged buffer SHALL contain all lines from both the previous buffer and the new response, with new lines overwriting any overlap.

**Validates: Requirements 12.1, 12.2, 12.7**

## Property 23: Sliding Window Trim Stability

*For any* buffer that exceeds WINDOW_SIZE (400 lines) after a merge, when `useLayoutEffect` trims lines from the top of the buffer, the ContentArea SHALL adjust `scrollTop` by the exact sum of `offsetHeight` values of the removed DOM `.line-container` elements. This ensures the visible content does not shift — the user sees no visual jump despite the DOM mutation. When trimming from the bottom, no `scrollTop` adjustment is needed.

**Validates: Requirements 12.1, 12.8**
