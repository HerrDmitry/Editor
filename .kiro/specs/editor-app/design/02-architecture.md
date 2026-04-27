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
