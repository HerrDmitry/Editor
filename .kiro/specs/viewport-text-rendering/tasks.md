# Implementation Plan: Viewport Text Rendering

## Overview

Replace the sliding-window virtual scroll (ContentArea.tsx) with a true viewport-based rendering system. Backend delivers exact rectangular character slices via new ViewportService; frontend renders with fixed-width font metrics, oversized buffer, and smooth scrolling. Two modes: wrap (virtual line splitting) and no-wrap (horizontal scrolling).

## Tasks

- [x] 1. Backend: MaxLineLength tracking in CacheEntry + file scan
  - [x] 1.1 Extend CacheEntry record with MaxLineLength field
    - Add `int MaxLineLength` to `CacheEntry` record in `FileService.cs`
    - Update all CacheEntry construction sites to include MaxLineLength
    - _Requirements: 10.1_

  - [x] 1.2 Compute max line length during OpenFileAsync scan
    - Track max byte-length delta between consecutive line offsets during the scan loop
    - For UTF-8: byte length ≈ char length (conservative overestimate)
    - Store result in CacheEntry.MaxLineLength
    - Ensure ≤10% overhead added to scan time
    - _Requirements: 10.1, 10.4, 10.5_

  - [x] 1.3 Update RefreshFileAsync to recompute MaxLineLength
    - MaxLineLength recalculated on file refresh via OpenFileAsync re-scan
    - _Requirements: 10.3_

  - [x] 1.4 Extend FileOpenedResponse with maxLineLength field
    - Add `[JsonPropertyName("maxLineLength")] public int MaxLineLength` to FileOpenedResponse
    - Populate in PhotinoHostService when sending FileOpenedResponse
    - _Requirements: 10.2, 13.4_

  - [x] 1.5 Write property test: max line length correctness (Property 9)
    - **Property 9: Maximum line length correctness**
    - Generate random file content, verify stored maxLineLength ≥ actual max char length
    - **Validates: Requirements 4.5, 10.1, 10.5**

- [x] 2. Backend: ViewportService interface + implementation
  - [x] 2.1 Create IViewportService interface
    - Define in `src/EditorApp/Services/IViewportService.cs`
    - Methods: GetViewportAsync, GetVirtualLineCount, GetMaxLineLength
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 2.2 Implement ViewportService core (no-wrap mode)
    - Create `src/EditorApp/Services/ViewportService.cs`
    - Inject IFileService dependency
    - Implement GetViewportAsync for no-wrap: iterate startLine..startLine+lineCount, call ReadLineChunkAsync for each line with (startColumn, columnCount)
    - Return empty string for lines shorter than startColumn
    - Include totalPhysicalLines, per-line char lengths, maxLineLength in response
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 8.1, 8.2, 8.5_

  - [x] 2.3 Write property test: viewport slicing correctness (Property 1)
    - **Property 1: Viewport slicing returns correct rectangular region**
    - Generate random file content + viewport rects, verify correct substrings returned
    - **Validates: Requirements 1.1, 1.4, 1.5**

  - [x] 2.4 Write property test: Unicode viewport read correctness (Property 10)
    - **Property 10: Unicode viewport read correctness**
    - Generate Unicode strings with multibyte/surrogates, verify correct slicing
    - **Validates: Requirements 14.1, 14.3, 14.4, 14.5**

- [x] 3. Backend: RequestViewport/ViewportResponse message models
  - [x] 3.1 Add RequestViewport message class
    - Add to `src/EditorApp/Models/Messages.cs`
    - Fields: StartLine, LineCount, StartColumn, ColumnCount, WrapMode, ViewportColumns
    - Implement IMessage interface
    - _Requirements: 9.1_

  - [x] 3.2 Add ViewportResponse message class
    - Add to `src/EditorApp/Models/Messages.cs`
    - Fields: Lines, StartLine, StartColumn, TotalPhysicalLines, LineLengths, MaxLineLength, TotalVirtualLines, Truncated
    - _Requirements: 9.2_

- [x] 4. Backend: Response size enforcement (≤4MB)
  - [x] 4.1 Implement 4MB payload cap in ViewportService
    - Before building response, estimate payload size: lineCount × columnCount × 2 bytes
    - If exceeds 4,000,000 bytes, reduce lineCount to fit
    - Set `Truncated = true` in response when capped
    - _Requirements: 9.4, 9.5, 8.3_

  - [x] 4.2 Write property test: response size enforcement (Property 8)
    - **Property 8: Response size enforcement with truncation**
    - Generate large viewport requests, verify serialized response ≤ 4MB
    - **Validates: Requirements 8.3, 9.4, 9.5**

