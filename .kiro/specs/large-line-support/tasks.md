# Implementation Plan: Large Line Support

## Overview

Enable the editor to handle files with arbitrarily long lines (100MB+) by introducing chunked line reading on the backend, a chunked message protocol, and horizontal virtualization on the frontend. Implementation proceeds bottom-up: backend data model/methods → message protocol → PhotinoHostService handler → frontend horizontal virtualization.

## Tasks

- [x] 1. Backend: Add constants and data model for chunked reading
  - [x] 1.1 Add `ChunkSizeChars` and `MaxMessagePayloadBytes` constants to `FileService.cs`
    - Add `public const int ChunkSizeChars = 65_536;`
    - Add `public const int MaxMessagePayloadBytes = 4_000_000;`
    - _Requirements: 1.3, 3.4, 7.3_
  - [x] 1.2 Add `LineChunkResult` record to `Models/FileModels.cs`
    - `public record LineChunkResult(int LineNumber, int StartColumn, string Text, int TotalLineChars, bool HasMore);`
    - _Requirements: 1.2, 3.1_
  - [x] 1.3 Add `GetLineCharLength` method to `IFileService` and implement in `FileService`
    - Add `int GetLineCharLength(string filePath, int lineNumber);` to interface
    - Implement using CompressedLineIndex offset derivation: `offset[n+1] - offset[n]` for byte length, convert to char length
    - Add `_charLengthCache` ConcurrentDictionary for multibyte lines
    - _Requirements: 2.1, 2.3_
  - [x] 1.4 Add `ReadLineChunkAsync` method to `IFileService` and implement in `FileService`
    - Add `Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default);` to interface
    - Implement seek-based reading: seek to line start offset, scan forward `startColumn` chars, read `columnCount` chars (capped at ChunkSizeChars)
    - Trim at newline if end-of-line reached
    - _Requirements: 1.2, 1.3, 6.1, 8.1, 8.2, 8.3_

- [x] 2. Backend: Modify ReadLinesAsync for large line detection
  - [x] 2.1 Add helper `GetLineByteLength` to `FileService`
    - Compute byte length from CompressedLineIndex offsets: `offset[n+1] - offset[n]` (or `fileSize - offset[n]` for last line)
    - _Requirements: 2.1_
  - [x] 2.2 Modify `ReadLinesAsync` to detect large lines and return truncated content + lineLengths
    - Before reading each line, check byte length via index
    - If `byteLength > ChunkSizeChars * 2`, use `ReadLineChunkAsync` for first chunk instead of `ReadLineAsync`
    - Build `int[] lineLengths` array with actual char lengths for all lines
    - Set `lineLengths` to null when all lines are normal (backward compat)
    - _Requirements: 1.1, 1.4, 7.1_
  - [ ]* 2.3 Write property test: Chunk read returns at most ChunkSizeChars characters (P1)
    - **Property 1: Chunk read returns at most ChunkSizeChars characters**
    - Generate random large lines (64KB–10MB chars). Request chunks with random columnCount (including values > ChunkSizeChars). Verify `result.Text.Length ≤ ChunkSizeChars`.
    - **Validates: Requirements 1.3, 6.1**
  - [ ]* 2.4 Write property test: ReadLinesAsync truncates large lines and provides length metadata (P2)
    - **Property 2: ReadLinesAsync truncates large lines and provides length metadata**
    - Generate files with random mix of normal/large lines. Call ReadLinesAsync. Verify truncation and lineLengths correctness.
    - **Validates: Requirements 1.4, 3.2**
  - [ ]* 2.5 Write property test: ReadLineChunkAsync returns correct substring (P3)
    - **Property 3: ReadLineChunkAsync returns correct substring**
    - Generate files with known content. Request chunks at random offsets. Verify text matches expected substring.
    - **Validates: Requirements 8.1, 8.2**
  - [ ]* 2.6 Write property test: Line byte length derived from CompressedLineIndex equals actual (P4)
    - **Property 4: Line byte length derived from CompressedLineIndex equals actual**
    - Generate random file content with mixed line endings. Open file. Verify derived byte lengths match actual.
    - **Validates: Requirements 2.1**
  - [ ]* 2.7 Write property test: All-normal files use existing LinesResponse with no chunking overhead (P7)
    - **Property 7: All-normal files use existing LinesResponse with no chunking overhead**
    - Generate files with all lines < ChunkSizeChars. Call ReadLinesAsync. Verify full strings returned, lineLengths null.
    - **Validates: Requirements 7.1**

