# Implementation Plan: Batch Chunk Requests

## Overview

Replace individual `RequestLineChunk`/`LineChunkResponse` message pair with batch variant (`RequestLineChunkBatch`/`LineChunkBatchResponse`). Frontend already accumulates pending requests in a Map during 150ms debounce — this wires them into one batch message/response. Copy and search-jump also migrate to batch protocol. Old single-request types removed entirely.

## Tasks

- [x] 1. Backend: Batch message models
  - [x] 1.1 Add RequestLineChunkBatch and ChunkRequestItem classes to Messages.cs
    - Add `RequestLineChunkBatch : IMessage` with `Items` property (`ChunkRequestItem[]`)
    - Add `ChunkRequestItem` with `LineNumber`, `StartColumn`, `ColumnCount` (all `[JsonPropertyName(...)]`)
    - _Requirements: 2.1, 4.2_

  - [x] 1.2 Add LineChunkBatchResponse and ChunkResponseItem classes to Messages.cs
    - Add `LineChunkBatchResponse : IMessage` with `Items` property (`ChunkResponseItem[]`)
    - Add `ChunkResponseItem` with `LineNumber`, `StartColumn`, `Text`, `TotalLineChars`, `HasMore`
    - _Requirements: 2.2, 4.3_

- [x] 2. Backend: Batch handler in PhotinoHostService
  - [x] 2.1 Implement HandleRequestLineChunkBatchAsync method
    - Loop over `request.Items`, call `_fileService.ReadLineChunkAsync` for each
    - On success: build `ChunkResponseItem[]` preserving request order, send `LineChunkBatchResponse`
    - On any failure: send single `ErrorResponse` with first error details, return immediately
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3_

  - [x] 2.2 Register batch handler in RegisterMessageHandlers
    - Add `_messageRouter.RegisterHandler<RequestLineChunkBatch>(HandleRequestLineChunkBatchAsync)`
    - _Requirements: 4.1, 4.2_

  - [ ]*2.3 Write property test: batch response correctness (Property 3)
    - **Property 3: Backend batch response correctness**
    - Generate N valid chunk requests, verify response has N items in same order with correct fields
    - **Validates: Requirements 2.1, 2.2, 2.3**

  - [ ]*2.4 Write property test: all-or-nothing error semantics (Property 4)
    - **Property 4: All-or-nothing error semantics**
    - Generate batch with at least one invalid line number, verify ErrorResponse returned (no partial LineChunkBatchResponse)
    - **Validates: Requirements 3.1, 3.2**

- [x] 3. Checkpoint - Backend batch handler complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Frontend: InteropService batch methods
  - [x] 4.1 Add sendRequestLineChunkBatch method to InteropService
    - Build envelope with type `RequestLineChunkBatch`, payload `{ items }` 
    - Send via `window.external.sendMessage`
    - On interop failure: invoke error callbacks
    - _Requirements: 1.1, 1.2_

  - [x] 4.2 Add onLineChunkBatchResponse callback registration to InteropService
    - Register callback for `LineChunkBatchResponse` message type
    - Parse payload as `LineChunkBatchPayload` (items array with lineNumber, startColumn, text, totalLineChars, hasMore)
    - _Requirements: 8.1_

  - [x] 4.3 Add MessageTypes entries for batch messages
    - Add `RequestLineChunkBatch` and `LineChunkBatchResponse` to MessageTypes object
    - _Requirements: 4.2_

- [x] 5. Frontend: ContentArea.tsx batch send
  - [x] 5.1 Replace individual chunk send with batch send in requestChunk debounce handler
    - On 150ms timer expiry: collect all entries from `pendingChunkRequestsRef.current`
    - Build items array with `lineNumber`, `startColumn`, `columnCount`
    - Call `interop.sendRequestLineChunkBatch(items)`
    - Clear pending map after send
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 7.1, 7.2_

  - [ ]*5.2 Write property test: batch message completeness (Property 1)
    - **Property 1: Batch message completeness**
    - Generate N pending requests, verify single batch message sent with exactly N items
    - **Validates: Requirements 1.1, 1.2**

  - [ ]*5.3 Write property test: pending map cleared after send (Property 2)
    - **Property 2: Pending map cleared after batch send**
    - Verify map empty after debounce fires
    - **Validates: Requirements 1.3**

  - [ ]*5.4 Write property test: debounce timer reset (Property 5)
    - **Property 5: Debounce timer reset on new request**
    - Generate sequence of requests within 150ms, verify batch only sent 150ms after last
    - **Validates: Requirements 7.2**

