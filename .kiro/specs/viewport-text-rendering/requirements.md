# Requirements Document

## Introduction

The editor currently uses a sliding-window virtual scroll that renders up to 400 lines in the DOM and fetches batches of 200 lines from the backend. While this works for moderate files, it breaks down at extreme scale: files with millions of rows and millions of characters per line cause truncation, broken scrollbars, and full-line memory loads on the backend.

This feature replaces the current approach with a true viewport-based rendering system. The backend delivers only the exact rectangular slice of the file visible on screen — bounded by both line range and character range. The frontend uses fixed-width font metrics to compute precise scrollbar positions without DOM measurement, supports smooth scrolling on both axes, and performs incremental viewport updates (trim + append) rather than full re-renders. Two display modes are supported: wrap mode (virtual line splitting) and no-wrap mode (horizontal scrolling).

This spec is distinct from the existing "large-line-support" spec. That spec focuses on chunked reading of individual large lines. This spec focuses on the overall viewport rendering system — how the entire view is managed, scrollbar behavior, smooth scrolling, and the coordination between frontend scroll state and backend data delivery.

## Glossary

- **Viewport_Renderer**: The React component responsible for rendering the visible rectangular slice of file content and managing scroll state.
- **Viewport_Service**: The C# backend service that computes and delivers the visible content slice based on scroll position, viewport dimensions, and wrap state.
- **Viewport_Rect**: The rectangular region of the file currently visible, defined by start line, line count, start column, and column count.
- **Char_Cell**: A single fixed-width character cell. All rendering assumes monospace font, so one character = one cell width.
- **Viewport_Dimensions**: The number of Char_Cells visible horizontally (columns) and vertically (rows) in the current window size.
- **Scroll_State**: The current scroll position expressed as (line offset, column offset, sub-pixel vertical offset, sub-pixel horizontal offset).
- **Wrap_Mode**: Display mode where long lines are split into virtual lines at the viewport column boundary. No horizontal scrollbar is shown.
- **No_Wrap_Mode**: Display mode where lines are not split. A horizontal scrollbar allows navigating to any column position.
- **Virtual_Line**: In Wrap_Mode, a segment of a physical line that fits within the viewport column width. One physical line may produce many virtual lines.
- **Incremental_Update**: A viewport update strategy where only content entering/leaving the view is added/removed, rather than re-rendering the entire viewport.
- **Scrollbar_Thumb**: The draggable element in a scrollbar representing the current viewport position within the total document extent.
- **File_Service**: The existing C# backend service with CompressedLineIndex and ReadLineChunkAsync for line-based file access.
- **Message_Bridge**: The Photino web message bridge used for JSON communication between backend and frontend.
- **Physical_Line**: A single line in the file as delimited by newline characters.

## Requirements

### Requirement 1: Viewport-Based Content Delivery

**User Story:** As a user, I want the editor to display files with millions of rows and millions of characters per line without lag or truncation, so that I can work with any file regardless of size.

#### Acceptance Criteria

1. WHEN the frontend sends a viewport request specifying start line, line count, start column, and column count, THE Viewport_Service SHALL return only the characters within that rectangular region for each requested line.
2. THE Viewport_Service SHALL read only the requested character range from each Physical_Line using seek-based partial reads, without loading the full line into memory.
3. THE Viewport_Service SHALL leverage the existing File_Service.ReadLineChunkAsync capability for partial line reads.
4. WHEN a Physical_Line is shorter than the requested start column, THE Viewport_Service SHALL return an empty string for that line in the response.
5. THE Viewport_Service SHALL include the total Physical_Line count and the character length of each returned line in the response, so the frontend can compute scrollbar extents.

### Requirement 2: Fixed-Width Font Scrollbar Calculation

**User Story:** As a user, I want scrollbars that accurately reflect my position in the file, so that I can navigate to any part of a large file using the scrollbar.

#### Acceptance Criteria

1. THE Viewport_Renderer SHALL compute vertical scrollbar thumb position and size using (visible rows / total rows) without DOM measurement.
2. WHILE in No_Wrap_Mode, THE Viewport_Renderer SHALL compute horizontal scrollbar thumb position and size using (visible columns / max line length) without DOM measurement.
3. WHILE in Wrap_Mode, THE Viewport_Renderer SHALL compute vertical scrollbar position using (visible virtual lines / total virtual lines) without DOM measurement.
4. WHEN the file metadata reports total line count and maximum line length, THE Viewport_Renderer SHALL use these values as the scrollbar extent rather than measuring rendered content.
5. THE Viewport_Renderer SHALL assume all characters occupy exactly one Char_Cell width (monospace font assumption).

