# Design Document: Batch Chunk Requests

## Overview

Replace the individual `RequestLineChunk`/`LineChunkResponse` message pair with a batch variant (`RequestLineChunkBatch`/`LineChunkBatchResponse`). The frontend already accumulates pending chunk requests in a `Map<lineNumber, startColumn>` during a 150ms debounce window — this design sends them as one batch message and receives one batch response, reducing interop round-trips.

The change is all-or-nothing: the old single-request types are removed entirely. Copy and search-jump operations also use the batch protocol (with single-item batches when needed).

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Frontend (TypeScript)                                       │
│                                                              │
│  ContentArea.tsx                                             │
│    requestChunk() → pendingChunkRequests Map                 │
│    150ms debounce timer expires →                            │
│      InteropService.sendRequestLineChunkBatch(items[])       │
│                                                              │
│  InteropService.ts                                           │
│    sendRequestLineChunkBatch(items)                          │
│    onLineChunkBatchResponse(callback)                        │
│    ─── removed: sendRequestLineChunk ───                     │
│    ─── removed: onLineChunkResponse ───                      │
└──────────────────────────┬──────────────────────────────────┘
                           │ window.external.sendMessage / receiveMessage
┌──────────────────────────▼──────────────────────────────────┐
│  Backend (C#)                                                │
│                                                              │
│  MessageRouter                                               │
│    RegisterHandler<RequestLineChunkBatch>(...)                │
│    ─── removed: RegisterHandler<RequestLineChunk> ───        │
│                                                              │
│  PhotinoHostService                                          │
│    HandleRequestLineChunkBatchAsync(request)                 │
│      for each item: FileService.ReadLineChunkAsync(...)      │
│      if any fails → SendToUIAsync(ErrorResponse)             │
│      else → SendToUIAsync(LineChunkBatchResponse)            │
│    ─── removed: HandleRequestLineChunkAsync ───              │
│                                                              │
│  Messages.cs                                                 │
│    + RequestLineChunkBatch { Items: ChunkRequestItem[] }     │
│    + LineChunkBatchResponse { Items: ChunkResponseItem[] }   │
│    ─── removed: RequestLineChunk ───                         │
│    ─── removed: LineChunkResponse ───                        │
└─────────────────────────────────────────────────────────────┘
```

## Components

### 1. Message Models (Messages.cs)

**New types:**

```csharp
public class RequestLineChunkBatch : IMessage
{
    [JsonPropertyName("items")]
    public ChunkRequestItem[] Items { get; set; } = Array.Empty<ChunkRequestItem>();
}

public class ChunkRequestItem
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("columnCount")]
    public int ColumnCount { get; set; }
}

public class LineChunkBatchResponse : IMessage
{
    [JsonPropertyName("items")]
    public ChunkResponseItem[] Items { get; set; } = Array.Empty<ChunkResponseItem>();
}

public class ChunkResponseItem
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("totalLineChars")]
    public int TotalLineChars { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}
```

**Removed types:** `RequestLineChunk`, `LineChunkResponse`

### 2. Backend Handler (PhotinoHostService.cs)

```csharp
private async Task HandleRequestLineChunkBatchAsync(RequestLineChunkBatch request)
{
    if (string.IsNullOrEmpty(_currentFilePath))
    {
        await _messageRouter.SendToUIAsync(new ErrorResponse
        {
            ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
            Message = "No file is currently open."
        });
        return;
    }

    var results = new ChunkResponseItem[request.Items.Length];

    for (int i = 0; i < request.Items.Length; i++)
    {
        var item = request.Items[i];
        try
        {
            var result = await _fileService.ReadLineChunkAsync(
                _currentFilePath, item.LineNumber, item.StartColumn, item.ColumnCount);

            results[i] = new ChunkResponseItem
            {
                LineNumber = result.LineNumber,
                StartColumn = result.StartColumn,
                Text = result.Text,
                TotalLineChars = result.TotalLineChars,
                HasMore = result.HasMore
            };
        }
        catch (Exception ex)
        {
            // All-or-nothing: first failure aborts entire batch
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = ex is ArgumentOutOfRangeException
                    ? Models.ErrorCode.UNKNOWN_ERROR.ToString()
                    : ex is FileNotFoundException
                        ? Models.ErrorCode.FILE_NOT_FOUND.ToString()
                        : Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                Message = "A chunk read failed within the batch.",
                Details = ex.Message
            });
            return;
        }
    }

    await _messageRouter.SendToUIAsync(new LineChunkBatchResponse { Items = results });
}
```

**Registration change in `RegisterMessageHandlers()`:**
- Remove: `_messageRouter.RegisterHandler<RequestLineChunk>(HandleRequestLineChunkAsync);`
- Add: `_messageRouter.RegisterHandler<RequestLineChunkBatch>(HandleRequestLineChunkBatchAsync);`

### 3. InteropService.ts Changes

**New interface method and message types:**

```typescript
interface LineChunkBatchPayload {
  items: Array<{
    lineNumber: number;
    startColumn: number;
    text: string;
    totalLineChars: number;
    hasMore: boolean;
  }>;
}