- [x] 6. Frontend: Batch response handler
  - [x] 6.1 Implement handleBatchChunkResponse in ContentArea.tsx
    - Iterate response items, update chunkCacheRef for each (lineNumber → { startCol, text, lruOrder })
    - Resolve any pending copy chunk promises
    - Run LRU eviction if total chars exceed MAX_CHUNK_CACHE_CHARS
    - Trigger re-render via `setChunkVersion(v => v + 1)`
    - _Requirements: 8.1, 8.2_

  - [x] 6.2 Register batch response callback in useEffect setup
    - Call `interop.onLineChunkBatchResponse(handleBatchChunkResponse)`
    - Ensure ErrorResponse still handled by existing error callback
    - _Requirements: 8.1, 8.3_

  - [ ]*6.3 Write property test: cache update completeness (Property 6)
    - **Property 6: Cache update completeness**
    - Generate batch response with N items, verify cache contains all N entries with correct values
    - **Validates: Requirements 8.1**

- [x] 7. Frontend: Copy handler migration to batch protocol
  - [x] 7.1 Migrate copy handler to use sendRequestLineChunkBatch
    - For large lines needing full content: build items array with startColumn=0, columnCount=lineLength
    - Send single batch request instead of individual requests per line
    - _Requirements: 5.1, 5.2_

  - [x] 7.2 Migrate search-jump chunk load to batch protocol
    - When search-jump needs a chunk: send single-item batch
    - _Requirements: 5.3_

  - [ ]*7.3 Write property test: copy handler batch correctness (Property 7)
    - **Property 7: Copy handler batch correctness**
    - Generate K large lines for copy, verify single batch with K items, each startColumn=0
    - **Validates: Requirements 5.1**

- [x] 8. Removal of old individual chunk protocol
  - [x] 8.1 Remove old types from backend (Messages.cs + PhotinoHostService)
    - Delete `RequestLineChunk` and `LineChunkResponse` classes from Messages.cs
    - Remove `HandleRequestLineChunkAsync` method from PhotinoHostService
    - Remove `RegisterHandler<RequestLineChunk>(...)` from RegisterMessageHandlers
    - _Requirements: 6.1, 6.4_

  - [x] 8.2 Remove old types from frontend (InteropService + ContentArea)
    - Remove `sendRequestLineChunk` method from InteropService
    - Remove `onLineChunkResponse` callback registration
    - Remove `RequestLineChunk` and `LineChunkResponse` from MessageTypes
    - Remove `handleChunkResponse` function from ContentArea.tsx
    - _Requirements: 6.2, 6.3_

- [x] 9. Final checkpoint - All tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Backend tests: C# with xUnit + FsCheck in `tests/EditorApp.Tests/`
- Frontend tests: TypeScript with vitest + fast-check in `tests/frontend/`
- Build: `dotnet build src/EditorApp` (also compiles TS)
- Run C# tests: `dotnet test tests/EditorApp.Tests`
- Run frontend tests: `npm test` (cwd = `tests/frontend`)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1", "2.2", "4.1", "4.2", "4.3"] },
    { "id": 2, "tasks": ["2.3", "2.4", "5.1"] },
    { "id": 3, "tasks": ["5.2", "5.3", "5.4", "6.1", "6.2"] },
    { "id": 4, "tasks": ["6.3", "7.1", "7.2"] },
    { "id": 5, "tasks": ["7.3", "8.1", "8.2"] }
  ]
}
```
