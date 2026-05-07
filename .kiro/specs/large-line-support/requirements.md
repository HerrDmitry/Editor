# Requirements Document

## Introduction

The editor currently reads entire lines into memory as single strings (`StreamReader.ReadLineAsync()`) and sends them as `string[]` in JSON messages. The frontend renders each line as a single `<pre>` element. This architecture fails catastrophically when a file contains lines exceeding ~100 MB: the backend throws `OutOfMemoryException`, the JSON message bridge cannot serialize a 100 MB string, and the browser DOM cannot render it.

This feature introduces chunked line reading, chunked message delivery, and horizontal virtualization so that lines of arbitrary length are supported without excessive memory consumption at any layer.

## Glossary

- **File_Service**: The C# backend service (`FileService`) responsible for opening files, building line-offset indices, and reading line ranges.
- **Line_Chunk**: A fixed-size substring of a logical line, identified by line number and column offset. The backend reads and transmits line content in chunks rather than as a single string.
- **Chunk_Size**: The maximum number of characters in a single Line_Chunk. A configurable constant (e.g., 64 KB of characters).
- **Line_Length_Index**: Metadata stored alongside the line offset index that records the byte length of each line, enabling the backend to know a line's total length without reading it.
- **Large_Line**: A line whose character length exceeds the Chunk_Size threshold, requiring chunked delivery.
- **Normal_Line**: A line whose character length is less than or equal to the Chunk_Size threshold, deliverable as a single string in the existing protocol.
- **Message_Router**: The C# `MessageRouter` service that serializes messages into `MessageEnvelope` JSON and sends them to the frontend via the Photino web message bridge.
- **Content_Area**: The React component that renders file lines using a sliding-window virtual scroll.
- **Horizontal_Viewport**: The visible column range within a Large_Line, analogous to the existing vertical viewport for lines. Only the characters within this range are rendered in the DOM.
- **Column_Window**: The range of character columns currently loaded in memory for a given Large_Line on the frontend.
- **Photino_Host_Service**: The C# service (`PhotinoHostService`) that orchestrates file operations and sends messages to the UI via the Message_Router.
- **Compressed_Line_Index**: The existing `CompressedLineIndex` that stores byte offsets of line starts.
- **Interop_Service**: The TypeScript `InteropService` that receives messages from the backend and dispatches them to registered callbacks.

## Requirements

### Requirement 1: Chunked Line Reading on the Backend

**User Story:** As a user, I want to open files containing lines larger than 100 MB without the application crashing, so that I can inspect any file regardless of line length.

#### Acceptance Criteria

1. WHEN `ReadLinesAsync` encounters a line whose byte length exceeds the Chunk_Size threshold, THE File_Service SHALL read only the requested chunk of that line (identified by byte offset and length) rather than loading the entire line into memory.
2. THE File_Service SHALL expose a method to read a specific column range of a specific line, accepting line number, start column, and column count as parameters.
3. THE File_Service SHALL allocate no more than Chunk_Size characters of heap memory per chunk read operation, regardless of the total line length.
4. WHEN `ReadLinesAsync` is called for a batch of lines that includes Large_Lines, THE File_Service SHALL return a placeholder or truncated preview for each Large_Line (e.g., the first Chunk_Size characters) along with metadata indicating the line's total length.

### Requirement 2: Line Length Tracking During File Scan

**User Story:** As a developer, I want the file scanning phase to record each line's byte length, so that the backend can determine whether a line is large without re-reading the file.

#### Acceptance Criteria

1. WHILE the Scanning_Phase builds the Compressed_Line_Index, THE File_Service SHALL compute and store the byte length of each line.
2. THE Line_Length_Index SHALL store line lengths using a memory-efficient representation (e.g., only storing lengths for lines exceeding a threshold, or using delta/compressed encoding).
3. WHEN a line's byte length is requested, THE File_Service SHALL return the stored length in O(1) time without file I/O.
4. THE Line_Length_Index SHALL support the same concurrent access guarantees as the Compressed_Line_Index (thread-safe reads during ongoing writes).

### Requirement 3: Chunked Line Content Message Protocol

**User Story:** As a developer, I want the message protocol to support delivering line content in chunks, so that no single JSON message exceeds a safe size for the Photino message bridge.

#### Acceptance Criteria

1. THE Message_Router SHALL send Large_Line content as a `LineChunkResponse` message containing: line number, start column, chunk text, total line length, and a flag indicating whether more chunks follow.
2. WHEN a `LinesResponse` includes a Large_Line, THE response SHALL include the line's total character length and the first chunk of content (up to Chunk_Size characters), rather than the full line text.
3. THE frontend SHALL be able to request additional chunks of a Large_Line by sending a `RequestLineChunk` message specifying line number, start column, and column count.
4. THE Photino_Host_Service SHALL ensure no single JSON message payload exceeds 4 MB of serialized text to stay within safe message bridge limits.

### Requirement 4: Frontend Horizontal Virtualization for Large Lines

**User Story:** As a user, I want to scroll horizontally through a very long line without the browser freezing, so that I can read any part of a large line smoothly.

#### Acceptance Criteria

