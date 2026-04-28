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

**Responsibility**: Root component managing application state, buffer management, and layout structure.

**State**:
```typescript
interface AppState {
  fileMeta: FileMeta | null;       // metadata from FileOpenedResponse
  lines: string[] | null;          // current line buffer (sliding window)
  linesStartLine: number;          // first logical line number in the buffer
  isLoading: boolean;              // true during initial file scan
  error: ErrorInfo | null;
  titleBarText: string;
  wrapLines: boolean;              // line wrapping state (default: false)
}
```

**Refs**:
- `linesRef` / `linesStartRef` — track current buffer for async merge callback
- `isJumpRequestRef` — distinguishes merge (append) from replace (jump) in `onLinesResponse`

**Buffer Management**:

The App component owns the line buffer and implements two distinct response strategies:

1. **Merge (edge-proximity scroll)**: When `onLinesResponse` fires and `isJumpRequestRef` is false, new lines are merged with the existing buffer. The merged array covers the union of both ranges, with new lines overwriting any overlap. No trimming happens here — ContentArea handles that after measuring DOM.

2. **Replace (scrollbar jump)**: When `isJumpRequestRef` is true, the buffer is replaced entirely with the new response. This is used when the user drags the scrollbar thumb to a position outside the current buffer.

**Callbacks**:
- `handleRequestLines(startLine, lineCount)` — sends `RequestLinesMessage` to backend (for edge-proximity fetches)
- `handleJumpToLine(startLine, lineCount)` — sets `isJumpRequestRef = true`, then sends `RequestLinesMessage` (for scrollbar jumps outside buffer)
- `handleTrimBuffer(newStart, newLines)` — called by ContentArea after it measures DOM heights and trims the buffer; updates `linesStartLine` and `lines` state

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
    onJumpToLine={handleJumpToLine}
    onTrimBuffer={handleTrimBuffer}
  />
  <StatusBar 
    metadata={fileMeta}
    wrapLines={wrapLines}
    onWrapLinesChange={handleWrapLinesChange}
  />
</div>
```

**Initial Request**: On `FileOpenedResponse`, App requests 200 lines (not 50) to fill the initial buffer generously.

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

**Responsibility**: Display file contents with sliding-window virtual scrolling, line numbers, custom scrollbar, buffer trim logic, and loading/error states. Uses a single unified rendering path for both wrapped and non-wrapped modes, with native browser scrolling in all modes.

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
  onJumpToLine: (startLine: number, lineCount: number) => void;
  onTrimBuffer: (newStart: number, newLines: string[]) => void;
}
```

**Constants**:
```typescript
const WINDOW_SIZE = 400;              // Max logical lines in sliding window
const EDGE_THRESHOLD = 600;           // Pixels from edge to trigger fetch
const FETCH_SIZE = 200;               // Lines to request per fetch
const LINE_HEIGHT = 20;               // Fixed height for line-number column
const SCROLLBAR_VIEWPORT_SIZE = 50;   // Viewport size for scrollbar thumb sizing
```

**Rendering Modes**:
- **Empty State**: No file open → Display prompt message "Press Ctrl+O to open a file"
- **Loading State**: File being scanned → Display spinner with "Scanning file..."
- **Error State**: Error occurred → Display error message with icon
- **Content State**: File metadata received → Sliding-window layout with custom scrollbar

**Unified Layout (Single Rendering Path)**:

There is ONE rendering path for content state. The only difference between wrap and non-wrap modes is CSS styling applied to the same DOM structure. There are NO separate `if (wrapLines) { ... } else { ... }` render branches.

```
┌──────────────────────────────────────────┬───────────┐
│ Viewport (overflow-y: auto)              │ Custom    │
│ ┌──────────────────────────────────────┐ │ Scrollbar │
│ │ Container (real DOM lines)           │ │ (14px)    │
│ │ ┌────────┬─────────────────────────┐ │ │           │
│ │ │ Line   │ Content Column          │ │ │           │
│ │ │ Numbers│                         │ │ │           │
│ │ │ (60px) │                         │ │ │           │
│ │ └────────┴─────────────────────────┘ │ │           │
│ └──────────────────────────────────────┘ │           │
└──────────────────────────────────────────┴───────────┘
```

The viewport div has `overflow-y: auto` and clips the container. The container holds up to WINDOW_SIZE (400) real DOM line elements. No spacer div, no fake height. The custom scrollbar sits outside the viewport.

**Sliding Window DOM Structure**:

```tsx
// SINGLE render path — no branching on wrapLines
<div className="content-area content-area--virtual" style={{ display: 'flex', flexDirection: 'row' }}>
  <div
    className="content-column content-column--hidden-scrollbar"
    ref={viewportRef}
    onScroll={handleScroll}
    style={{ overflowY: 'auto', overflowX: wrapLines ? 'hidden' : 'auto', flex: 1, minWidth: 0 }}
  >
    <div ref={containerRef}>
      {lines && lines.map((line, index) => (
        <div key={linesStartLine + index} className="line-container"
             style={{ display: 'flex', flexDirection: 'row', minHeight: LINE_HEIGHT }}>
          <div className="line-number-row"
               style={{ flexShrink: 0, width: 60, textAlign: 'right', paddingRight: 12,
                        alignSelf: 'flex-start', height: LINE_HEIGHT, lineHeight: `${LINE_HEIGHT}px` }}>
            {linesStartLine + index + 1}
          </div>
          <pre className="content-line"
               style={{ whiteSpace: wrapLines ? 'pre-wrap' : 'pre', margin: 0, flex: 1,
                        minWidth: 0, wordBreak: wrapLines ? 'break-all' : 'normal' }}>
            {line}
          </pre>
        </div>
      ))}
    </div>
  </div>
  <CustomScrollbar
    range={fileMeta.totalLines}
    position={scrollbarPosition}
    viewportSize={SCROLLBAR_VIEWPORT_SIZE}
    onPositionChange={handleScrollbarDrag}
  />
</div>
```

Key points:
- `wrapLines` only affects CSS properties (`whiteSpace`, `overflowX`, `wordBreak`) — never the DOM structure or code path
- Line numbers and content are always in the same `line-container` flex row
- Line numbers use `alignSelf: 'flex-start'` so they stay at the top when content wraps to multiple visual rows
- No spacer div, no absolute positioning, no `totalHeight` calculation
- **Validates: Requirements 3.10, 11.8**

**Sliding Window Architecture**:

The sliding window replaces the old spacer-based virtual scrolling approach:

- **No spacer div**: The container holds real DOM lines. The viewport clips with `overflow-y: auto` → native pixel-smooth scroll.
- **No proportional mapping**: Line positions are determined by walking DOM `.line-container` elements and measuring actual `offsetHeight` values.
- **No height cap**: Since there's no fake height element, browser element height limits are irrelevant.
- **Works identically for wrap and non-wrap**: Same DOM, just CSS. Wrapped lines are taller but the sliding window handles variable heights naturally.

**Edge-Proximity Fetching**:

The `handleScroll` callback checks distance to container edges:
- Near bottom (`containerHeight - scrollTop - viewportHeight < EDGE_THRESHOLD`): request FETCH_SIZE lines starting from buffer end
- Near top (`scrollTop < EDGE_THRESHOLD`): request FETCH_SIZE lines before buffer start
- A `pendingRequestRef` prevents duplicate requests until new data arrives

**`useLayoutEffect` — Trim and Scroll Adjustment**:

Runs after DOM update, before paint. Handles three scenarios:

1. **Prepend (scroll up)**: New lines added before existing buffer → measure added height → `scrollTop += addedHeight` → no visual jump

2. **Buffer exceeds WINDOW_SIZE**: After merge, buffer may exceed 400 lines. Trim logic:
   - Closer to top (user scrolled up, excess at bottom) → trim from bottom → no scroll adjustment needed
   - Closer to bottom (user scrolled down, excess at top) → measure height of removed lines → `scrollTop -= removedHeight` → no visual jump
   - Calls `onTrimBuffer(newStartLine, newLines)` to update App state

3. **Jump from scrollbar drag**: `isJumpingRef` is true → walk DOM to find target line offset → set `scrollTop` directly

The `isTrimming` ref prevents the trim-triggered re-render from re-entering the layout effect.

**Scrollbar Communication (Two Directions)**:

```
Direction 1: wheel scroll → scrollbar (no callback)
  Browser native scroll → onScroll handler fires
  → Walk DOM line-containers, accumulate heights vs scrollTop
  → Find first visible logical line
  → setScrollbarPosition(firstVisibleLine)
  → CustomScrollbar receives position prop → moves thumb
  → CustomScrollbar does NOT call onPositionChange (prop update, not user drag)

Direction 2: thumb drag → content (local scroll or backend jump)
  User drags thumb → CustomScrollbar calls onPositionChange(linePosition)
  → handleScrollbarDrag computes targetLine
  → If targetLine in buffer:
      Walk DOM to measure offset → set viewport.scrollTop directly
      No backend request needed
  → If targetLine outside buffer:
      Debounce 150ms → onJumpToLine(startLine, FETCH_SIZE)
      App replaces buffer entirely (isJumpRequestRef = true)
      useLayoutEffect detects jump → measures DOM → sets scrollTop
```