### Requirement 3: Wrap Mode with Virtual Line Splitting

**User Story:** As a user, I want a wrap mode that splits long lines at the viewport boundary, so that I can read long lines without horizontal scrolling.

#### Acceptance Criteria

1. WHILE in Wrap_Mode, THE Viewport_Renderer SHALL split each Physical_Line into Virtual_Lines of exactly Viewport_Dimensions.columns characters each (except the last segment which may be shorter).
2. WHILE in Wrap_Mode, THE Viewport_Service SHALL compute the total virtual line count for the file based on each Physical_Line's character length divided by viewport column width (rounded up).
3. WHEN the user scrolls vertically in Wrap_Mode, THE Viewport_Service SHALL map the virtual line offset to the corresponding Physical_Line and character offset, and return the appropriate character slice.
4. WHILE in Wrap_Mode, THE Viewport_Renderer SHALL NOT display a horizontal scrollbar.
5. WHEN the viewport column width changes (window resize), THE Viewport_Service SHALL recompute the virtual line mapping and update the total virtual line count.

### Requirement 4: No-Wrap Mode with Horizontal Scrolling

**User Story:** As a user, I want a no-wrap mode with horizontal scrolling, so that I can view long lines at any column position without wrapping.

#### Acceptance Criteria

1. WHILE in No_Wrap_Mode, THE Viewport_Renderer SHALL display each Physical_Line on a single row, showing only the characters within the current horizontal viewport range.
2. WHILE in No_Wrap_Mode, THE Viewport_Renderer SHALL display a horizontal scrollbar whose range equals the maximum character length across all Physical_Lines in the file.
3. WHEN the user scrolls horizontally, THE Viewport_Renderer SHALL update the start column in the Scroll_State and request the new character range from the Viewport_Service.
4. WHEN the horizontal scroll position exceeds a Physical_Line's length, THE Viewport_Renderer SHALL render empty space for that line.
5. THE Viewport_Service SHALL report the maximum line character length in file metadata so the frontend can set the horizontal scrollbar range without scanning all lines.

### Requirement 5: Smooth Scrolling on Both Axes

**User Story:** As a user, I want smooth pixel-level scrolling in both directions, so that the editor feels responsive and fluid during navigation.

#### Acceptance Criteria

1. WHEN the user scrolls via mouse wheel, THE Viewport_Renderer SHALL scroll by a small increment (a few lines vertically, a few columns horizontally) with sub-pixel precision.
2. THE Viewport_Renderer SHALL maintain sub-pixel scroll offsets in the Scroll_State to enable smooth transitions between character cell boundaries.
3. WHEN the sub-pixel offset crosses a full Char_Cell boundary, THE Viewport_Renderer SHALL update the logical line or column offset and request new content if needed.
4. THE Viewport_Renderer SHALL render content at the sub-pixel offset using CSS transform or equivalent technique to avoid layout recalculation.
5. WHEN scrolling vertically, THE Viewport_Renderer SHALL pre-fetch content one viewport height ahead of the scroll direction to prevent blank flashes.

### Requirement 6: Oversized Buffer with Edge Updates

**User Story:** As a user, I want scrolling to feel seamless without visible re-renders, so that the editor performs well even with very large files.

#### Acceptance Criteria