1. WHEN a Large_Line is rendered, THE Content_Area SHALL render only the characters within the Horizontal_Viewport (visible column range) in the DOM.
2. WHEN the user scrolls horizontally within a Large_Line, THE Content_Area SHALL update the Horizontal_Viewport and request additional chunks from the backend if the new viewport extends beyond the Column_Window.
3. THE Content_Area SHALL maintain a Column_Window buffer larger than the Horizontal_Viewport (analogous to the vertical WINDOW_SIZE) to allow smooth scrolling without visible blank regions.
4. WHILE horizontal scrolling is in progress, THE Content_Area SHALL display a placeholder (e.g., monospace-width empty space) for columns not yet loaded, replacing them with actual content when chunks arrive.
5. WHEN word-wrap is disabled, THE Content_Area SHALL set the horizontal scroll range to the maximum character length across all lines currently in the vertical buffer, so that the user can scroll to any column of the longest line regardless of shorter lines in view.
6. WHEN the vertical buffer contains a mix of Normal_Lines and Large_Lines, THE Content_Area SHALL only request chunks for Large_Lines whose content intersects the current Horizontal_Viewport; Normal_Lines that are shorter than the current horizontal scroll position SHALL render as empty space beyond their end.

### Requirement 5: Horizontal Scrollbar Behavior

**User Story:** As a user, I want the horizontal scrollbar to reflect the longest line in view, so that I can navigate to any column position even when most visible lines are short.

#### Acceptance Criteria

1. WHEN word-wrap is disabled, THE Content_Area SHALL display a horizontal scrollbar whose range equals the maximum character length across all lines in the current vertical buffer.
2. THE horizontal scrollbar SHALL allow the user to jump to an arbitrary column position up to the length of the longest buffered line.
3. WHEN the user drags the horizontal scrollbar thumb to the midpoint, THE Content_Area SHALL scroll to approximately column (max_line_length / 2) and request chunks for any Large_Lines that intersect that column range.
4. WHEN the user scrolls horizontally past the end of a Normal_Line, THE Content_Area SHALL render empty space for that line while continuing to show content for Large_Lines that extend to that column.
5. WHEN the vertical buffer changes (lines scroll in/out), THE Content_Area SHALL recalculate the horizontal scroll range based on the new set of buffered lines' lengths.

### Requirement 6: Memory Bounds for Large Line Operations

**User Story:** As a developer, I want guaranteed memory bounds when handling large lines, so that the application remains stable regardless of line length.

#### Acceptance Criteria

1. THE File_Service SHALL hold at most one Chunk_Size buffer in memory per concurrent chunk read operation.
2. THE frontend SHALL hold at most Column_Window characters in memory per Large_Line that is within the vertical viewport.
3. WHEN a Large_Line scrolls out of the vertical viewport, THE Content_Area SHALL release the cached Column_Window data for that line.
4. THE total memory consumed by Large_Line chunk caches on the frontend SHALL NOT exceed a configurable maximum (e.g., 10 MB across all cached line chunks).

### Requirement 7: Normal Line Backward Compatibility

**User Story:** As a user, I want files with normal-length lines to continue working exactly as before, so that this feature does not regress existing behavior.

#### Acceptance Criteria

1. WHEN all lines in a requested range are Normal_Lines, THE File_Service SHALL return them using the existing `LinesResponse` format with full line strings, with no behavioral change.
2. WHEN all lines are Normal_Lines, THE Content_Area SHALL render them using the existing `<pre>` element approach with no horizontal virtualization overhead.
3. THE Chunk_Size threshold SHALL be large enough (at least 64 KB characters) that typical source code lines are always treated as Normal_Lines.

### Requirement 8: Seek-Based Chunk Reading

**User Story:** As a developer, I want chunk reads to use file seeking rather than sequential reading, so that reading column 50,000,000 of a line does not require reading the preceding 50 million characters.

#### Acceptance Criteria

1. WHEN a chunk is requested at a specific column offset within a line, THE File_Service SHALL seek to the corresponding byte position in the file and read only the requested range.
2. THE File_Service SHALL compute the byte position for a given column offset using the encoding's bytes-per-character ratio (exact for fixed-width encodings, sequential scan for variable-width encodings like UTF-8).
3. IF the file uses a variable-width encoding (UTF-8), THEN THE File_Service SHALL use an efficient strategy to locate the byte position for a given character offset (e.g., scanning forward from the line start offset with a buffered reader, or maintaining a sparse column-to-byte offset map for very large lines).

### Requirement 9: Copy and Selection Support for Large Lines

**User Story:** As a user, I want to select and copy text from a large line, so that I can extract data from any part of the line.

#### Acceptance Criteria

1. WHEN the user selects a range of text within a Large_Line, THE Content_Area SHALL ensure the selected range is fully loaded (requesting additional chunks if necessary) before copying to the clipboard.
2. IF the selected range spans more than Chunk_Size characters, THEN THE Content_Area SHALL assemble the full selection from multiple chunks before placing it on the clipboard.
3. WHEN a copy operation requires loading additional chunks, THE Content_Area SHALL show a brief loading indicator until the data is ready.

### Requirement 10: Search Within Large Lines

**User Story:** As a user, I want search/find to work within large lines, so that I can locate content regardless of line length.

#### Acceptance Criteria

1. WHEN a search is performed, THE File_Service SHALL search within Large_Lines by reading them in chunks from disk rather than loading the entire line into memory.
2. WHEN a search match is found within a Large_Line, THE Content_Area SHALL scroll horizontally to the matching column position and highlight the match.
3. IF a search match spans a chunk boundary, THEN THE File_Service SHALL detect the match by overlapping chunk reads by at least the maximum search term length.

