# Components and Interfaces

## Backend Components

### 1. PhotinoHostService

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

### 2. FileService

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

### 3. MessageRouter

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

### 4. KeyboardShortcutHandler

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

## Frontend Components

### 1. App Component

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
  wrapLines: boolean;              // line wrapping state (default: false)
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
    wrapLines={wrapLines}
    onRequestLines={handleRequestLines}
  />
  <StatusBar 
    metadata={fileMeta}
    wrapLines={wrapLines}
    onWrapLinesChange={handleWrapLinesChange}
  />
</div>
```

### 2. TitleBar Component

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

### 3. ContentArea Component

**Responsibility**: Display file contents with virtual scrolling, line numbers, custom scrollbar, and loading/error states. Only the lines currently visible in the viewport are rendered.

**Props**:
```typescript
interface ContentAreaProps {
  fileMeta: FileMeta | null;
  lines: string[] | null;
  linesStartLine: number;
  isLoading: boolean;
  error: ErrorInfo | null;
  wrapLines: boolean;
  onRequestLines: (startLine: number, lineCount: number) => void;
}
```

**Rendering Modes**:
- **Empty State**: No file open → Display prompt message "Press Ctrl+O to open a file"
- **Loading State**: File being scanned → Display spinner with "Scanning file..."
- **Error State**: Error occurred → Display error message with icon
- **Content State**: File metadata received → Three-column virtual-scrolling layout

**Three-Column Layout**:

```
┌────────┬──────────────────────────┬───────────┐
│ Line   │ Content Column           │ Custom    │
│ Numbers│ (native scroll hidden)   │ Scrollbar │
│ (60px) │ (flex)                   │ (14px)    │
└────────┴──────────────────────────┴───────────┘
```

1. **Line numbers column** (60px): Synced vertically with content, no horizontal scroll
2. **Content column** (flex): Native scrollbar hidden via CSS, handles wheel/trackpad input
3. **Custom scrollbar** (14px): Operates in line space, reflects current position

**Virtual Scrolling with Capped Height**:

Browsers cap element heights at ~33 million pixels. For files with more than ~1.6M lines (at 20px/line), the spacer would exceed this. The spacer height is capped at 10 million pixels.

**Proportional Scroll Mapping**:

Since the spacer height is capped, a simple `scrollTop * scale / LINE_HEIGHT` mapping fails to reach the last line. Instead, use proportional mapping:

```typescript
// scrollTop → line number
scrollFraction = scrollTop / maxScrollTop;  // 0.0 to 1.0
line = scrollFraction * (totalLines - visibleLineCount);

// line number → scrollTop
scrollFraction = line / (totalLines - visibleLineCount);
scrollTop = scrollFraction * maxScrollTop;
```

This ensures:
- `scrollTop = 0` → line 0
- `scrollTop = max` → last possible line (totalLines - visibleLineCount)
- Linear and exact regardless of scroll height cap

**Line Positioning Clamping**:

Rendered lines are positioned at `lineToScrollTop(linesStartLine)`, clamped so they never extend past the spacer bottom:
```typescript
clampedOffset = Math.min(linesPixelOffset, totalHeight - renderedLinesHeight);
```
This prevents the browser from bouncing `scrollTop` back when lines overflow the spacer.

**Styling Requirements**:
- Monospaced font: `'Consolas', 'Monaco', 'Courier New', monospace`
- Native vertical scrollbar hidden: `scrollbar-width: none` + `::-webkit-scrollbar { display: none }`
- Horizontal scrolling: `overflow-x: auto` on content column when `wrapLines` is false, `overflow-x: hidden` when `wrapLines` is true
- Preserve whitespace: `white-space: pre` when `wrapLines` is false, `white-space: pre-wrap` when `wrapLines` is true
- Fixed line height (20px) for consistent virtual scroll calculations

**Line Wrapping Behavior**:

When `wrapLines` is true:
- Lines that exceed the visible width of the content column are wrapped to multiple visual rows
- The line number is displayed only on the first visual row of each logical line
- Subsequent visual rows of the same logical line have no line number displayed
- The vertical scrollbar continues to represent logical lines (not visual rows)
- Horizontal scrolling is disabled (`overflow-x: hidden`)
- Text wrapping is enabled (`white-space: pre-wrap`)

When `wrapLines` is false:
- Lines are displayed without wrapping
- Each logical line occupies exactly one visual row
- Horizontal scrolling is enabled for lines that exceed the visible width
- Text wrapping is disabled (`white-space: pre`)

**Line Number Rendering with Wrapping**:

The line numbers column must be synchronized with the content column. When a line wraps to multiple visual rows, the line number appears only on the first row. This can be achieved by:
1. Rendering each logical line as a separate container (e.g., a `<div>` with class `line-container`)
2. Within each container, display the line number and the line content side-by-side
3. The line number has `align-self: flex-start` or similar to stay at the top of the container
4. The line content wraps naturally within its column when `white-space: pre-wrap` is set

Example structure:
```tsx
<div className="line-container">
  <div className="line-number">{lineNum}</div>
  <div className="line-content" style={{ whiteSpace: wrapLines ? 'pre-wrap' : 'pre' }}>
    {lineText}
  </div>
