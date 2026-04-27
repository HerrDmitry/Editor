# Requirements Document

## Introduction

A cross-platform desktop editor application built with C# .NET 10 and Photino.Blazor for the native window host, with React/TypeScript as the UI layer. The application is compiled into a single self-contained executable that embeds all resources using .NET's `ManifestEmbeddedFileProvider`. This initial version focuses on read-only file viewing — opening and displaying file contents without editing capabilities.

### Implementation Notes

- **Photino.Blazor version**: 4.0.13 (not 3.x — older versions have incompatible APIs)
- **No npm/node_modules dependency**: The frontend is part of the C# project. TSX files live in `src/EditorApp/src/` and are compiled to JS by `tsc.js` (the TypeScript compiler, bundled in `src/EditorApp/scripts/`). React and ReactDOM are included as standalone JS files in `wwwroot/js/`. The only external dependency is the Node.js runtime (for running `tsc.js`).
- **TSX compilation**: TSX files use `React.createElement` output (not JSX transform). The .csproj `CompileTypeScript` target runs `node scripts/tsc.js -p tsconfig.json` before every build. Output goes to `wwwroot/js/`.
- **React as standalone scripts**: `react.js` and `react-dom.js` are included as standalone UMD/development builds in `wwwroot/js/`. They are loaded via `<script>` tags in `index.html` and expose `React` and `ReactDOM` as globals.
- **Resource embedding**: All `wwwroot/` files are embedded as `EmbeddedResource` items (not `Content`). The .csproj must set `GenerateEmbeddedFilesManifest=true`, `StaticWebAssetsEnabled=false`, and use `ManifestEmbeddedFileProvider` at runtime. This eliminates the need for a physical `wwwroot/` directory at runtime.
- **Interop direction**: Frontend → Backend uses `window.external.sendMessage(json)`. Backend → Frontend uses `window.external.receiveMessage(callback)` (NOT `window.addEventListener('message')`).
- **Keyboard shortcuts**: Must be handled in exactly one place (the React `keydown` listener in the App component) to avoid duplicate native file dialogs. Do NOT add a second handler in `index.html`.
- **Non-JSON messages**: The Blazor framework sends internal messages (e.g. starting with `_blazor`) through the same web message channel. The MessageRouter must skip messages that don't start with `{`.
- **Single InteropService instance**: The React App component must create one InteropService and use that same instance for both sending requests and receiving responses. Creating separate instances causes callbacks to be registered on a different instance than the one receiving messages.

## Glossary

- **App**: The cross-platform desktop editor application built with Photino.Blazor and React/TypeScript
- **Main_Window**: The primary application window rendered by Photino.Blazor containing the React UI
- **File_Viewer**: The React component responsible for displaying file contents in a read-only view
- **File_Picker**: The native OS file dialog used to select a file for opening
- **Title_Bar**: The area of the Main_Window that displays the application name and currently opened file name
- **Content_Area**: The scrollable region within the Main_Window where file contents are displayed
- **Status_Bar**: The bar at the bottom of the Main_Window displaying file metadata such as file size, encoding, and line count
- **Backend**: The C# .NET 10 Photino.Blazor host responsible for native file system access and interop with the React UI
- **Line_Index**: A data structure maintained by the Backend that maps line numbers to byte offsets in the file, enabling O(1) seeking to any line without loading the entire file
- **Visible_Range**: The range of line numbers currently visible in the Content_Area, determined by the scroll position and viewport height
- **Virtual_Scrollbar**: A generic, reusable custom scrollbar component driven by two abstract numeric parameters (range and position) with no awareness of what the values represent, providing clean separation between external position updates (no events) and user-initiated thumb drags (which calculate and report position)

## Requirements

### Requirement 1: Application Launch

**User Story:** As a user, I want to launch the editor application from a single executable, so that I can use it without installing dependencies or additional files.

#### Acceptance Criteria