1. THE Viewport_Service SHALL deliver a content frame larger than the visible viewport on both axes — at least 2× viewport height vertically and at least 2× viewport width horizontally — so that small scrolls are served from the local buffer without a backend request.
2. WHEN the user scrolls vertically by a small amount (mouse wheel), THE Viewport_Renderer SHALL trim lines leaving the buffer at one edge and request only the new lines entering at the opposite edge from the Viewport_Service.
3. WHEN the user scrolls horizontally by a small amount, THE Viewport_Renderer SHALL trim columns leaving the buffer at one side and request only the new column slice entering at the opposite side from the Viewport_Service.
4. WHEN new content arrives from the Viewport_Service, THE Viewport_Renderer SHALL splice it into the buffer at the correct position without disrupting the current scroll position or re-rendering unchanged content.
5. WHEN the scroll position jumps (scrollbar thumb drag) but the target position remains within the current buffer boundaries, THE Viewport_Renderer SHALL perform a partial edge update (trim + append) rather than a full buffer replacement.
6. WHEN the scroll position jumps (scrollbar thumb drag) and the target position falls outside the current buffer boundaries, THE Viewport_Renderer SHALL replace the entire buffer with a new oversized frame centered on the target position.
7. THE incremental edge-update mechanism SHALL complete within 16ms (one frame) for mouse wheel scroll events to maintain 60fps rendering.
8. THE Viewport_Renderer SHALL trigger a prefetch request when the visible viewport approaches within a configurable threshold of the buffer edge (e.g., 25% of buffer remaining in the scroll direction), so that new content arrives before the user reaches the buffer boundary.
9. THE Viewport_Renderer SHALL NOT wait until the scroll position reaches the buffer edge to request more content; the prefetch SHALL be invisible to the user — no blank frames or loading indicators during normal scrolling.
10. WHEN scrolling within the already-buffered region and no prefetch is needed, THE Viewport_Renderer SHALL handle rendering entirely on the frontend with no backend round-trip.

### Requirement 7: Scrollbar Thumb Drag (Arbitrary Position Jump)

**User Story:** As a user, I want to drag the scrollbar thumb to jump to any position in a multi-million line file, so that I can navigate quickly to distant parts of the file.

#### Acceptance Criteria

1. WHEN the user drags the vertical scrollbar thumb, THE Viewport_Renderer SHALL compute the target line from the thumb position using (thumb_fraction × total_lines).
2. WHEN the user drags the horizontal scrollbar thumb in No_Wrap_Mode, THE Viewport_Renderer SHALL compute the target column from the thumb position using (thumb_fraction × max_line_length).
3. WHEN a scrollbar drag results in a position outside the current buffer, THE Viewport_Renderer SHALL request a new viewport centered on the target position from the Viewport_Service.
4. THE Viewport_Renderer SHALL debounce scrollbar drag requests to avoid flooding the backend during rapid thumb movement.
5. WHEN the new viewport data arrives after a jump, THE Viewport_Renderer SHALL replace the buffer and render the new content at the target position without intermediate blank frames.

### Requirement 8: Partial Line Reads on Backend

**User Story:** As a developer, I want the backend to read only the needed character range per line, so that memory usage stays bounded regardless of line length.

#### Acceptance Criteria

1. WHEN the Viewport_Service builds a viewport response, THE Viewport_Service SHALL read each line using File_Service.ReadLineChunkAsync with the requested start column and column count.
2. THE Viewport_Service SHALL NOT load any full Physical_Line into memory when serving a viewport request, regardless of the line's total character length.
3. WHEN serving a viewport of N lines with column width W, THE Viewport_Service SHALL allocate at most O(N × W) characters of memory for the response payload.
4. THE Viewport_Service SHALL handle lines of any length (including millions of characters) without degraded performance, by seeking directly to the requested byte offset.
5. IF a line's character length is less than the requested start column, THEN THE Viewport_Service SHALL skip the file read for that line and return an empty string.

### Requirement 9: Viewport Request Message Protocol

**User Story:** As a developer, I want a well-defined message protocol for viewport requests and responses, so that frontend and backend communicate efficiently.

#### Acceptance Criteria

1. THE frontend SHALL send a `RequestViewport` message containing: start line, line count, start column, column count, and wrap mode flag.
2. THE Viewport_Service SHALL respond with a `ViewportResponse` message containing: the requested lines' content (character slices), total Physical_Line count, per-line character lengths for the returned range, and maximum line length across the file.
3. WHEN in Wrap_Mode, THE `RequestViewport` message SHALL specify the target virtual line offset and the viewport column width, and THE Viewport_Service SHALL resolve these to the correct Physical_Line and character offset.
4. THE `ViewportResponse` payload SHALL NOT exceed 4 MB of serialized JSON to stay within Message_Bridge limits.
5. IF the requested viewport would produce a response exceeding 4 MB, THEN THE Viewport_Service SHALL reduce the line count and indicate truncation in the response.

### Requirement 10: Maximum Line Length Metadata

**User Story:** As a developer, I want the backend to provide the maximum line length efficiently, so that the frontend can set horizontal scrollbar range without scanning all lines.

#### Acceptance Criteria