- [x] 3. Checkpoint — Backend chunked reading
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Message protocol: New message types
  - [x] 4.1 Add `RequestLineChunk` message class to `Models/Messages.cs`
    - Properties: `LineNumber` (int), `StartColumn` (int), `ColumnCount` (int) with JsonPropertyName attributes
    - Implements `IMessage`
    - _Requirements: 3.3_
  - [x] 4.2 Add `LineChunkResponse` message class to `Models/Messages.cs`
    - Properties: `LineNumber`, `StartColumn`, `Text`, `TotalLineChars`, `HasMore` with JsonPropertyName attributes
    - Implements `IMessage`
    - _Requirements: 3.1_
  - [x] 4.3 Add `LineLengths` property to existing `LinesResponse` in `Models/Messages.cs`
    - Add `[JsonPropertyName("lineLengths")] public int[]? LineLengths { get; set; }` (nullable for backward compat)
    - _Requirements: 3.2, 7.1_
  - [x] 4.4 Update `HandleRequestLinesAsync` in `PhotinoHostService` to populate `LineLengths` in `LinesResponse`
    - Pass lineLengths from `LinesResult` to `LinesResponse.LineLengths`
    - Update `LinesResult` record to include `int[]? LineLengths` field
    - _Requirements: 1.4, 3.2_
  - [ ]* 4.5 Write property test: Serialized LineChunkResponse size bounded (P11)
    - **Property 11: Serialized LineChunkResponse size bounded**
    - Generate LineChunkResponse with random text up to ChunkSizeChars. Serialize to JSON envelope. Verify byte length < MaxMessagePayloadBytes (4MB).
    - **Validates: Requirements 3.4**

- [x] 5. PhotinoHostService: Register RequestLineChunk handler
  - [x] 5.1 Register `RequestLineChunk` handler in `RegisterMessageHandlers`
    - Add `_messageRouter.RegisterHandler<RequestLineChunk>(HandleRequestLineChunkAsync);`
    - _Requirements: 3.3_
  - [x] 5.2 Implement `HandleRequestLineChunkAsync` in `PhotinoHostService`
    - Validate `_currentFilePath` is set
    - Call `_fileService.ReadLineChunkAsync` with request params
    - Send `LineChunkResponse` via `_messageRouter.SendToUIAsync`
    - Handle errors (no file open, invalid line number, file not found)
    - _Requirements: 3.1, 3.3, 3.4_
  - [ ]* 5.3 Write unit tests for HandleRequestLineChunkAsync
    - Test: no file open → ErrorResponse
    - Test: valid request → correct LineChunkResponse sent
    - Test: invalid line number → ErrorResponse
    - _Requirements: 3.1, 3.3_

- [x] 6. Checkpoint — Message protocol complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Frontend: InteropService new message types
  - [x] 7.1 Add `RequestLineChunk` send method to `InteropService.ts`
    - Add `sendRequestLineChunk(lineNumber: number, startColumn: number, columnCount: number): void`
    - Sends message with type `'RequestLineChunk'` and payload `{ lineNumber, startColumn, columnCount }`
    - _Requirements: 3.3_
  - [x] 7.2 Add `LineChunkResponse` handler registration to `InteropService.ts`
    - Add `onLineChunkResponse(callback: (data: LineChunkPayload) => void): void`
    - Define `LineChunkPayload` interface: `{ lineNumber, startColumn, text, totalLineChars, hasMore }`
    - _Requirements: 3.1_
  - [x] 7.3 Update `LinesResponsePayload` type to include optional `lineLengths`
    - Add `lineLengths?: number[]` to existing lines response payload interface
    - _Requirements: 3.2_

