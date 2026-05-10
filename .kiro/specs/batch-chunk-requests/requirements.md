# Requirements Document

## Introduction

Replace the individual `RequestLineChunk`/`LineChunkResponse` message protocol with a batch variant (`RequestLineChunkBatch`/`LineChunkBatchResponse`) that combines multiple chunk requests into a single round-trip. The frontend already collects pending chunk requests into a `Map<lineNumber, startColumn>` during a 150ms debounce window but currently sends each as a separate message. This feature sends them as one batch message and receives one batch response, reducing interop round-trips during scroll.

## Glossary

- **Batch_Message_System**: The end-to-end pipeline comprising the frontend InteropService, the Photino web-message bridge, the MessageRouter, and the PhotinoHostService handler that processes batched chunk requests.
- **Frontend**: The React/TypeScript layer (`ContentArea.tsx`, `InteropService.ts`) running inside the Photino webview.
- **Backend**: The C# layer (`PhotinoHostService.cs`, `MessageRouter.cs`, `FileService.cs`) running in the .NET host process.
- **Chunk**: A substring of a large line identified by line number, start column, and column count.
- **Batch**: A collection of one or more chunk requests sent/received as a single message envelope.
- **Debounce_Window**: The existing 150ms timer that accumulates chunk requests before dispatch.

## Requirements

### Requirement 1: Batch Request Message

**User Story:** As a developer, I want the frontend to send all accumulated chunk requests in a single message, so that interop round-trips are minimized during scroll.

#### Acceptance Criteria

1. WHEN the Debounce_Window expires with one or more pending chunk requests, THE Frontend SHALL send exactly one `RequestLineChunkBatch` message containing all pending requests.
2. THE Frontend SHALL include each pending request as an item with `lineNumber`, `startColumn`, and `columnCount` fields in the batch payload's `items` array.
3. THE Frontend SHALL clear the pending requests map after sending the batch message.
4. THE Frontend SHALL NOT send individual `RequestLineChunk` messages for scroll-triggered chunk loading.

### Requirement 2: Batch Response Message

**User Story:** As a developer, I want the backend to return all requested chunks in a single response message, so that the frontend processes them in one callback.

#### Acceptance Criteria

1. WHEN the Backend receives a `RequestLineChunkBatch` message, THE Backend SHALL read all requested chunks and return a single `LineChunkBatchResponse` message.
2. THE Backend SHALL include one result item per request item in the response, each containing `lineNumber`, `startColumn`, `text`, `totalLineChars`, and `hasMore` fields.
3. THE Backend SHALL preserve the order of items in the response to match the order in the request.

### Requirement 3: All-or-Nothing Error Handling

**User Story:** As a developer, I want the entire batch to fail if any single chunk read fails, so that error handling remains simple and predictable.

#### Acceptance Criteria

1. IF any chunk read within a batch fails, THEN THE Backend SHALL return a single `ErrorResponse` message instead of a partial `LineChunkBatchResponse`.
2. IF any chunk read within a batch fails, THEN THE Backend SHALL NOT return partial results for the chunks that succeeded.
3. THE Backend SHALL include the error code, message, and details of the first failure encountered in the `ErrorResponse`.

### Requirement 4: MessageRouter Integration

**User Story:** As a developer, I want the batch message to integrate with the existing MessageRouter dispatch pattern, so that no architectural changes are needed.

#### Acceptance Criteria

1. THE Backend SHALL register a handler for `RequestLineChunkBatch` using the existing `RegisterHandler<T>` pattern in MessageRouter.
2. THE Backend SHALL dispatch `RequestLineChunkBatch` messages by type name, consistent with all other message types.
3. THE Backend SHALL serialize `LineChunkBatchResponse` using the existing `SendToUIAsync` envelope mechanism.

### Requirement 5: Copy Handler Compatibility

**User Story:** As a developer, I want the copy handler to use the batch protocol with a single-item batch, so that the old individual message type can be removed entirely.

#### Acceptance Criteria

1. WHEN the copy handler needs to load full line content for one or more large lines, THE Frontend SHALL send a `RequestLineChunkBatch` message containing one item per line.
2. THE Frontend SHALL NOT use the individual `RequestLineChunk` message type for copy operations.
3. WHEN a search-jump requires a chunk load, THE Frontend SHALL send a `RequestLineChunkBatch` message with a single item.

### Requirement 6: Removal of Individual Chunk Protocol

**User Story:** As a developer, I want the old single-request chunk messages removed, so that there is only one code path to maintain.

#### Acceptance Criteria

1. THE Backend SHALL NOT register a handler for the `RequestLineChunk` message type.
2. THE Frontend SHALL NOT define or send `RequestLineChunk` envelopes.
3. THE Frontend SHALL NOT register a callback for `LineChunkResponse` messages.
4. THE Backend SHALL NOT define or send `LineChunkResponse` messages.

### Requirement 7: Debounce Window Unchanged

**User Story:** As a developer, I want the debounce timing to remain at 150ms, so that scroll responsiveness is unaffected.

#### Acceptance Criteria

1. THE Frontend SHALL use a 150ms debounce window for accumulating chunk requests before sending a batch.
2. THE Frontend SHALL reset the debounce timer each time a new chunk request is added to the pending map.

### Requirement 8: Frontend Response Handling

**User Story:** As a developer, I want the frontend to process batch responses and update the chunk cache for each item, so that rendered lines display correctly.

#### Acceptance Criteria

1. WHEN the Frontend receives a `LineChunkBatchResponse`, THE Frontend SHALL update the chunk cache for each item in the response.
2. WHEN the Frontend receives a `LineChunkBatchResponse`, THE Frontend SHALL trigger a re-render so that lines using placeholder text are replaced with actual content.
3. IF the Frontend receives an `ErrorResponse` in reply to a batch request, THEN THE Frontend SHALL handle the error using the existing error callback mechanism.