**`handleScrollbarDrag` Implementation**:

```typescript
const handleScrollbarDrag = React.useCallback((pos: number) => {
  const meta = fileMetaRef.current;
  if (!meta) return;
  const targetLine = Math.max(0, Math.min(Math.round(pos), meta.totalLines - 1));

  // Update scrollbar position immediately for visual feedback
  setScrollbarPosition(targetLine);

  // If target is already in the buffer, scroll to it locally
  const buf = bufferRef.current;
  if (buf.count > 0 && targetLine >= buf.start && targetLine < buf.start + buf.count) {
    const lineIndex = targetLine - buf.start;
    const lineElements = containerRef.current.querySelectorAll('.line-container');
    let targetOffset = 0;
    for (let i = 0; i < lineIndex; i++) {
      targetOffset += (lineElements[i] as HTMLElement).offsetHeight;
    }
    viewportRef.current.scrollTop = targetOffset;
    return;
  }

  // Target outside buffer — debounced backend request
  if (dragDebounceRef.current) clearTimeout(dragDebounceRef.current);
  dragDebounceRef.current = setTimeout(() => {
    const halfWindow = Math.floor(FETCH_SIZE / 2);
    const startLine = Math.max(0, targetLine - halfWindow);
    const count = Math.min(FETCH_SIZE, meta.totalLines - startLine);
    jumpTargetLineRef.current = targetLine;
    isJumpingRef.current = true;
    onJumpToLine(startLine, count);
  }, 150);
}, [onJumpToLine]);
```

Key design decisions:
- **Single handler**: ONE `handleScrollbarDrag` for both wrap and non-wrap modes
- **Local scroll first**: If target line is in buffer, no backend request — just measure DOM and set scrollTop
- **Debounced jump**: If target outside buffer, wait 150ms for drag to settle before requesting
- **No suppressScrollRef**: The old spacer-based approach needed scroll suppression to prevent feedback loops. The sliding window approach doesn't need it because local scrolls don't trigger line requests (the line is already in buffer), and jump requests replace the buffer entirely
- **Validates: Requirements 10.4, 10.11, 10.12, 12.9, 12.10, 12.11**

**Native Browser Scrolling for All Modes**:

Both wrapped and non-wrapped modes use the same scrolling mechanism:
- The viewport div has `overflow-y: auto`
- Real DOM lines inside the container provide natural scrollable height
- The browser handles all wheel/trackpad input natively, providing pixel-smooth scrolling
- There is NO manual `onWheel` handler that translates `deltaY` to line jumps
- There is NO `overflow-y: hidden` in any mode
- There is NO spacer div or fake height
- **Validates: Requirements 3.5, 12.3, 12.4, 12.5**

**Styling Requirements**:
- Monospaced font: `'Consolas', 'Monaco', 'Courier New', monospace`
- Native vertical scrollbar hidden: `scrollbar-width: none` + `::-webkit-scrollbar { display: none }`
- Horizontal scrolling: `overflow-x: auto` on content column when `wrapLines` is false, `overflow-x: hidden` when `wrapLines` is true
- Preserve whitespace: `white-space: pre` when `wrapLines` is false, `white-space: pre-wrap` when `wrapLines` is true
- Fixed line height (20px) for line number column only — content lines have variable height when wrapped

**Line Wrapping Behavior**:

When `wrapLines` is true:
- Lines that exceed the visible width of the content column are wrapped to multiple visual rows
- The line number is displayed only on the first visual row of each logical line (via `alignSelf: 'flex-start'`)
- The vertical scrollbar continues to represent logical lines (not visual rows)
- Horizontal scrolling is disabled (`overflow-x: hidden`)
- Text wrapping is enabled (`white-space: pre-wrap`, `word-break: break-all`)

When `wrapLines` is false:
- Lines are displayed without wrapping
- Each logical line occupies exactly one visual row
- Horizontal scrolling is enabled for lines that exceed the visible width (`overflow-x: auto`)
- Text wrapping is disabled (`white-space: pre`)

Both modes use the identical DOM structure — only CSS properties differ. The sliding window handles variable line heights naturally, so wrap mode works without any special code path.

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
- The separation between external updates (no event) and user drags (with event) is the foundation of the unidirectional scrollbar communication pattern: when ContentArea updates the `position` prop (e.g., after a native scroll event), the scrollbar moves its thumb but does NOT call `onPositionChange`, preventing feedback loops
- No track-click behavior — only thumb dragging is supported for position changes
- **Validates: Requirements 10.4, 10.11, 10.12**

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