- [x] 8. Frontend: Horizontal virtualization in ContentArea
  - [x] 8.1 Add horizontal virtualization constants and state to `ContentArea.tsx`
    - Constants: `LARGE_LINE_THRESHOLD = 65_536`, `H_VIEWPORT_CHARS = 200`, `H_WINDOW_CHARS = 600`, `MAX_CHUNK_CACHE_CHARS = 5_000_000`
    - State: `hScrollCol` (number), `chunkCacheRef` (Map), `lineLengths` (number[] | null)
    - _Requirements: 4.1, 6.2, 6.4_
  - [x] 8.2 Implement large line rendering with horizontal virtualization
    - For lines where `lineLength > LARGE_LINE_THRESHOLD`: render only chars in `[hScrollCol, hScrollCol + H_VIEWPORT_CHARS]`
    - Cache hit → extract visible portion from cached chunk
    - Cache miss → render placeholder spaces, request chunk via `sendRequestLineChunk`
    - Normal lines shorter than `hScrollCol` → render empty space
    - _Requirements: 4.1, 4.4, 4.6_
  - [x] 8.3 Implement chunk cache with LRU eviction and vertical-scroll cleanup
    - On chunk response: insert into `chunkCacheRef`, evict if total > `MAX_CHUNK_CACHE_CHARS`
    - On vertical buffer change: delete cache entries for lines outside `[linesStartLine, linesStartLine + lines.length)`
    - _Requirements: 6.2, 6.3, 6.4_
  - [x] 8.4 Implement horizontal scrollbar
    - Compute `maxLineLength = Math.max(...lineLengths)` across buffered lines
    - Render horizontal scrollbar when `!wrapLines && maxLineLength > H_VIEWPORT_CHARS`
    - Scrollbar range = `maxLineLength - H_VIEWPORT_CHARS`, value = `hScrollCol`
    - On drag: update `hScrollCol`, trigger chunk requests for large lines intersecting new viewport
    - Recalculate range when vertical buffer changes
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 8.5 Wire `lineLengths` from LinesResponse into ContentArea state
    - When `LinesResponse` arrives with non-null `lineLengths`, store in state
    - Pass to rendering logic for large line detection
    - Normal lines (lineLengths null) → existing rendering path unchanged
    - _Requirements: 7.1, 7.2_
  - [ ]* 8.6 Write property test: Horizontal scroll range equals max line length (P5)
    - **Property 5: Horizontal scroll range equals max line length**
    - Generate random lineLengths arrays. Compute maxLineLength. Verify equals `Math.max(...lineLengths)`.
    - **Validates: Requirements 4.5, 5.1, 5.5**
  - [ ]* 8.7 Write property test: Only large lines trigger chunk requests (P6)
    - **Property 6: Only large lines trigger chunk requests; short lines render empty**
    - Generate mixed lineLengths + random hScrollCol. Verify chunk requests fire only for qualifying large lines.
    - **Validates: Requirements 4.6, 5.4**
  - [ ]* 8.8 Write property test: Chunk cache evicts entries for lines outside vertical buffer (P8)
    - **Property 8: Chunk cache evicts entries for lines outside vertical buffer**
    - Generate sequence of (linesStartLine, lineCount) changes. Verify cache only contains in-range entries after each change.
    - **Validates: Requirements 6.3**
  - [ ]* 8.9 Write property test: Total chunk cache size bounded (P9)
    - **Property 9: Total chunk cache size bounded**
    - Generate sequence of chunk arrivals with random sizes. Verify total cache chars ≤ MAX_CHUNK_CACHE_CHARS after each insertion.
    - **Validates: Requirements 6.4**

- [x] 9. Checkpoint — Horizontal virtualization complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Frontend: Copy and selection support for large lines
  - [x] 10.1 Implement selection-aware chunk loading for copy
    - On copy: detect if selection spans beyond cached range
    - If so, request missing chunks, show loading indicator
    - Assemble full selection text from multiple chunks before clipboard write
    - Timeout after 10s → show error
    - _Requirements: 9.1, 9.2, 9.3_
  - [ ]* 10.2 Write unit tests for copy with large line selection
    - Test: selection within cache → immediate copy
    - Test: selection beyond cache → loading indicator shown, chunks requested
    - _Requirements: 9.1, 9.3_

- [x] 11. Backend: Chunked search within large lines
  - [x] 11.1 Implement chunked search in `FileService`
    - Read large lines in chunks from disk for search (not loading entire line)
    - Overlap consecutive chunk reads by at least max search term length to detect boundary matches
    - Return match positions as column offsets
    - _Requirements: 10.1, 10.3_
  - [x] 11.2 Frontend: scroll to search match in large line
    - On search match in large line: set `hScrollCol` to match column, request chunk if not cached, highlight match
    - _Requirements: 10.2_
  - [ ]* 11.3 Write property test: Search detects matches at chunk boundaries via overlap (P10)
    - **Property 10: Search detects matches at chunk boundaries via overlap**
    - Generate large lines with search terms placed at chunk boundaries. Run chunked search. Verify all found.
    - **Validates: Requirements 10.3**
  - [ ]* 11.4 Write property test: Chunked search finds all occurrences in large lines (P12)
    - **Property 12: Chunked search finds all occurrences in large lines**
    - Generate large lines with known embedded terms. Run chunked search. Verify all N occurrences found with correct offsets.
    - **Validates: Requirements 10.1**

- [x] 12. Final checkpoint — All features integrated
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (FsCheck for C#, fast-check for frontend)
- Unit tests validate specific examples and edge cases
- Backend tasks (1–3) have no frontend dependency and can be validated independently
- Message protocol (4–6) bridges backend and frontend
- Frontend tasks (7–9) depend on backend + protocol being complete
