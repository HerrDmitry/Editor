# Streamed File Reading and Virtual Scrolling

## Overview
Replace the current whole-file-into-memory approach with streamed reading using a line offset index. The backend scans the file once to build an index, then serves line ranges on demand. The frontend uses virtual scrolling to display only visible lines while the scrollbar represents the full file.

## Tasks

- [x] 1. Update backend data models and message types
  - [x] 1.1 Add new data models
    - Add `FileOpenMetadata` record: FilePath, FileName, TotalLines, FileSizeBytes, Encoding
    - Add `LinesResult` record: StartLine, Lines (string[]), TotalLines
    - Add `RequestLinesMessage` class (implements IMessage): StartLine, LineCount
    - Add `FileOpenedResponse` class (implements IMessage): FileName, TotalLines, FileSizeBytes, Encoding
    - Add `LinesResponse` class (implements IMessage): StartLine, Lines (string[]), TotalLines
  - [x] 1.2 Remove obsolete models
    - Remove `FileLoadedResponse` (replaced by FileOpenedResponse + LinesResponse)
    - Remove `WarningResponse` (no more file size warnings)
    - Remove `FileContent` record (no longer loading full file)
    - Remove `FILE_TOO_LARGE` from ErrorCode enum

- [x] 2. Rewrite FileService for streamed reading
  - [x] 2.1 Implement OpenFileAsync with line offset index
    - Scan the file from start to end, recording byte offset of each line start into a `List<long>`
    - Count total lines during the scan
    - Detect encoding via BOM detection (fallback to UTF-8)
    - Store the line index in a dictionary keyed by file path (for later ReadLinesAsync calls)
    - Return FileOpenMetadata (totalLines, fileSize, encoding, fileName)
    - Remove ValidateFileSize method and the 10MB/50MB limits
  - [x] 2.2 Implement ReadLinesAsync
    - Look up byte offset for startLine in the line offset index
    - Open a FileStream and seek to that offset
    - Read lineCount lines using a StreamReader
    - Clamp lineCount to not exceed totalLines - startLine
    - Return LinesResult (startLine, lines array, totalLines)
  - [x] 2.3 Remove old ReadFileAsync and GetFileMetadataAsync methods
    - These loaded the entire file into memory — no longer needed

- [x] 3. Update PhotinoHostService message handlers
  - [x] 3.1 Update HandleOpenFileRequestAsync
    - After file picker selection, call FileService.OpenFileAsync (not ReadFileAsync)
    - Send FileOpenedResponse (metadata only) instead of FileLoadedResponse
    - Remove the WarningResponse for large files
    - Remove the file size validation check
  - [x] 3.2 Add HandleRequestLinesAsync
    - Register handler for RequestLinesMessage
    - Call FileService.ReadLinesAsync with the requested startLine and lineCount
    - Send LinesResponse back to the UI
    - Handle errors (file not found, seek errors) and send ErrorResponse

- [x] 4. Update frontend InteropService
  - [x] 4.1 Add new message types and methods
    - Add `sendRequestLines(startLine, lineCount)` method
    - Add `onFileOpened(callback)` handler (for FileOpenedResponse)
    - Add `onLinesResponse(callback)` handler (for LinesResponse)
    - Update MessageTypes constants: add FileOpenedResponse, RequestLinesMessage, LinesResponse
  - [x] 4.2 Remove obsolete handlers
    - Remove `onFileLoaded` (replaced by onFileOpened + onLinesResponse)
    - Remove `onWarning` (no more file size warnings)

- [x] 5. Update frontend components for virtual scrolling
  - [x] 5.1 Update App.tsx state management
    - Replace `fileContent` state with `fileMeta` (FileMeta | null)
    - Add `lines` state (string[] | null) for currently visible lines
    - Add `linesStartLine` state (number) for the start line of the current buffer
    - Remove `warning` state (no more file size warnings)
    - Wire up onFileOpened → set fileMeta, request initial lines
    - Wire up onLinesResponse → set lines and linesStartLine
    - Add handleRequestLines callback that calls interop.sendRequestLines
  - [x] 5.2 Rewrite ContentArea.tsx with virtual scrolling
    - Add `onRequestLines` prop: (startLine: number, lineCount: number) => void
    - Add `fileMeta` prop (for totalLines)
    - Add `linesStartLine` prop (for positioning visible lines)
    - Implement virtual scroll container:
      - Outer div with overflow-y: auto, overflow-x: auto
      - Spacer div with height = totalLines × lineHeight
      - Visible lines div positioned at linesStartLine × lineHeight
    - Implement scroll handler:
      - Calculate startLine = Math.floor(scrollTop / lineHeight)
      - Calculate lineCount = Math.ceil(containerHeight / lineHeight) + buffer
      - Call onRequestLines when visible range changes
    - Line numbers: display linesStartLine + index + 1 (1-based)
    - Use a fixed lineHeight constant (e.g. 20px) for consistent calculations
  - [x] 5.3 Update StatusBar.tsx
    - Accept FileMeta instead of FileMetadata
    - Display totalLines, fileSizeBytes, encoding from FileMeta
  - [x] 5.4 Update index.html and CSS
    - Update CSS for virtual scroll container styles
    - Ensure fixed line height in CSS matches the JS constant

- [x] 6. Verify the migration
  - Run `dotnet build` and verify TypeScript compiles without errors
  - Run the app and verify:
    - Small files open and display correctly
    - Scrolling loads new line ranges
    - Line numbers are correct at all scroll positions
    - Scrollbar represents the full file extent
    - Title bar and status bar update correctly
    - Error handling still works (try opening non-existent file)
  - Test with a large file (100MB+) to verify memory stays bounded

## Notes

- The line offset index costs ~8 bytes per line (a List<long>). For a 10-million-line file, that's ~80MB of index memory. This is acceptable for the initial version.
- The scroll handler should debounce or throttle requests to avoid flooding the backend with RequestLinesMessage during fast scrolling.
- Consider requesting a buffer of lines beyond the visible range (e.g. 2× the viewport) to reduce flicker during scrolling.