1. WHEN a file is opened, THE File_Service SHALL compute and store the maximum character length across all Physical_Lines during the line index scan.
2. THE maximum line length SHALL be included in the `FileOpenedResponse` metadata sent to the frontend.
3. THE File_Service SHALL update the stored maximum line length when the file is refreshed.
4. THE maximum line length computation SHALL add no more than 10% overhead to the existing file scan time.
5. WHEN the file uses a variable-width encoding (UTF-8), THE File_Service SHALL estimate character length from byte length using the encoding's average bytes-per-character ratio, accepting a conservative overestimate.

### Requirement 11: Wrap Mode Virtual Line Count Computation

**User Story:** As a developer, I want the backend to compute the total virtual line count for wrap mode, so that the vertical scrollbar can be sized correctly.

#### Acceptance Criteria

1. WHEN the frontend sends a viewport request in Wrap_Mode with a specified column width, THE Viewport_Service SHALL compute the total virtual line count as the sum of ceil(line_char_length / column_width) across all Physical_Lines.
2. THE Viewport_Service SHALL include the total virtual line count in the `ViewportResponse` when in Wrap_Mode.
3. THE virtual line count computation SHALL complete in O(N) time where N is the number of Physical_Lines, using stored line lengths rather than reading file content.
4. WHEN the viewport column width changes, THE frontend SHALL request an updated virtual line count from the Viewport_Service.
5. FOR ALL Physical_Lines, THE virtual line count for a line SHALL equal ceil(max(1, line_char_length) / column_width), ensuring empty lines count as one virtual line.

### Requirement 12: Viewport Dimensions Auto-Detection

**User Story:** As a user, I want the editor to automatically determine how many rows and columns fit in the viewport, so that content fills the available space without manual configuration.

#### Acceptance Criteria

1. WHEN the editor window loads or resizes, THE Viewport_Renderer SHALL compute Viewport_Dimensions (rows and columns) from the container pixel dimensions and the fixed Char_Cell size.
2. THE Viewport_Renderer SHALL compute Char_Cell size once at startup by measuring a reference character in the monospace font.
3. WHEN the window is resized, THE Viewport_Renderer SHALL recompute Viewport_Dimensions and request a new viewport from the Viewport_Service if dimensions changed.
4. THE Viewport_Renderer SHALL report the computed Viewport_Dimensions to the Viewport_Service with each viewport request.
5. THE Char_Cell measurement SHALL occur only once per font change, not on every render frame.

### Requirement 13: Backward Compatibility with Existing Features

**User Story:** As a user, I want existing features (file open, search, copy) to continue working with the new viewport system, so that no functionality is lost.

#### Acceptance Criteria

1. WHEN a file is opened, THE Viewport_Renderer SHALL display the first viewport of content using the new viewport protocol, replacing the current sliding-window approach.
2. THE existing File_Service.ReadLineChunkAsync and CompressedLineIndex SHALL remain unchanged; the Viewport_Service SHALL compose viewport responses using these existing primitives.
3. WHEN the large-line-support features (search in large lines, copy from large lines) are used, THE Viewport_Renderer SHALL coordinate with the existing chunk request protocol for those operations.
4. THE existing `FileOpenedResponse` message format SHALL be extended (not replaced) with the new maximum line length field.
5. WHEN the viewport system is active, THE frontend SHALL NOT use the legacy `RequestLines`/`LinesResponse` protocol for content display.

### Requirement 14: Correct Unicode Handling During Seek-Based Reads

**User Story:** As a user, I want text with variable-length Unicode characters (e.g., UTF-8 multibyte, surrogate pairs) to display correctly regardless of scroll position, so that no characters are corrupted or garbled.

#### Acceptance Criteria

1. WHEN seeking to a character offset within a line encoded in a variable-width encoding (UTF-8), THE Viewport_Service SHALL NOT seek to an arbitrary byte position that could land in the middle of a multibyte character sequence.
2. THE Viewport_Service SHALL always begin reading from a known valid character boundary — either the line start offset (scanning forward character-by-character) or a previously cached character-to-byte mapping.
3. WHEN a character occupies multiple bytes (e.g., 2–4 bytes in UTF-8), THE Viewport_Service SHALL decode the complete byte sequence before returning it, never returning partial byte sequences as characters.
4. WHEN a character is a Unicode surrogate pair (characters outside the Basic Multilingual Plane), THE Viewport_Service SHALL treat the pair as a single logical character for column counting and slicing purposes.
5. THE Viewport_Service SHALL produce identical output for a given (line, startColumn, columnCount) request regardless of whether the read was performed via sequential scan or seek-based access — no correctness difference between access methods.
