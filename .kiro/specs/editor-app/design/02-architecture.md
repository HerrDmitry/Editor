# Architecture

## High-Level Architecture

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

## Component Interaction Flow

**File Open Flow:**
1. User triggers open action (keyboard shortcut Ctrl+O or UI button)
2. React UI sends `OpenFileRequest` message to backend
3. Backend displays native file picker dialog
4. User selects file → Backend scans the file (reads through once to build a line offset index mapping each line number to its byte offset, counts total lines, detects encoding)
5. Backend sends `FileOpenedResponse` message to React UI (metadata only: totalLines, fileSize, encoding, fileName — no file content)
6. React UI sets up virtual scrollbar (range = totalLines)
7. React UI sends `RequestLinesMessage` for the initial buffer (lines 0–199, FETCH_SIZE = 200)
8. Backend seeks to the byte offset for the requested start line and reads the requested number of lines from disk
9. Backend sends `LinesResponse` to React UI (startLine, lines array, totalLines)
10. React UI renders the lines as real DOM in the Content_Area (sliding window)

**Scroll Flow (Mouse Wheel / Trackpad — Sliding Window):**
1. User scrolls the Content_Area via mouse wheel or trackpad
2. Browser handles the scroll natively (`overflow-y: auto` on viewport div) — pixel-smooth in all modes
3. React `onScroll` handler fires on the viewport
4. Handler walks DOM `.line-container` elements, accumulates heights vs `scrollTop` to find first visible logical line
5. Handler updates the CustomScrollbar `position` prop with the first visible line number
6. CustomScrollbar moves its thumb to the new position — does NOT call `onPositionChange` (prop update, not user drag)
7. Handler checks edge proximity: if `distToBottom < EDGE_THRESHOLD` (600px), requests FETCH_SIZE (200) lines from backend starting at buffer end; if `scrollTop < EDGE_THRESHOLD`, requests lines before buffer start
8. Backend seeks to the byte offset for startLine using the line index, reads the requested lines
9. Backend sends `LinesResponse` → App merges new lines into existing buffer (not replace)
10. `useLayoutEffect` in ContentArea detects buffer > WINDOW_SIZE (400):
    - Trim from top (scroll down): measure removed height → `scrollTop -= removedHeight` → no visual jump
    - Trim from bottom (scroll up): just remove, no scroll adjustment needed
11. If lines were prepended (scroll up): `useLayoutEffect` measures added height → `scrollTop += addedHeight` → no visual jump

**Scroll Flow (Scrollbar Thumb Drag):**
1. User drags the CustomScrollbar thumb
2. CustomScrollbar calls `onPositionChange(linePosition)` with the computed line number
3. ContentArea's `handleScrollbarDrag` checks if target line is within current buffer:
   - **In buffer**: walk DOM line-containers to measure offset, set `viewport.scrollTop` directly — no backend request
   - **Outside buffer**: debounce 150ms, then call `onJumpToLine(startLine, FETCH_SIZE)` → App sets `isJumpRequestRef = true` and sends request
4. Backend responds with `LinesResponse`
5. App detects jump request → replaces buffer entirely (not merge)
6. `useLayoutEffect` in ContentArea detects `isJumpingRef` → walks DOM to find target line offset → sets `viewport.scrollTop`

**Error Handling Flow:**
1. Backend encounters error (permission denied, file not found)
2. Backend sends `ErrorResponse` message to React UI
3. React UI displays error message to user
4. Application returns to previous state

**Line Wrapping Toggle Flow:**
1. User toggles the "Wrap Lines" checkbox in the StatusBar
2. StatusBar calls `onWrapLinesChange(newValue)` callback
3. App component updates `wrapLines` state
4. App component passes updated `wrapLines` prop to ContentArea
5. ContentArea re-renders the same DOM structure with different CSS properties:
   - If `wrapLines` is true: applies `white-space: pre-wrap`, `word-break: break-all`, and `overflow-x: hidden` to the existing line containers
   - If `wrapLines` is false: applies `white-space: pre`, `word-break: normal`, and `overflow-x: auto` to the existing line containers
6. No code path branching occurs — the same render function produces the same DOM, only CSS values change
7. Vertical scrollbar continues to represent logical lines (not visual rows) regardless of wrapping state
8. Native browser scrolling (`overflow-y: auto`) continues to work identically in both modes — sliding window works the same regardless of wrap state since all lines are real DOM

## Deployment Architecture

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