- [x] 5. Backend: Wrap mode virtual line count + virtual-to-physical mapping
  - [x] 5.1 Implement GetVirtualLineCount in ViewportService
    - Sum of ceil(max(1, lineCharLength) / columnWidth) across all physical lines
    - Use stored line lengths (GetLineCharLength) — O(N) computation
    - Cache result per column width
    - _Requirements: 11.1, 11.3, 11.5, 3.2_

  - [x] 5.2 Implement virtual-to-physical line resolution in GetViewportAsync (wrap mode)
    - Resolve virtual line offset → (physicalLine, charOffset) using linear scan
    - Read correct character slice for each virtual line in range
    - Include totalVirtualLines in response
    - _Requirements: 3.1, 3.3, 9.3, 11.2_

  - [x] 5.3 Write property test: virtual line count formula (Property 3)
    - **Property 3: Virtual line count formula**
    - Generate random line length arrays + column widths, verify sum formula
    - **Validates: Requirements 3.2, 11.1, 11.5**

  - [x] 5.4 Write property test: virtual-to-physical mapping (Property 4)
    - **Property 4: Virtual-to-physical line mapping**
    - Generate line lengths + virtual offsets, verify resolution correctness
    - **Validates: Requirements 3.1, 3.3, 9.3**

- [x] 6. Backend: PhotinoHostService handler registration for RequestViewport
  - [x] 6.1 Register RequestViewport handler in PhotinoHostService
    - Add IViewportService dependency injection
    - Register handler: `_messageRouter.RegisterHandler<RequestViewport>(HandleRequestViewportAsync)`
    - Implement HandleRequestViewportAsync: call ViewportService.GetViewportAsync, send ViewportResponse
    - Handle errors (file not found, invalid params) with ErrorResponse
    - _Requirements: 9.1, 9.2, 13.2_

  - [x] 6.2 Register IViewportService in DI container
    - Add service registration in Program.cs
    - _Requirements: 9.1_

- [x] 7. Checkpoint - Backend complete
  - Ensure all backend tests pass, ask the user if questions arise.

- [x] 8. Frontend: ViewportRenderer component (replaces ContentArea rendering)
  - [x] 8.1 Create ViewportRenderer React component
    - Create `src/EditorApp/src/ViewportRenderer.tsx`
    - Implement basic structure: container div, content area, scrollbar placeholders
    - Accept props: fileMeta (totalLines, maxLineLength), wrapMode
    - Render lines from internal buffer state
    - _Requirements: 1.1, 13.1_

  - [x] 8.2 Implement viewport request/response messaging
    - Send RequestViewport messages via window.external.sendMessage
    - Handle ViewportResponse messages, update buffer state
    - _Requirements: 9.1, 9.2_

  - [x] 8.3 Integrate ViewportRenderer into App (replace ContentArea for display)
    - Wire ViewportRenderer into existing App component
    - Ensure file open triggers initial viewport request
    - Stop using RequestLines/LinesResponse for content display when viewport active
    - _Requirements: 13.1, 13.5_

- [x] 9. Frontend: Char cell measurement + viewport dimensions auto-detection
  - [x] 9.1 Implement measureCharCell utility
    - Measure single monospace character ('M') once at startup
    - Return { width, height } in pixels
    - Only re-measure on font change, not per render
    - _Requirements: 12.2, 12.5_

  - [x] 9.2 Implement viewport dimensions computation
    - Compute rows = floor(containerHeight / cellHeight), columns = floor(containerWidth / cellWidth)
    - Recompute on window resize (ResizeObserver)
    - Send updated dimensions with each viewport request
    - _Requirements: 12.1, 12.3, 12.4_

  - [x] 9.3 Write property test: viewport dimensions formula (Property 11)
    - **Property 11: Viewport dimensions from pixel measurements**
    - Generate random pixel/cell sizes, verify floor division
    - **Validates: Requirements 12.1**

- [x] 10. Frontend: Scrollbar computation (vertical + horizontal, no DOM measurement)
  - [x] 10.1 Implement vertical scrollbar computation
    - thumbHeight = (rows / totalLines) × trackHeight
    - thumbPosition = (startLine / totalLines) × trackHeight
    - In wrap mode: use totalVirtualLines instead of totalLines
    - No DOM measurement — pure arithmetic from char/line counts
    - _Requirements: 2.1, 2.3, 2.4_

  - [x] 10.2 Implement horizontal scrollbar computation (no-wrap only)
    - thumbWidth = (columns / maxLineLength) × trackWidth
    - thumbPosition = (startColumn / maxLineLength) × trackWidth
    - Hidden in wrap mode
    - _Requirements: 2.2, 3.4, 4.2_

  - [x] 10.3 Write property test: scrollbar position/size computation (Property 2)
    - **Property 2: Scrollbar position and size computation**
    - Generate random (visibleUnits, totalUnits, currentOffset), verify formula
    - **Validates: Requirements 2.1, 2.2, 2.3, 7.1, 7.2**