</div>
```

### 3a. CustomScrollbar Component

**Responsibility**: A generic, self-contained, reusable scrollbar component driven by abstract numeric `range` and `position` values. It has no awareness of what the values represent (lines, pixels, columns, or any other unit) and no dependencies on the Content_Area, Backend, or any other application-specific component.

**Props**:
```typescript
interface CustomScrollbarProps {
  range: number;           // Total scrollable extent (abstract numeric value)
  position: number;        // Current offset within the range (0 to range)
  viewportSize: number;    // Viewport size in the same units as range (for thumb sizing)
  onPositionChange?: (position: number) => void;  // Called only on user-initiated drags
}
```

**Thumb Sizing**:
- Thumb height is proportional to the viewport relative to the total range: `thumbHeight = Math.max(MIN_THUMB_HEIGHT, (viewportSize / range) * trackHeight)`
- Example: if `viewportSize = 50` and `range = 10000`, the thumb occupies 0.5% of the track
- `MIN_THUMB_HEIGHT` ensures the thumb remains grabbable (e.g. 20px minimum)

**Position-to-Thumb Mapping**:
- The thumb position within the track is a linear mapping from `position` (0 to `range`) to the scrollable track space:
  ```
  scrollableTrack = trackHeight - thumbHeight
  thumbTop = (position / range) * scrollableTrack
  ```
- When `position = 0`: thumb is at the very top
- When `position = range`: thumb is at the very bottom
- When `position = range / 2`: center of thumb is at the vertical center of the track

**Interactions**:
- **External position update** (prop change): The thumb moves to the corresponding location. The `onPositionChange` callback is NOT called — this distinguishes programmatic updates from user-initiated drags.
- **Thumb drag**: mousedown on thumb → document-level mousemove/mouseup → calculate position from thumb location within the track → call `onPositionChange(calculatedPosition)`

**Drag Position Calculation**:
When the user drags the thumb to a pixel offset `thumbTop` within the track:
```
scrollableTrack = trackHeight - thumbHeight
calculatedPosition = (thumbTop / scrollableTrack) * range
```
This ensures:
- Thumb at top of track → reports `position = 0`
- Thumb at bottom of track → reports `position = range`
- Thumb centered in track → reports `position = range / 2`

**Design Rationale**:
- The component is completely generic — it can be used for vertical scrolling, horizontal scrolling, volume sliders, or any other range-based control
- The separation between external updates (no event) and user drags (with event) prevents feedback loops when the parent component updates position programmatically
- No track-click behavior — only thumb dragging is supported for position changes

### 4. StatusBar Component

**Responsibility**: Display file metadata (size, line count, encoding) and provide line wrapping control.

**Props**:
```typescript
interface StatusBarProps {
  metadata: FileMeta | null;
  wrapLines: boolean;
  onWrapLinesChange: (enabled: boolean) => void;
}
```

**Display Format**:
- File size: Format as "1.2 KB", "3.4 MB", etc.
- Line count: "150 lines"
- Encoding: "UTF-8", "ASCII", etc.
- Wrap Lines checkbox: Labeled "Wrap Lines", reflects current wrapping state
- When no file open: Display empty or "No file open"

**Interaction**:
- When the user toggles the "Wrap Lines" checkbox, call `onWrapLinesChange(newValue)` to notify the parent component (App) of the state change

### 5. InteropService (Frontend)

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