// In MessageTypes:
RequestLineChunkBatch: 'RequestLineChunkBatch',
LineChunkBatchResponse: 'LineChunkBatchResponse',
// Removed: RequestLineChunk, LineChunkResponse

// New method:
sendRequestLineChunkBatch(items: Array<{ lineNumber: number; startColumn: number; columnCount: number }>): void;

// New callback:
onLineChunkBatchResponse(callback: (data: LineChunkBatchPayload) => void): void;

// Removed:
// sendRequestLineChunk(...)
// onLineChunkResponse(...)
```

**Implementation:**

```typescript
sendRequestLineChunkBatch(items: Array<{ lineNumber: number; startColumn: number; columnCount: number }>): void {
  const envelope: MessageEnvelope = {
    type: MessageTypes.RequestLineChunkBatch,
    payload: { items },
    timestamp: new Date().toISOString(),
  };
  try {
    (window as any).external.sendMessage(JSON.stringify(envelope));
  } catch {
    const interopError: ErrorInfo = {
      errorCode: 'INTEROP_FAILURE',
      message: 'The application is not responding. Please restart.',
    };
    for (const cb of errorCallbacks) { cb(interopError); }
  }
}
```

### 4. ContentArea.tsx Changes

**Debounce send (requestChunk function):**

```typescript
function requestChunk(lineNumber: number, visibleStart: number): void {
  pendingChunkRequestsRef.current.set(lineNumber, visibleStart);

  if (chunkRequestTimerRef.current) {
    clearTimeout(chunkRequestTimerRef.current);
  }
  chunkRequestTimerRef.current = setTimeout(() => {
    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.sendRequestLineChunkBatch !== 'function') return;

    const pending = pendingChunkRequestsRef.current;
    const items: Array<{ lineNumber: number; startColumn: number; columnCount: number }> = [];
    for (const [lineNum, startCol] of pending.entries()) {
      const chunkStart = Math.max(0, startCol - Math.floor((H_WINDOW_CHARS - viewportColumns) / 2));
      items.push({ lineNumber: lineNum, startColumn: chunkStart, columnCount: H_WINDOW_CHARS });
    }
    pending.clear();

    if (items.length > 0) {
      interop.sendRequestLineChunkBatch(items);
    }
  }, 150);
}
```

**Batch response handler (replaces `handleChunkResponse`):**

```typescript
function handleBatchChunkResponse(data: LineChunkBatchPayload) {
  const cache = chunkCacheRef.current;
  for (const item of data.items) {
    const lruOrder = ++lruCounterRef.current;
    const entry: ChunkCacheEntry = { startCol: item.startColumn, text: item.text, lruOrder };
    cache.set(item.lineNumber, entry);

    // Resolve pending copy chunk if waiting
    const resolver = pendingCopyChunksRef.current.get(item.lineNumber);
    if (resolver) {
      resolver(entry);
      pendingCopyChunksRef.current.delete(item.lineNumber);
    }
  }

  // Evict if total chars exceed MAX_CHUNK_CACHE_CHARS
  let totalChars = 0;
  for (const e of cache.values()) { totalChars += e.text.length; }
  if (totalChars > MAX_CHUNK_CACHE_CHARS) {
    const sorted = Array.from(cache.entries()).sort((a, b) => a[1].lruOrder - b[1].lruOrder);
    for (const [lineNum, e] of sorted) {
      if (totalChars <= MAX_CHUNK_CACHE_CHARS) break;
      totalChars -= e.text.length;
      cache.delete(lineNum);
    }
  }

  setChunkVersion(v => v + 1);
}