- [x] 11. Frontend: Oversized buffer management (2× both axes, edge updates, prefetch)
  - [x] 11.1 Implement oversized buffer initialization
    - Request 2× viewport rows and 2× viewport columns on initial load
    - Store buffer with metadata (startLine, startCol, lineCount, colCount)
    - _Requirements: 6.1_

  - [x] 11.2 Implement incremental edge updates (trim + append)
    - On small vertical scroll: trim lines at opposite edge, request new lines at scroll edge
    - On small horizontal scroll: trim columns at opposite side, request new column slice
    - Splice new content into buffer without disrupting scroll position
    - Buffer size remains constant (2× viewport)
    - _Requirements: 6.2, 6.3, 6.4_

  - [x] 11.3 Implement prefetch at 25% threshold
    - When remaining buffer in scroll direction < 25% of total buffer, trigger prefetch
    - No backend request when ≥ 25% remaining (pure frontend rendering)
    - Prefetch invisible to user — no blank frames or loading indicators
    - _Requirements: 6.8, 6.9, 6.10_

  - [x] 11.4 Write property test: buffer update strategy selection (Property 5)
    - **Property 5: Buffer update strategy selection**
    - Generate buffer bounds + targets, verify partial/full decision
    - **Validates: Requirements 6.5, 6.6, 7.3**

  - [x] 11.5 Write property test: incremental edge update correctness (Property 6)
    - **Property 6: Incremental edge update correctness**
    - Generate buffer state + deltas, verify trim/append behavior and constant buffer size
    - **Validates: Requirements 6.2, 6.3, 6.4**

  - [x] 11.6 Write property test: prefetch threshold trigger (Property 7)
    - **Property 7: Prefetch threshold trigger**
    - Generate buffer state + positions, verify trigger decision at 25%
    - **Validates: Requirements 6.8, 6.9, 6.10**

  - [x] 11.7 Write property test: oversized buffer request sizing (Property 12)
    - **Property 12: Oversized buffer request sizing**
    - Generate viewport dimensions, verify 2× request
    - **Validates: Requirements 6.1**

- [x] 12. Frontend: Smooth scrolling (sub-pixel offsets, CSS transform)
  - [x] 12.1 Implement sub-pixel scroll state management
    - Track subPixelY and subPixelX in scroll state
    - On wheel event: update sub-pixel offset by small increment
    - When offset crosses cell boundary: update logical line/column offset
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 12.2 Implement CSS transform rendering
    - Apply `transform: translate(subPixelX, subPixelY)` to content container
    - GPU-composited — no layout recalculation
    - _Requirements: 5.4_

  - [x] 12.3 Implement pre-fetch on scroll direction
    - Pre-fetch content one viewport height ahead of scroll direction
    - Prevent blank flashes during fast scrolling
    - _Requirements: 5.5_

- [x] 13. Frontend: Scrollbar thumb drag handling (partial vs full buffer replacement)
  - [x] 13.1 Implement vertical thumb drag → target line computation
    - targetLine = floor(thumbFraction × totalLines)
    - Debounce drag requests (16ms) to avoid flooding backend
    - _Requirements: 7.1, 7.4_

  - [x] 13.2 Implement horizontal thumb drag → target column computation (no-wrap)
    - targetColumn = floor(thumbFraction × maxLineLength)
    - _Requirements: 7.2_

  - [x] 13.3 Implement partial vs full buffer replacement on jump
    - If target within current buffer: partial edge update (trim + append)
    - If target outside buffer: request full 2× frame centered on target, replace buffer
    - No intermediate blank frames
    - _Requirements: 6.5, 6.6, 7.3, 7.5_

- [x] 14. Checkpoint - Frontend complete
  - Ensure all frontend tests pass, ask the user if questions arise.

- [x] 15. Integration: wire up message handlers, backward compatibility
  - [x] 15.1 Ensure existing FileService primitives unchanged
    - Verify ReadLineChunkAsync, CompressedLineIndex, GetLineCharLength remain unchanged
    - ViewportService composes from these primitives only
    - _Requirements: 13.2_

  - [x] 15.2 Ensure legacy RequestLines/LinesResponse still works for non-viewport ops
    - Large-line-support features (search, copy) continue using chunk protocol
    - Frontend does NOT use RequestLines for content display when viewport active
    - _Requirements: 13.3, 13.5_

  - [x] 15.3 Wire ViewportRenderer to file open flow
    - On FileOpenedResponse: initialize ViewportRenderer with totalLines + maxLineLength
    - Request initial viewport (startLine=0, startCol=0, 2× dimensions)
    - _Requirements: 13.1_

  - [x] 15.4 Write integration tests
    - Full round-trip: open file → RequestViewport → verify content matches
    - Wrap mode: request virtual line → verify correct physical segment
    - File refresh: verify maxLineLength updates
    - _Requirements: 13.1, 13.2, 13.3_

- [x] 16. Final checkpoint - All tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- Backend uses C# with xUnit + FsCheck; frontend uses TypeScript with vitest + fast-check
- ViewportService composes from existing FileService primitives — no changes to FileService internals