1. WHEN the user executes the application binary, THE App SHALL display the Main_Window within 3 seconds
2. WHEN the Main_Window is displayed on launch, THE App SHALL show an empty Content_Area with a prompt message indicating no file is open
3. THE App SHALL run as a single self-contained executable embedding all static resources including the React UI bundle
4. THE App SHALL run on Windows, macOS, and Linux without requiring a separate .NET runtime installation

### Requirement 2: Open File via File Picker

**User Story:** As a user, I want to open a file using a native file dialog, so that I can browse and select a file to view.

#### Acceptance Criteria

1. WHEN the user triggers the open file action, THE App SHALL display the native OS File_Picker dialog
2. WHEN the user selects a file in the File_Picker and confirms, THE Backend SHALL open the file for streamed reading and send file metadata (total line count, file size, encoding) to the File_Viewer
3. WHEN the user cancels the File_Picker dialog, THE App SHALL return to its previous state without changes
4. IF the selected file cannot be read due to permission errors, THEN THE App SHALL display a descriptive error message to the user
5. IF the selected file cannot be read because the file no longer exists, THEN THE App SHALL display a descriptive error message to the user
6. THE App SHALL be able to open files of any size without loading the entire file into memory

### Requirement 3: Display File Contents

**User Story:** As a user, I want to see the contents of the opened file displayed in the application, so that I can read the file.

#### Acceptance Criteria

1. WHEN a file is successfully opened, THE File_Viewer SHALL display the text content visible at the current scroll position in the Content_Area
2. WHEN a file is successfully opened, THE File_Viewer SHALL display line numbers alongside each visible line of text
3. THE File_Viewer SHALL render file contents using a monospaced font
4. THE Content_Area SHALL provide a vertical scrollbar that represents the full extent of the file, allowing the user to scroll to any position in the file
5. WHEN the user scrolls, THE Backend SHALL stream the text content for the newly visible line range and send it to the File_Viewer
6. WHEN the file contents contain lines wider than the visible area, THE Content_Area SHALL provide horizontal scrolling to access the full line width
7. THE File_Viewer SHALL preserve the original line endings and whitespace of the file contents
8. THE Backend SHALL only hold the currently visible portion of the file in memory, not the entire file
9. WHEN the user scrolls horizontally, THE line numbers column SHALL remain fixed (sticky) on the left side and always visible

### Requirement 4: Title Bar File Indication

**User Story:** As a user, I want to see the name of the currently opened file in the window title, so that I know which file I am viewing.

#### Acceptance Criteria

1. WHEN no file is open, THE Title_Bar SHALL display the application name "Editor"
2. WHEN a file is successfully loaded, THE Title_Bar SHALL display the file name followed by a dash and the application name (e.g., "readme.txt - Editor")

### Requirement 5: Status Bar File Metadata

**User Story:** As a user, I want to see metadata about the opened file, so that I can understand basic properties of the file.

#### Acceptance Criteria

1. WHEN a file is successfully loaded, THE Status_Bar SHALL display the file size in human-readable format (bytes, KB, MB)
2. WHEN a file is successfully loaded, THE Status_Bar SHALL display the total number of lines in the file
3. WHEN a file is successfully loaded, THE Status_Bar SHALL display the detected text encoding of the file (e.g., UTF-8, ASCII)
4. WHEN no file is open, THE Status_Bar SHALL display no metadata

### Requirement 6: Streamed File Reading

**User Story:** As a user, I want the application to handle files of any size, so that I can view large log files, data dumps, and other large text files without the application becoming unresponsive.

#### Acceptance Criteria

1. THE App SHALL be able to open files of any size (no upper limit)
2. WHEN a file is opened, THE Backend SHALL perform an initial scan to count total lines and build a line offset index, then send metadata (total line count, file size, encoding) to the UI
3. WHILE the initial scan is in progress, THE App SHALL display a loading indicator
4. WHEN the UI requests a range of lines (e.g. lines 500-550), THE Backend SHALL seek to the correct file offset and read only the requested lines from disk
5. THE Backend SHALL NOT load the entire file into memory at any point
6. THE scrollbar in the Content_Area SHALL reflect the total number of lines in the file, allowing the user to jump to any position

