# Design Document: Editor App

## Overview

The Editor App is a cross-platform desktop application for read-only file viewing, built using a hybrid architecture that combines C# .NET 10 with Photino.Blazor for native window hosting and React/TypeScript for the user interface layer. The application compiles into a single self-contained executable that embeds all resources, eliminating external dependencies.

### Key Design Goals

1. **Cross-Platform Native Experience**: Leverage Photino.Blazor to provide native window management across Windows, macOS, and Linux while maintaining a consistent UI through React
2. **Single Executable Deployment**: Embed all resources (React bundle, static assets) into the compiled binary for zero-installation deployment
3. **Clear Separation of Concerns**: Maintain distinct boundaries between native backend (file I/O, OS integration) and web-based UI (rendering, user interaction)
4. **Streamed Reading for Any File Size**: Use a line offset index and on-demand line reading so files of any size can be opened without loading the entire file into memory
5. **Reliable Interop**: Establish robust message-passing between C# backend and React frontend

### Technology Stack

- **Backend**: C# .NET 10, Photino.NET, Photino.Blazor 4.0.13
- **Frontend**: React 19+ (standalone UMD scripts), TypeScript (compiled by bundled `tsc.js`)
- **Build tooling**: No npm/node_modules. TypeScript is compiled by `node scripts/tsc.js`. React/ReactDOM are standalone JS files in `wwwroot/js/`. Only Node.js runtime is required.
- **Interop**: Photino's JavaScript interop bridge (`window.external.sendMessage` for JS→C#, `window.external.receiveMessage` for C#→JS)
- **Packaging**: .NET publish with `PublishSingleFile=true`, `SelfContained=true`, `GenerateEmbeddedFilesManifest=true`, `StaticWebAssetsEnabled=false`
- **Resource Embedding**: `Microsoft.Extensions.FileProviders.Embedded` with `ManifestEmbeddedFileProvider` — wwwroot files are `EmbeddedResource` items, not `Content`
- **Testing**: FsCheck + xUnit (C# backend)

### Project Structure

All frontend code lives inside the C# project — no separate `frontend/` directory:

```
src/EditorApp/
├── EditorApp.csproj
├── Program.cs
├── App.razor
├── _Imports.razor
├── tsconfig.json              ← TypeScript config
├── scripts/
│   ├── tsc.js                 ← Bundled TypeScript compiler
│   └── lib.*.d.ts             ← TypeScript lib definitions
├── src/                       ← TSX source files
│   ├── App.tsx
│   ├── ContentArea.tsx
│   ├── TitleBar.tsx
│   ├── StatusBar.tsx
│   └── InteropService.ts
├── Models/
├── Services/
└── wwwroot/
    ├── index.html
    └── js/
        ├── react.js           ← Standalone React UMD build
        ├── react-dom.js       ← Standalone ReactDOM UMD build
        ├── App.js             ← Compiled from src/App.tsx by tsc
        ├── ContentArea.js
        ├── TitleBar.js
        ├── StatusBar.js
        └── InteropService.js
```

### Critical Implementation Details

These were discovered during implementation and must be followed:

1. **Photino.Blazor requires `ManifestEmbeddedFileProvider`**: The default `PhysicalFileProvider` looks for a physical `wwwroot/` directory at runtime, which breaks single-file deployment. Register `ManifestEmbeddedFileProvider` in `Program.cs`:
   ```csharp
   builder.Services.AddSingleton<IFileProvider>(
       new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot"));
   ```

2. **wwwroot must be EmbeddedResource, not Content**: In the .csproj:
   ```xml
   <Content Remove="wwwroot\**" />
   <EmbeddedResource Include="wwwroot\**" />
   ```

3. **TypeScript compilation via .csproj target**: No npm needed. The .csproj runs `tsc.js` before build:
   ```xml
   <Target Name="CompileTypeScript" BeforeTargets="BeforeBuild">
     <Exec Command="node scripts/tsc.js -p tsconfig.json" />
   </Target>
   ```

4. **TSX files use `React.createElement`**: Since there's no JSX transform bundler, `tsconfig.json` must set `"jsx": "react"` so TSX compiles to `React.createElement(...)` calls. React is available as a global from the standalone script.

5. **React components exposed via `window`**: Each component file exposes its component to `window` (e.g. `window.renderApp = ...`) so `index.html` can mount them. This is the same pattern used by HyprConfig.

6. **Message receiving uses `window.external.receiveMessage`**: Photino does NOT deliver C#→JS messages via the standard DOM `message` event. The frontend must use:
   ```typescript
   window.external.receiveMessage((msg: string) => { /* handle JSON */ });
   ```

7. **Skip non-JSON messages in MessageRouter**: Blazor sends internal messages (starting with `_`) through the same channel. Guard with:
   ```csharp
   if (trimmed[0] != '{') return; // skip non-JSON
   ```

8. **Single keyboard shortcut handler**: Handle Ctrl+O/Cmd+O in the React `keydown` listener only. Do NOT add a duplicate handler in `index.html` — it causes two native file dialogs.

9. **Single InteropService instance**: Create one instance, register all callbacks on it, and use the same instance for `sendOpenFileRequest()`. Creating multiple instances causes responses to arrive on an instance with no callbacks registered.

## Architecture

### High-Level Architecture

The application follows a **two-tier architecture** with clear separation between native and web layers:

```
┌─────────────────────────────────────────────────────────┐
│                    Operating System                      │
│  (Windows / macOS / Linux)                              │
└────────────────┬────────────────────────────────────────┘
                 │
                 │ Native APIs
                 │
┌────────────────▼────────────────────────────────────────┐
│              C# Backend Layer                            │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Photino.Blazor Host                             │  │
│  │  - Window Management                             │  │
│  │  - Native File Dialogs                           │  │
│  │  - Streamed File Reading (Line Index + Seek)     │  │
│  │  - Message Router                                │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────┬────────────────────────────────────────┘
                 │
                 │ Web Message Interop
                 │ (JSON over Photino bridge)
                 │
┌────────────────▼────────────────────────────────────────┐
│              React UI Layer                              │
│  ┌──────────────────────────────────────────────────┐  │
│  │  React Components                                │  │
│  │  - App Shell (Title Bar, Status Bar)            │  │
│  │  - Virtual-Scrolling File Viewer                │  │
│  │  - Loading States & Error Messages              │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │  State Management                                │  │
│  │  - File Metadata State                          │  │
│  │  - Visible Lines Buffer                         │  │
│  │  - UI State (loading, errors)                   │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### Component Interaction Flow

**File Open Flow:**
1. User triggers open action (keyboard shortcut Ctrl+O or UI button)
2. React UI sends `OpenFileRequest` message to backend
3. Backend displays native file picker dialog
4. User selects file → Backend scans the file (reads through once to build a line offset index mapping each line number to its byte offset, counts total lines, detects encoding)
5. Backend sends `FileOpenedResponse` message to React UI (metadata only: totalLines, fileSize, encoding, fileName — no file content)
6. React UI sets up virtual scrollbar (total height = totalLines × lineHeight)
7. React UI sends `RequestLinesMessage` for the initial visible range (e.g. lines 0–50)
8. Backend seeks to the byte offset for the requested start line and reads the requested number of lines from disk
9. Backend sends `LinesResponse` to React UI (startLine, lines array, totalLines)
10. React UI renders the visible lines in the Content_Area

**Scroll Flow:**
1. User scrolls the Content_Area
2. React UI calculates the new visible line range from scroll position: `startLine = Math.floor(scrollTop / lineHeight)`
3. React UI sends `RequestLinesMessage` with the new startLine and lineCount
4. Backend seeks to the byte offset for startLine using the line index, reads lineCount lines
5. Backend sends `LinesResponse` with the requested lines
6. React UI renders the new lines at the correct position within the virtual scroll container

**Error Handling Flow:**
1. Backend encounters error (permission denied, file not found)
2. Backend sends `ErrorResponse` message to React UI
3. React UI displays error message to user
4. Application returns to previous state

### Deployment Architecture

The application is packaged as a single executable using .NET's single-file publishing with embedded resources:

```
editor-app (single executable)
├── .NET Runtime (embedded, self-contained)
├── C# Application Code
├── Photino.NET Native Libraries
├── Embedded Resources (via ManifestEmbeddedFileProvider)
│   └── wwwroot/
│       ├── index.html (host page with #app and #root divs, blazor.webview.js)
│       └── js/
│           ├── react.js       (standalone React UMD build)
│           ├── react-dom.js   (standalone ReactDOM UMD build)
│           ├── App.js         (compiled from src/App.tsx)
│           ├── ContentArea.js
│           ├── TitleBar.js
│           ├── StatusBar.js
│           └── InteropService.js
```

**Key .csproj settings**:
```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
<StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>
```

**Build pipeline**: The .csproj `CompileTypeScript` target runs `node scripts/tsc.js -p tsconfig.json` before every C# build. TSX files in `src/` are compiled to JS files in `wwwroot/js/`. No npm, no node_modules, no separate frontend project — just `dotnet build`.

## Components and Interfaces

### Backend Components

#### 1. PhotinoHostService

**Responsibility**: Initialize and manage the Photino window, handle application lifecycle.

**Key Methods**:
- `Initialize()`: Create Photino window, configure size/title, register message handlers
- `Run()`: Start the application event loop
- `Shutdown()`: Clean up resources and close window

**Configuration**:
- Window size: 1200x800 (default)
- Window title: "Editor"
- Resizable: true
- Centered on screen

#### 2. FileService

**Responsibility**: Handle all file system operations including scanning files to build a line offset index, reading specific line ranges on demand, and detecting encoding. Files of any size are supported — the entire file is never loaded into memory.

**Key Methods**:
```csharp
public interface IFileService
{
    Task<FileOpenResult> OpenFileDialogAsync();
    Task<FileOpenMetadata> OpenFileAsync(string filePath);
    Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount);
}
```

**Data Structures**:
```csharp
public record FileOpenResult(bool Success, string? FilePath, string? ErrorMessage);

public record FileOpenMetadata(
    string FilePath,
    string FileName,
    int TotalLines,
    long FileSizeBytes,
    string Encoding
);

public record LinesResult(
    int StartLine,
    string[] Lines,
    int TotalLines
);
```

**Line Offset Index**:

When `OpenFileAsync` is called, the service reads through the file once from start to end, recording the byte offset of each line start into a `List<long>` (the line offset index). This scan also counts total lines and detects encoding via BOM detection (falling back to UTF-8). The index is kept in memory — for a file with N lines, this costs approximately N × 8 bytes (e.g. ~80 MB for a 10-million-line file).

When `ReadLinesAsync` is called, the service:
1. Looks up the byte offset for `startLine` in the line offset index
2. Opens a `FileStream` and seeks to that offset
3. Reads `lineCount` lines using a `StreamReader`
4. Returns the lines as a string array

**Encoding Detection**:
- Use BOM detection on the first few bytes of the file
- Fallback to UTF-8 if no BOM is detected
- Report detected encoding in metadata

#### 3. MessageRouter

**Responsibility**: Route messages between C# backend and React frontend, serialize/deserialize JSON payloads.

**Key Methods**:
```csharp
public interface IMessageRouter
{
    void RegisterHandler<TRequest>(Func<TRequest, Task> handler) 
        where TRequest : IMessage;
    Task SendToUIAsync<TMessage>(TMessage message) 
        where TMessage : IMessage;
    void StartListening();
}
```

**Message Flow**:
- Incoming (JS → C#): `window.external.sendMessage(json)` → C# `RegisterWebMessageReceivedHandler` callback → MessageRouter deserializes envelope → Routes to typed handler
- Outgoing (C# → JS): C# handler → MessageRouter serializes `MessageEnvelope` → `SendWebMessage(json)` → JS `window.external.receiveMessage(callback)`
- **Important**: The MessageRouter must skip non-JSON messages (e.g. Blazor framework messages starting with `_`) by checking if the message starts with `{` before attempting deserialization

#### 4. KeyboardShortcutHandler

**Responsibility**: Register and handle keyboard shortcuts at the native level.

**Key Methods**:
```csharp
public interface IKeyboardShortcutHandler
{
    void RegisterShortcut(string key, ModifierKeys modifiers, Action handler);
    void Initialize(PhotinoWindow window);
}
```

**Registered Shortcuts**:
- `Ctrl+O` (Windows/Linux) / `Cmd+O` (macOS): Open file dialog

### Frontend Components

#### 1. App Component

**Responsibility**: Root component managing application state and layout structure.

**State**:
```typescript
interface AppState {
  fileMeta: FileMeta | null;       // metadata from FileOpenedResponse
  lines: string[] | null;          // currently visible lines from LinesResponse
  linesStartLine: number;          // the startLine of the current lines buffer
  isLoading: boolean;              // true during initial file scan
  error: ErrorInfo | null;
  titleBarText: string;
}
```

**Layout Structure**:
```tsx
<div className="app">
  <TitleBar title={titleBarText} />
  <ContentArea 
    fileMeta={fileMeta}
    lines={lines}
    linesStartLine={linesStartLine}
    isLoading={isLoading}
    error={error}
    onRequestLines={handleRequestLines}
  />
  <StatusBar metadata={fileMeta} />
</div>
```

#### 2. TitleBar Component

**Responsibility**: Display application name and current file name.

**Props**:
```typescript
interface TitleBarProps {
  title: string; // "Editor" or "filename.txt - Editor"
}
```

**Rendering Logic**:
- No file open: Display "Editor"
- File open: Display "{fileName} - Editor"

#### 3. ContentArea Component

**Responsibility**: Display file contents with virtual scrolling, line numbers, and loading/error states. Only the lines currently visible in the viewport are rendered.

**Props**:
```typescript
interface ContentAreaProps {
  fileMeta: FileMeta | null;
  lines: string[] | null;
  linesStartLine: number;
  isLoading: boolean;
  error: ErrorInfo | null;
  onRequestLines: (startLine: number, lineCount: number) => void;
}
```

**Rendering Modes**:
- **Empty State**: No file open → Display prompt message "Press Ctrl+O to open a file"
- **Loading State**: File being scanned → Display spinner with "Scanning file..."
- **Error State**: Error occurred → Display error message with icon
- **Content State**: File metadata received → Virtual-scrolling line display

**Virtual Scrolling Implementation**:

The ContentArea uses a virtual scrolling approach to handle files of any size:

1. **Outer container**: A scrollable `div` with `overflow-y: auto`.
2. **Spacer div**: A child `div` whose height is `totalLines × lineHeight` pixels. This creates the full-height scrollbar representing the entire file, even though only a small window of lines is in the DOM.
3. **Visible lines div**: Positioned absolutely (or via `transform: translateY`) at `startLine × lineHeight` pixels from the top of the spacer. Contains only the currently visible lines.
4. **Scroll handler**: On the `scroll` event of the outer container, calculate:
   - `startLine = Math.floor(scrollTop / lineHeight)`
   - `lineCount = Math.ceil(containerHeight / lineHeight) + buffer`
   - If the new range differs from the currently loaded range, call `onRequestLines(startLine, lineCount)`
5. **Line numbers**: Each rendered line displays a line number calculated as `linesStartLine + index + 1` (1-based), not from the array index alone.

```tsx
<div className="content-area" onScroll={handleScroll} style={{ overflowY: 'auto', overflowX: 'auto' }}>
  {/* Spacer creates full-height scrollbar */}
  <div style={{ height: totalLines * lineHeight, position: 'relative' }}>
    {/* Visible lines positioned at correct offset */}
    <div style={{ position: 'absolute', top: linesStartLine * lineHeight }}>
      {lines.map((line, index) => (
        <div key={linesStartLine + index} className="line-row" style={{ height: lineHeight }}>
          <span className="line-number">{linesStartLine + index + 1}</span>
          <pre className="content-line">{line}</pre>
        </div>
      ))}
    </div>
  </div>
</div>
```

**Styling Requirements**:
- Monospaced font: `'Consolas', 'Monaco', 'Courier New', monospace`
- Vertical scrolling: `overflow-y: auto` on outer container
- Horizontal scrolling: `overflow-x: auto` on outer container
- Preserve whitespace: `white-space: pre`
- Fixed line height for consistent virtual scroll calculations

#### 4. StatusBar Component

**Responsibility**: Display file metadata (size, line count, encoding).

**Props**:
```typescript
interface StatusBarProps {
  metadata: FileMeta | null;
}
```

**Display Format**:
- File size: Format as "1.2 KB", "3.4 MB", etc.
- Line count: "150 lines"
- Encoding: "UTF-8", "ASCII", etc.
- When no file open: Display empty or "No file open"

#### 5. InteropService (Frontend)

**Responsibility**: Manage communication with C# backend, send requests and handle responses.

**Key Methods**:
```typescript
interface InteropService {
  sendOpenFileRequest(): void;
  sendRequestLines(startLine: number, lineCount: number): void;
  onFileOpened(callback: (data: FileMeta) => void): void;
  onLinesResponse(callback: (data: LinesResponsePayload) => void): void;
  onError(callback: (error: ErrorInfo) => void): void;
}
```

**Implementation**:
```typescript
// InteropService.ts — compiled by tsc to wwwroot/js/InteropService.js
// No module imports — React is a global, types are ambient.

// IMPORTANT: Create a single instance per app lifecycle.
// Do NOT create multiple instances — callbacks are per-instance.

function createInteropService() {
  // ... registers window.external.receiveMessage(handler)
  // ... routes incoming messages by type: FileOpenedResponse, LinesResponse, ErrorResponse
  // ... exposes sendOpenFileRequest(), sendRequestLines(), onFileOpened(), onLinesResponse(), onError(), dispose()
}

// Expose to window for use by App.js
(window as any).createInteropService = createInteropService;
```

## Data Models

### Message Schemas

All messages between backend and frontend use JSON serialization with a common envelope structure:

```typescript
interface MessageEnvelope {
  type: string;
  payload?: any;
  timestamp: string; // ISO 8601
}
```

#### Frontend → Backend Messages

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

#### Backend → Frontend Messages

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

### Frontend State Models

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

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Line Offset Index Correctness

*For any* text file content, when the Backend builds a line offset index during `OpenFileAsync`, seeking to the byte offset recorded for line N and reading one line SHALL produce exactly the Nth line of the original file content.

**Validates: Requirements 6.2**

### Property 2: ReadLinesAsync Round-Trip Correctness

*For any* text file content (including various line endings, whitespace characters, tabs, and special characters) and *for any* valid line range (startLine, lineCount) within the file, `ReadLinesAsync(filePath, startLine, lineCount)` SHALL return an array of lines that exactly matches the corresponding lines from the original file content, preserving all whitespace and characters.

**Validates: Requirements 3.1, 3.7, 6.4**

### Property 3: File Metadata Accuracy

*For any* text file, when `OpenFileAsync` completes, the returned `FileOpenMetadata` SHALL have a `TotalLines` value that exactly matches the number of lines in the file (where lines are delimited by \n, \r\n, or \r) and a `FileSizeBytes` value that exactly matches the file's size on disk.

**Validates: Requirements 2.2, 5.2**

### Property 4: Dialog Cancellation Idempotence

*For any* application state (whether a file is loaded or not, regardless of current scroll position or displayed lines), when the user cancels the File_Picker dialog, the application state SHALL remain identical to the state before the dialog was opened.

**Validates: Requirements 2.3**

### Property 5: Line Number Sequential Generation

*For any* startLine offset and lineCount, the displayed line numbers SHALL form a sequential series from (startLine + 1) to (startLine + lineCount), where each line number corresponds to its 1-based position in the file.

**Validates: Requirements 3.2**

### Property 6: Virtual Scrollbar Height

*For any* totalLines value and a fixed lineHeight, the virtual scroll spacer element's height SHALL equal totalLines × lineHeight pixels, ensuring the scrollbar accurately represents the full extent of the file.

**Validates: Requirements 3.4, 6.6**

### Property 7: Title Bar Format Consistency

*For any* file name, when a file is successfully opened, the Title_Bar SHALL display the title in the format "{fileName} - Editor" where {fileName} is the base name of the file without the full path.

**Validates: Requirements 4.2**

### Property 8: File Size Human-Readable Formatting

*For any* file size in bytes, the Status_Bar SHALL display the size in human-readable format following these rules:
- Sizes < 1024 bytes: display as "X bytes"
- Sizes >= 1024 and < 1048576 bytes: display as "X.Y KB" (rounded to 1 decimal place)
- Sizes >= 1048576 bytes: display as "X.Y MB" (rounded to 1 decimal place)

**Validates: Requirements 5.1**

### Property 9: Encoding Detection Correctness

*For any* file with a detectable BOM (UTF-8 BOM, UTF-16 LE/BE BOM), the Backend's encoding detection SHALL return the correct encoding name. For files without a BOM, the Backend SHALL default to UTF-8.

**Validates: Requirements 5.3**

### Property 10: LinesResponse Message Structure Correctness

*For any* `LinesResult` (containing startLine, lines array, and totalLines), when the Backend serializes it as a `LinesResponse` message, the resulting JSON SHALL conform to the `MessageEnvelope` schema with type `"LinesResponse"` and a payload containing `startLine`, `lines`, and `totalLines` fields matching the original data.

**Validates: Requirements 7.1**

## Error Handling

### Error Categories

The application handles errors at multiple layers with consistent error reporting to the user:

#### 1. File System Errors

**File Not Found**:
- **Detection**: Backend catches `FileNotFoundException` when attempting to scan or read file
- **Response**: Send `ErrorResponse` with code `FILE_NOT_FOUND`
- **User Message**: "The selected file could not be found."
- **Recovery**: User can try opening a different file

**Permission Denied**:
- **Detection**: Backend catches `UnauthorizedAccessException` when attempting to scan or read file
- **Response**: Send `ErrorResponse` with code `PERMISSION_DENIED`
- **User Message**: "You do not have permission to read this file."
- **Recovery**: User can try opening a different file or adjust file permissions

#### 2. Interop Errors

**Message Serialization Failure**:
- **Detection**: Backend catches `JsonException` during message serialization
- **Response**: Log error, send `ErrorResponse` with code `INTEROP_FAILURE`
- **User Message**: "An internal communication error occurred."
- **Recovery**: User can try the operation again

**Message Delivery Failure**:
- **Detection**: Frontend timeout waiting for response (5 seconds)
- **Response**: Frontend displays error locally
- **User Message**: "The application is not responding. Please restart."
- **Recovery**: User should restart the application

#### 3. Encoding Errors

**Unreadable Encoding**:
- **Detection**: Backend catches `DecoderFallbackException` when reading lines
- **Response**: Attempt to read as UTF-8 with replacement characters, send the lines with replacement characters (�) for unreadable bytes
- **User Message**: Displayed inline — unreadable bytes appear as replacement characters
- **Recovery**: File is displayed with replacement characters

### Error Handling Strategy

**Backend Error Handling**:
```csharp
public async Task<FileOpenMetadata> OpenFileAsync(string filePath)
{
    try
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var fileInfo = new FileInfo(filePath);
        var encoding = DetectEncoding(filePath);

        // Scan file to build line offset index
        var lineOffsets = new List<long>();
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true))
        {
            lineOffsets.Add(stream.Position);
            while (reader.ReadLine() != null)
            {
                lineOffsets.Add(stream.Position);
            }
        }

        // Store index for later ReadLinesAsync calls
        _lineIndexCache[filePath] = lineOffsets;

        return new FileOpenMetadata(
            filePath, fileInfo.Name, lineOffsets.Count - 1,
            fileInfo.Length, encoding.EncodingName);
    }
    catch (FileNotFoundException ex)
    {
        await SendErrorAsync("FILE_NOT_FOUND", "The selected file could not be found.", ex.FileName);
        throw;
    }
    catch (UnauthorizedAccessException)
    {
        await SendErrorAsync("PERMISSION_DENIED", "You do not have permission to read this file.", filePath);
        throw;
    }
    catch (Exception)
    {
        await SendErrorAsync("UNKNOWN_ERROR", "An unexpected error occurred while opening the file.", filePath);
        throw;
    }
}