interop.onLineChunkBatchResponse(handleBatchChunkResponse);
```

**Copy handler change:**

```typescript
// Instead of individual sendRequestLineChunk calls per line:
const items = linesNeedingChunks.map(info => ({
  lineNumber: info.lineNumber,
  startColumn: 0,
  columnCount: info.lineLength,
}));
interop.sendRequestLineChunkBatch(items);
```

## Data Models

| Type | Direction | Fields |
|------|-----------|--------|
| `RequestLineChunkBatch` | Frontend → Backend | `items: ChunkRequestItem[]` |
| `ChunkRequestItem` | (nested) | `lineNumber`, `startColumn`, `columnCount` |
| `LineChunkBatchResponse` | Backend → Frontend | `items: ChunkResponseItem[]` |
| `ChunkResponseItem` | (nested) | `lineNumber`, `startColumn`, `text`, `totalLineChars`, `hasMore` |

## Error Handling

- **All-or-nothing**: If any `ReadLineChunkAsync` call throws within the batch loop, the handler immediately sends a single `ErrorResponse` and returns without sending partial results.
- **Error details**: The `ErrorResponse` contains the error code and message from the first failure encountered.
- **Frontend handling**: The existing `onError` callback mechanism handles `ErrorResponse` messages regardless of which request triggered them — no change needed.

## Interfaces

No new service interfaces are introduced. The existing `IFileService.ReadLineChunkAsync` is called in a loop. The existing `IMessageRouter.RegisterHandler<T>` and `SendToUIAsync<T>` patterns are reused.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Batch message completeness

*For any* set of N pending chunk requests (N ≥ 1) accumulated during a debounce window, when the 150ms timer expires, exactly one `RequestLineChunkBatch` message SHALL be sent containing exactly N items, where each item's `lineNumber`, `startColumn`, and `columnCount` correspond to a pending request.

**Validates: Requirements 1.1, 1.2**

### Property 2: Pending map cleared after batch send

*For any* debounce expiry that triggers a batch send, the pending chunk requests map SHALL be empty immediately after the send call completes.

**Validates: Requirements 1.3**

### Property 3: Backend batch response correctness

*For any* valid `RequestLineChunkBatch` with N items where all chunk reads succeed, the backend SHALL return exactly one `LineChunkBatchResponse` containing exactly N items, where `response.items[i]` corresponds to `request.items[i]` (same `lineNumber` and `startColumn`) and each response item contains `text`, `totalLineChars`, and `hasMore` fields.

**Validates: Requirements 2.1, 2.2, 2.3**

### Property 4: All-or-nothing error semantics

*For any* `RequestLineChunkBatch` where at least one item references an invalid line number (out of range), the backend SHALL return a single `ErrorResponse` and SHALL NOT return a `LineChunkBatchResponse`.

**Validates: Requirements 3.1, 3.2**

### Property 5: Debounce timer reset on new request

*For any* sequence of chunk requests arriving within 150ms of each other, the batch SHALL only be sent 150ms after the last request in the sequence (timer resets on each addition).

**Validates: Requirements 7.2**

### Property 6: Cache update completeness

*For any* `LineChunkBatchResponse` with N items received by the frontend, after processing, the chunk cache SHALL contain an entry for each of the N line numbers with the corresponding `startColumn` and `text` values from the response.

**Validates: Requirements 8.1**

### Property 7: Copy handler batch correctness

*For any* set of K large lines (K ≥ 1) requiring full content for a copy operation, the frontend SHALL send exactly one `RequestLineChunkBatch` message containing K items, each with `startColumn: 0` and `columnCount` equal to the line's total character length.

**Validates: Requirements 5.1**