### Requirement 7: Backend-to-UI Interop

**User Story:** As a developer, I want a clear communication channel between the C# backend and the React UI, so that file data flows reliably between native and web layers.

#### Acceptance Criteria

1. WHEN the Backend reads a line range from a file, THE Backend SHALL send the line content and line range metadata to the React UI via Photino's web message interop
2. WHEN the React UI requests a file open action, THE Backend SHALL receive the request via Photino's web message interop and invoke the native File_Picker
3. WHEN the React UI requests a line range (scroll event), THE Backend SHALL receive the request and respond with the requested lines
4. IF the interop message fails to deliver, THEN THE App SHALL display an error message indicating a communication failure

### Requirement 8: Keyboard Shortcut for Open File

**User Story:** As a user, I want to use a keyboard shortcut to open a file, so that I can quickly access the file picker without using a menu or button.

#### Acceptance Criteria

1. WHEN the user presses Ctrl+O (Cmd+O on macOS), THE App SHALL trigger the open file action and display the File_Picker

### Requirement 9: Unit Testing

**User Story:** As a developer, I want comprehensive unit tests for the backend services, so that I can refactor and extend the codebase with confidence.

#### Acceptance Criteria

1. THE FileService SHALL have unit tests covering line offset index building for all line ending types (\n, \r\n, \r, mixed)
2. THE FileService SHALL have unit tests covering ReadLinesAsync for boundary conditions (first lines, middle lines, last lines, beyond end, negative start)
3. THE FileService SHALL have unit tests covering encoding detection for all supported BOM types and the no-BOM fallback
4. THE FileService SHALL have property-based tests (FsCheck) verifying that for any file content, ReadLinesAsync returns the correct lines at any valid offset
5. THE MessageRouter SHALL have unit tests covering message serialization (SendToUIAsync) and deserialization (HandleMessageAsync)
6. THE MessageRouter SHALL have unit tests verifying that non-JSON messages, malformed JSON, and unknown message types are silently ignored
7. THE MessageRouter SHALL have unit tests verifying handler registration, routing, and error handling
8. ALL unit tests SHALL pass on `dotnet test` without requiring a running application or Photino window
9. THE test project SHALL achieve 80%+ code coverage for FileService and MessageRouter

### Requirement 10: Custom Scrollbar

**User Story:** As a user, I want a generic custom scrollbar driven by range and position, so that I can navigate content precisely with a clean, predictable control.

#### Acceptance Criteria

1. THE Virtual_Scrollbar SHALL accept exactly two input parameters: range (a numeric value representing the total scrollable extent) and position (a numeric value representing the current offset within that range)
2. THE Virtual_Scrollbar SHALL display a vertical track and a draggable thumb
3. THE Virtual_Scrollbar SHALL treat range and position as abstract numeric values with no awareness of what they represent (lines, pixels, columns, or any other unit)
4. WHEN the position is updated externally (programmatic update), THE Virtual_Scrollbar SHALL move the thumb to the corresponding location without emitting any position-change event
5. WHEN the user drags the thumb, THE Virtual_Scrollbar SHALL calculate and report a position value relative to the range, the thumb size, and the thumb location within the track
6. WHEN the thumb is at the very top of the track, THE Virtual_Scrollbar SHALL report a position of 0
7. WHEN the thumb is at the very bottom of the track, THE Virtual_Scrollbar SHALL report a position equal to range
8. WHEN the center of the thumb is at the vertical center of the track, THE Virtual_Scrollbar SHALL report a position equal to range / 2
9. THE Virtual_Scrollbar thumb size SHALL be proportional to the viewport size relative to the total range (e.g. if viewport represents 50 units out of 10,000 total, the thumb occupies 0.5% of the track)
10. THE Virtual_Scrollbar SHALL be a self-contained, reusable component with no dependencies on the Content_Area, Backend, or any other application-specific component