public async Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount)
{
    var lineOffsets = _lineIndexCache[filePath];
    var totalLines = lineOffsets.Count - 1;
    var actualCount = Math.Min(lineCount, totalLines - startLine);

    var encoding = DetectEncoding(filePath);
    var lines = new string[actualCount];

    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    stream.Seek(lineOffsets[startLine], SeekOrigin.Begin);
    using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

    for (int i = 0; i < actualCount; i++)
    {
        lines[i] = await reader.ReadLineAsync() ?? string.Empty;
    }

    return new LinesResult(startLine, lines, totalLines);
}
```

**Frontend Error Handling**:
```typescript
const handleError = (error: ErrorInfo) => {
  setError(error);
  setIsLoading(false);
  setFileMeta(null);
  setLines(null);
};

const handleInteropTimeout = () => {
  setError({
    errorCode: 'INTEROP_FAILURE',
    message: 'The application is not responding. Please restart.',
  });
  setIsLoading(false);
};
```

### Logging Strategy

**Backend Logging**:
- Use structured logging with severity levels (Info, Warning, Error)
- Log all file operations (open/scan, line reads)
- Log all interop messages (sent/received)
- Log all errors with stack traces
- Log file: `editor-app.log` in application directory

**Frontend Logging**:
- Use console logging for development
- Log all interop messages (sent/received)
- Log all state transitions
- Log all errors
- Production: Consider disabling console logs or using a logging service

## Testing Strategy

### Overview

The testing strategy employs a dual approach combining property-based testing for universal correctness properties with example-based unit tests for specific scenarios, edge cases, and integration points.

### Property-Based Testing

**Framework**: Use **FsCheck** for C# backend property tests and **fast-check** for TypeScript frontend property tests.

**Configuration**:
- Minimum 100 iterations per property test
- Each property test must reference its design document property using a comment tag
- Tag format: `// Feature: editor-app, Property {number}: {property_text}`

