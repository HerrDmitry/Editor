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
2. WHEN the user selects a file in the File_Picker and confirms, THE Backend SHALL read the file contents from disk and send them to the File_Viewer
3. WHEN the user cancels the File_Picker dialog, THE App SHALL return to its previous state without changes
4. IF the selected file cannot be read due to permission errors, THEN THE App SHALL display a descriptive error message to the user
5. IF the selected file cannot be read because the file no longer exists, THEN THE App SHALL display a descriptive error message to the user

### Requirement 3: Display File Contents

**User Story:** As a user, I want to see the contents of the opened file displayed in the application, so that I can read the file.

#### Acceptance Criteria

1. WHEN a file is successfully loaded, THE File_Viewer SHALL display the full text contents of the file in the Content_Area
2. WHEN a file is successfully loaded, THE File_Viewer SHALL display line numbers alongside each line of text
3. THE File_Viewer SHALL render file contents using a monospaced font
4. WHEN the file contents exceed the visible area, THE Content_Area SHALL provide vertical scrolling to access all content
5. WHEN the file contents contain lines wider than the visible area, THE Content_Area SHALL provide horizontal scrolling to access the full line width
6. THE File_Viewer SHALL preserve the original line endings and whitespace of the file contents

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

### Requirement 6: Large File Handling

**User Story:** As a user, I want the application to handle large files gracefully, so that the application remains responsive.

#### Acceptance Criteria

1. WHEN a file larger than 10 MB is selected, THE App SHALL display a warning to the user before loading the file
2. WHILE a file is being loaded, THE App SHALL display a loading indicator in the Content_Area
3. IF a file exceeds 50 MB, THEN THE App SHALL decline to open the file and display a message indicating the file is too large

### Requirement 7: Backend-to-UI Interop

**User Story:** As a developer, I want a clear communication channel between the C# backend and the React UI, so that file data flows reliably between native and web layers.

#### Acceptance Criteria

1. WHEN the Backend reads a file, THE Backend SHALL send the file contents and metadata to the React UI via Photino's web message interop
2. WHEN the React UI requests a file open action, THE Backend SHALL receive the request via Photino's web message interop and invoke the native File_Picker
3. IF the interop message fails to deliver, THEN THE App SHALL display an error message indicating a communication failure

### Requirement 8: Keyboard Shortcut for Open File

**User Story:** As a user, I want to use a keyboard shortcut to open a file, so that I can quickly access the file picker without using a menu or button.

#### Acceptance Criteria

1. WHEN the user presses Ctrl+O (Cmd+O on macOS), THE App SHALL trigger the open file action and display the File_Picker
