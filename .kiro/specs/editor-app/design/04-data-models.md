# Data Models

## Message Schemas

All messages between backend and frontend use JSON serialization with a common envelope structure:

```typescript
interface MessageEnvelope {
  type: string;
  payload?: any;
  timestamp: string; // ISO 8601
}
```

### Frontend → Backend Messages

**OpenFileRequest**:
```json
{
  "type": "OpenFileRequest",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

**RequestLinesMessage**:
```json
{
  "type": "RequestLinesMessage",
  "payload": {
    "startLine": 500,
    "lineCount": 50
  },
  "timestamp": "2024-01-15T10:30:02Z"
}
```

### Backend → Frontend Messages

**FileOpenedResponse** (sent after initial file scan — metadata only, no file content):
```json
{
  "type": "FileOpenedResponse",
  "payload": {
    "fileName": "file.txt",
    "totalLines": 125000,
    "fileSizeBytes": 4194304,
    "encoding": "UTF-8"
  },
  "timestamp": "2024-01-15T10:30:01Z"
}
```

**LinesResponse** (sent in response to RequestLinesMessage):
```json
{
  "type": "LinesResponse",
  "payload": {
    "startLine": 500,
    "lines": ["line 501 content", "line 502 content", "..."],
    "totalLines": 125000
  },
  "timestamp": "2024-01-15T10:30:02Z"
}
```

**ErrorResponse**:
```json
{
  "type": "ErrorResponse",
  "payload": {
    "errorCode": "FILE_NOT_FOUND",
    "message": "The selected file could not be found.",
    "details": "/path/to/missing.txt"
  },
  "timestamp": "2024-01-15T10:30:01Z"
}
```

**Error Codes**:
- `FILE_NOT_FOUND`: File does not exist
- `PERMISSION_DENIED`: Insufficient permissions to read file
- `INTEROP_FAILURE`: Communication failure between backend and frontend
- `UNKNOWN_ERROR`: Unexpected error occurred

## Frontend State Models

**FileMeta**:
```typescript
interface FileMeta {
  fileName: string;
  totalLines: number;
  fileSizeBytes: number;
  encoding: string;
}
```

**LinesResponsePayload**:
```typescript
interface LinesResponsePayload {
  startLine: number;
  lines: string[];
  totalLines: number;
}
```

**ErrorInfo**:
```typescript
interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}
```