**Backend Property Tests** (C# with FsCheck):

1. **Line Offset Index Correctness** (Property 1):
   ```csharp
   // Feature: editor-app, Property 1: Line offset index correctness
   [Property]
   public Property LineOffsetIndexCorrectness()
   {
       return Prop.ForAll(
           Arb.Generate<NonEmptyArray<string>>(),
           linesArray =>
           {
               var content = string.Join("\n", linesArray.Get);
               var tempFile = WriteToTempFile(content);
               var metadata = fileService.OpenFileAsync(tempFile).Result;
               
               // For each line, seek using index and verify content
               for (int i = 0; i < linesArray.Get.Length; i++)
               {
                   var result = fileService.ReadLinesAsync(tempFile, i, 1).Result;
                   if (result.Lines[0] != linesArray.Get[i]) return false;
               }
               return true;
           }
       );
   }
   ```

2. **ReadLinesAsync Round-Trip Correctness** (Property 2):
   ```csharp
   // Feature: editor-app, Property 2: ReadLinesAsync round-trip correctness
   [Property]
   public Property ReadLinesRoundTrip()
   {
       return Prop.ForAll(
           Arb.Generate<NonEmptyArray<string>>(),
           Gen.Choose(0, 100).ToArbitrary(),
           Gen.Choose(1, 50).ToArbitrary(),
           (linesArray, startLine, lineCount) =>
           {
               var content = string.Join("\n", linesArray.Get);
               var tempFile = WriteToTempFile(content);
               fileService.OpenFileAsync(tempFile).Wait();
               
               var clampedStart = Math.Min(startLine, linesArray.Get.Length - 1);
               var clampedCount = Math.Min(lineCount, linesArray.Get.Length - clampedStart);
               
               var result = fileService.ReadLinesAsync(tempFile, clampedStart, clampedCount).Result;
               var expected = linesArray.Get.Skip(clampedStart).Take(clampedCount).ToArray();
               
               return result.Lines.SequenceEqual(expected);
           }
       );
   }
   ```

3. **File Metadata Accuracy** (Property 3):
   ```csharp
   // Feature: editor-app, Property 3: File metadata accuracy
   [Property]
   public Property FileMetadataAccuracy()
   {
       return Prop.ForAll(
           Arb.Generate<NonEmptyArray<string>>(),
           linesArray =>
           {
               var content = string.Join("\n", linesArray.Get);
               var tempFile = WriteToTempFile(content);
               var metadata = fileService.OpenFileAsync(tempFile).Result;
               
               var expectedLines = linesArray.Get.Length;
               var expectedSize = new FileInfo(tempFile).Length;
               
               return metadata.TotalLines == expectedLines &&
                      metadata.FileSizeBytes == expectedSize;
           }
       );
   }
   ```

4. **File Size Formatting** (Property 8):
   ```csharp
   // Feature: editor-app, Property 8: File size human-readable formatting
   [Property]
   public Property FileSizeFormatting()
   {
       return Prop.ForAll(
           Arb.Generate<long>().Where(size => size >= 0),
           fileSize =>
           {
               var formatted = FormatFileSize(fileSize);
               
               if (fileSize < 1024)
                   return formatted.EndsWith(" bytes");
               else if (fileSize < 1048576)
                   return formatted.EndsWith(" KB");
               else
                   return formatted.EndsWith(" MB");
           }
       );
   }
   ```

5. **LinesResponse Message Structure** (Property 10):
   ```csharp
   // Feature: editor-app, Property 10: LinesResponse message structure correctness
   [Property]
   public Property LinesResponseMessageStructure()
   {
       return Prop.ForAll(
           Arb.Generate<int>().Where(i => i >= 0),
           Arb.Generate<string[]>().Where(a => a != null),
           Arb.Generate<int>().Where(i => i >= 0),
           (startLine, lines, totalLines) =>
           {
               var linesResult = new LinesResult(startLine, lines, totalLines);
               var message = messageRouter.SerializeLinesResponse(linesResult);
               var envelope = JsonSerializer.Deserialize<MessageEnvelope>(message);
               
               return envelope.Type == "LinesResponse" &&
                      envelope.Payload != null &&
                      envelope.Timestamp != null;
           }
       );
   }
   ```

**Frontend Property Tests** (TypeScript with fast-check):

1. **Title Bar Format** (Property 7):
   ```typescript
   // Feature: editor-app, Property 7: Title bar format consistency
   test('title bar format consistency', () => {
     fc.assert(
       fc.property(fc.string({ minLength: 1 }), (fileName) => {
         const title = formatTitleBar(fileName);
         return title === `${fileName} - Editor`;
       }),
       { numRuns: 100 }
     );
   });
   ```

2. **Line Number Generation** (Property 5):
   ```typescript
   // Feature: editor-app, Property 5: Line number sequential generation
   test('line numbers are sequential from startLine offset', () => {
     fc.assert(
       fc.property(
         fc.nat(10000),
         fc.integer({ min: 1, max: 200 }),
         (startLine, lineCount) => {
           const lineNumbers = generateLineNumbers(startLine, lineCount);
           return lineNumbers.every((num, idx) => num === startLine + idx + 1);
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

3. **Virtual Scrollbar Height** (Property 6):
   ```typescript
   // Feature: editor-app, Property 6: Virtual scrollbar height
   test('scrollbar height equals totalLines * lineHeight', () => {
     fc.assert(
       fc.property(
         fc.integer({ min: 0, max: 10_000_000 }),
         fc.integer({ min: 10, max: 30 }),
         (totalLines, lineHeight) => {
           const height = calculateScrollHeight(totalLines, lineHeight);
           return height === totalLines * lineHeight;
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

4. **Dialog Cancellation Idempotence** (Property 4):
   ```typescript
   // Feature: editor-app, Property 4: Dialog cancellation idempotence
   test('canceling dialog preserves state', () => {
     fc.assert(
       fc.property(fc.record({
         fileMeta: fc.option(fc.record({
           fileName: fc.string(),
           totalLines: fc.nat(),
           fileSizeBytes: fc.nat(),
           encoding: fc.string()
         }), { nil: null }),
         isLoading: fc.boolean(),
         error: fc.option(fc.string(), { nil: null })
       }), (initialState) => {
         const stateBefore = JSON.parse(JSON.stringify(initialState));
         handleDialogCancel(initialState);
         return JSON.stringify(initialState) === JSON.stringify(stateBefore);
       }),
       { numRuns: 100 }
     );
   });
   ```

### Unit Testing

**Purpose**: Test specific examples, edge cases, error conditions, and integration points that are not suitable for property-based testing.

**Framework**: Use **xUnit** for C# backend tests and **Vitest** for TypeScript frontend tests.

**Backend Unit Tests**:

1. **Initial State Display** (Requirement 1.2):
   ```csharp
   [Fact]
   public void App_OnLaunch_ShowsEmptyContentArea()
   {
       var app = new App();
       var initialState = app.GetInitialState();
       
       Assert.Null(initialState.FileMeta);
       Assert.False(initialState.IsLoading);
       Assert.Null(initialState.Error);
   }
   ```

2. **Permission Error Handling** (Requirement 2.4):
   ```csharp
   [Fact]
   public async Task OpenFile_PermissionDenied_SendsErrorResponse()
   {
       var mockFileSystem = new Mock<IFileSystem>();
       mockFileSystem
           .Setup(fs => fs.OpenRead(It.IsAny<string>()))
           .Throws(new UnauthorizedAccessException());
       
       var fileService = new FileService(mockFileSystem.Object);
       
       await Assert.ThrowsAsync<UnauthorizedAccessException>(
           () => fileService.OpenFileAsync("test.txt")
       );
       
       mockMessageRouter.Verify(mr => mr.SendToUIAsync(
           It.Is<ErrorResponse>(er => er.ErrorCode == "PERMISSION_DENIED")
       ));
   }
   ```

3. **File Not Found Error Handling** (Requirement 2.5):
   ```csharp
   [Fact]
   public async Task OpenFile_FileNotFound_SendsErrorResponse()
   {
       var mockFileSystem = new Mock<IFileSystem>();
       mockFileSystem
           .Setup(fs => fs.Exists(It.IsAny<string>()))
           .Returns(false);
       
       var fileService = new FileService(mockFileSystem.Object);
       
       await Assert.ThrowsAsync<FileNotFoundException>(
           () => fileService.OpenFileAsync("missing.txt")
       );
       
       mockMessageRouter.Verify(mr => mr.SendToUIAsync(
           It.Is<ErrorResponse>(er => er.ErrorCode == "FILE_NOT_FOUND")
       ));
   }
   ```

4. **Empty State Title Bar** (Requirement 4.1):
   ```csharp
   [Fact]
   public void TitleBar_NoFileOpen_DisplaysAppName()
   {
       var title = FormatTitleBar(null);
       Assert.Equal("Editor", title);
   }
   ```

5. **Empty State Status Bar** (Requirement 5.4):
   ```csharp
   [Fact]
   public void StatusBar_NoFileOpen_DisplaysNoMetadata()
   {
       var statusBar = new StatusBar();
       var display = statusBar.GetDisplay(null);
       Assert.Empty(display);
   }
   ```

**Frontend Unit Tests**:

1. **Loading State Display** (Requirement 6.3):
   ```typescript
   test('displays loading indicator while file is being scanned', () => {
     const { getByText } = render(
       <ContentArea fileMeta={null} lines={null} linesStartLine={0}
                    isLoading={true} error={null} onRequestLines={() => {}} />
     );
     expect(getByText('Scanning file...')).toBeInTheDocument();
   });
   ```

2. **Error State Display**:
   ```typescript
   test('displays error message when error occurs', () => {
     const error = {
       errorCode: 'FILE_NOT_FOUND',
       message: 'The selected file could not be found.'
     };
     const { getByText } = render(
       <ContentArea fileMeta={null} lines={null} linesStartLine={0}
                    isLoading={false} error={error} onRequestLines={() => {}} />
     );
     expect(getByText(error.message)).toBeInTheDocument();
   });
   ```

3. **Monospaced Font Styling** (Requirement 3.3):
   ```typescript
   test('content area uses monospaced font', () => {
     const { container } = render(
       <ContentArea fileMeta={mockFileMeta} lines={['hello']} linesStartLine={0}
                    isLoading={false} error={null} onRequestLines={() => {}} />
     );
     const contentLine = container.querySelector('.content-line');
     const styles = window.getComputedStyle(contentLine);
     expect(styles.fontFamily).toMatch(/Consolas|Monaco|Courier New|monospace/);
   });
   ```

4. **Horizontal Scrolling** (Requirement 3.6):
   ```typescript
   test('content area provides horizontal scrolling for wide lines', () => {
     const longLine = 'x'.repeat(5000);
     const { container } = render(
       <ContentArea fileMeta={mockFileMeta} lines={[longLine]} linesStartLine={0}
                    isLoading={false} error={null} onRequestLines={() => {}} />
     );
     const contentArea = container.querySelector('.content-area');
     expect(contentArea.scrollWidth).toBeGreaterThan(contentArea.clientWidth);
   });
   ```

5. **Interop Communication Failure** (Requirement 7.4):
   ```typescript
   test('displays error when interop fails', () => {
     const mockInterop = {
       sendOpenFileRequest: jest.fn(() => { throw new Error('Interop failure'); }),
       sendRequestLines: jest.fn(),
       onFileOpened: jest.fn(),
       onLinesResponse: jest.fn(),
       onError: jest.fn(),
     };
     const { getByText } = render(<App interop={mockInterop} />);
     fireEvent.click(getByText('Open File'));
     expect(getByText(/communication failure/i)).toBeInTheDocument();
   });
   ```

### Integration Testing

**Purpose**: Test end-to-end flows and integration with external systems (OS file dialogs, Photino interop).

**Approach**: Use manual testing or UI automation tools for integration tests.

**Key Integration Tests**:

1. **Application Startup** (Requirement 1.1):
   - Launch application binary
   - Verify window appears within 3 seconds
   - Verify initial state is displayed

2. **Native File Picker** (Requirement 2.1):
   - Trigger open file action
   - Verify native OS file dialog appears
   - Verify dialog is modal and blocks application

3. **Streamed File Open** (Requirement 6.1, 6.2):
   - Open a large file (100MB+)
   - Verify the scan completes and metadata is sent
   - Verify memory usage stays bounded (not loading entire file)

4. **Virtual Scroll Line Requests** (Requirement 3.5, 7.3):
   - Open a file, scroll to various positions
   - Verify RequestLinesMessage is sent and LinesResponse is received
   - Verify correct lines are displayed at each scroll position

5. **Keyboard Shortcut** (Requirement 8.1):
   - Press Ctrl+O (or Cmd+O on macOS)
   - Verify file picker dialog appears

6. **Cross-Platform Compatibility** (Requirement 1.4):
   - Run application on Windows, macOS, and Linux
   - Verify all features work on each platform

### Test Coverage Goals

- **Backend**: 80%+ code coverage for FileService, MessageRouter, and core logic
- **Frontend**: 80%+ code coverage for React components and interop service
- **Property Tests**: 100% coverage of all correctness properties (10 properties)
- **Integration Tests**: Coverage of all cross-boundary interactions (interop, file system, OS dialogs)

### Test Organization

**Backend Tests**:
```
tests/
├── EditorApp.Tests/
│   ├── Unit/
│   │   ├── FileServiceTests.cs
│   │   ├── MessageRouterTests.cs
│   │   └── KeyboardShortcutHandlerTests.cs
│   ├── Properties/
│   │   ├── LineIndexProperties.cs
│   │   ├── ReadLinesProperties.cs
│   │   ├── MetadataProperties.cs
│   │   ├── FormattingProperties.cs
│   │   └── InteropProperties.cs
│   └── Integration/
│       ├── PhotinoHostTests.cs
│       └── EndToEndTests.cs
```

**Frontend Tests**:
```
src/
├── components/
│   ├── __tests__/
│   │   ├── App.test.tsx
│   │   ├── ContentArea.test.tsx
│   │   ├── TitleBar.test.tsx
│   │   └── StatusBar.test.tsx
│   └── __properties__/
│       ├── titleBar.properties.test.ts
│       ├── lineNumbers.properties.test.ts
│       ├── scrollbar.properties.test.ts
│       └── statePreservation.properties.test.ts
└── services/
    └── __tests__/
        └── InteropService.test.ts
```

### Continuous Integration

- Run all unit tests and property tests on every commit
- Run integration tests on pull requests
- Enforce minimum code coverage thresholds
- Run tests on all target platforms (Windows, macOS, Linux) in CI pipeline

## Detailed Test Design

For the complete test design including test matrices, test infrastructure (mocks, helpers), and property-based test specifications, see **[test-design.md](./test-design.md)**.
