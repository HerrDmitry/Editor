# Requirements Document

## Introduction

When a file currently open in the editor is modified by an external process, the application must detect the change and transparently refresh its internal state (line index, cache, displayed content) so the user sees up-to-date content without errors, scroll jumps, or visual disruption. The current behavior — throwing `InvalidOperationException("File has been modified since it was opened")` — must be replaced with automatic, silent re-indexing and content refresh.

## Glossary

- **File_Watcher**: Backend component that monitors the currently open file for external modifications using OS file-system notifications.
- **File_Service**: Backend service (`FileService.cs`) responsible for file I/O, line-index construction, caching, and line reading.
- **Host_Service**: Backend orchestrator (`PhotinoHostService.cs`) that coordinates file operations and message routing between backend and frontend.
- **Message_Router**: Backend service (`MessageRouter.cs`) that routes JSON messages between C# backend and React frontend via the Photino web message bridge.
- **Content_Area**: Frontend virtual-scroll component (`ContentArea.tsx`) that renders a sliding window of lines.
- **App_Component**: Root React component (`App.tsx`) managing file state, line buffers, and interop callbacks.
- **Interop_Service**: Frontend message bridge (`InteropService.ts`) for backend communication.
- **Line_Index**: Compressed byte-offset index (`CompressedLineIndex`) mapping line numbers to file positions.
- **Cache_Entry**: Record storing `(CompressedLineIndex, Encoding, FileSize, LastWriteTimeUtc)` for an opened file.
- **Visible_Range**: The set of logical line numbers currently rendered in the viewport DOM.
- **Buffer_Window**: The sliding window of up to 400 lines held in frontend memory.
- **Stale_Detection**: Current mechanism in `ReadLinesAsync` that compares `FileSize` and `LastWriteTimeUtc` against the cached values.
- **Refresh_Cycle**: The complete sequence: detect external change → re-index file → update cache → notify frontend → refresh displayed content.

## Requirements

### Requirement 1: File System Monitoring

**User Story:** As a user, I want the application to automatically detect when the currently open file is modified by another process, so that I do not have to manually reopen the file.

#### Acceptance Criteria

1. WHEN a file is opened via File_Service, THE File_Watcher SHALL begin monitoring that file path for write events.
2. WHEN the currently monitored file is written to by an external process, THE File_Watcher SHALL notify Host_Service within 1 second of the OS reporting the change.
3. WHEN a different file is opened, THE File_Watcher SHALL stop monitoring the previously opened file and begin monitoring the new file.
4. WHEN the currently open file is closed, THE File_Watcher SHALL stop monitoring that file path.
5. IF the file system does not support change notifications for the monitored path, THEN THE File_Watcher SHALL fall back to periodic polling at an interval no greater than 2 seconds.

### Requirement 2: Debouncing Rapid Changes

**User Story:** As a user, I want the application to handle rapid successive file writes gracefully, so that the editor does not perform redundant refresh cycles.

#### Acceptance Criteria

1. WHEN the File_Watcher receives multiple change notifications within a 500-millisecond window, THE Host_Service SHALL perform only one Refresh_Cycle using the final file state.
2. WHILE a Refresh_Cycle is in progress, IF a new change notification arrives for the same file, THEN THE Host_Service SHALL NOT cancel the in-progress Refresh_Cycle. THE Host_Service SHALL mark that a subsequent refresh is pending and start a new Refresh_Cycle after the current one completes and the debounce window elapses.

### Requirement 3: Silent Re-Indexing

**User Story:** As a user, I want the file content to be re-indexed automatically after an external modification, so that line offsets and total line count remain accurate.

#### Acceptance Criteria

1. WHEN a debounced change notification is received, THE File_Service SHALL rebuild the Line_Index for the modified file by re-scanning its bytes.
2. WHEN re-indexing completes, THE File_Service SHALL update the Cache_Entry with the new Line_Index, FileSize, LastWriteTimeUtc, and Encoding, and SHALL dispose the previous Line_Index to free memory.
3. WHILE re-indexing is in progress, THE File_Service SHALL continue serving read requests using the previous Cache_Entry so that the user experiences no interruption.
4. IF the file is deleted or becomes inaccessible during re-indexing, THEN THE File_Service SHALL send an ErrorResponse with error code FILE_NOT_FOUND to the frontend via Message_Router.
5. THE File_Service SHALL open files with shared read-write access (FileShare.ReadWrite) so that reading does not block or fail while another process is writing to the file.
6. IF the file has not actually changed since the last Cache_Entry (same FileSize and LastWriteTimeUtc), THEN THE File_Service SHALL skip re-scanning and return the existing metadata.

### Requirement 4: Frontend Notification

**User Story:** As a user, I want the application to inform the frontend of updated file metadata after a refresh, so that the UI reflects the current file state.

#### Acceptance Criteria

1. WHEN re-indexing completes successfully, THE Host_Service SHALL send a FileOpenedResponse message to the frontend with updated totalLines, fileSizeBytes, and encoding, and with isPartial set to false.
2. THE FileOpenedResponse sent after a refresh SHALL include a field indicating that the message is a refresh rather than a new file open, so that App_Component can distinguish the two cases.

### Requirement 5: Scroll Position Preservation

**User Story:** As a user, I want my scroll position to remain unchanged after an external file modification, so that I do not lose my place in the file.

#### Acceptance Criteria

1. WHEN App_Component receives a refresh FileOpenedResponse, THE App_Component SHALL preserve the current linesStartLine and scroll offset values.
2. WHEN App_Component receives a refresh FileOpenedResponse, THE App_Component SHALL re-request lines from the backend using a buffer size of at least APP_FETCH_SIZE (200) lines, even if the current buffer is smaller, to ensure growing files display new content.
3. WHEN Content_Area receives refreshed lines for the current Buffer_Window, THE Content_Area SHALL replace the buffer content without altering the viewport scrollTop position.
4. IF the file has shrunk such that the current linesStartLine exceeds the new totalLines, THEN THE App_Component SHALL clamp linesStartLine to `max(0, newTotalLines - bufferLength)` and adjust the scroll position to the end of the file.

### Requirement 6: Visible Range Refresh

**User Story:** As a user, I want to see updated content immediately if the externally modified portion of the file is within my current viewport, so that I always view accurate data.

#### Acceptance Criteria

1. WHEN refreshed lines are received for the current Buffer_Window, THE Content_Area SHALL render the new line content in place of the old content for all lines within the Visible_Range.
2. WHEN the refreshed content causes line heights to change within the Visible_Range, THE Content_Area SHALL adjust the container layout without changing the viewport scrollTop, preventing visible scroll jumps.

### Requirement 7: Removal of Stale File Error

**User Story:** As a user, I want the application to stop showing "File has been modified" errors, so that external edits do not disrupt my workflow.

#### Acceptance Criteria

1. WHEN ReadLinesAsync detects that the file's FileSize or LastWriteTimeUtc differs from the Cache_Entry, THE File_Service SHALL trigger a Refresh_Cycle instead of throwing InvalidOperationException.
2. WHILE a Refresh_Cycle triggered by Stale_Detection is in progress, THE File_Service SHALL queue the pending read request and fulfill it after re-indexing completes with the updated Line_Index.
3. THE File_Service SHALL remove the code path that throws InvalidOperationException for stale file detection.

### Requirement 8: Cancellation Safety

**User Story:** As a user, I want file refresh operations to be cancellable when switching files or closing the app, so that opening a new file or shutting down while a refresh is in progress does not cause errors or prevent exit, while same-file change notifications never interrupt an in-progress refresh.

#### Acceptance Criteria

1. WHEN a new file is opened while a Refresh_Cycle is in progress for a different file, THE Host_Service SHALL cancel the in-progress Refresh_Cycle via CancellationToken.
2. WHILE a Refresh_Cycle is in progress for the current file, IF a change notification arrives for the same file, THEN THE Host_Service SHALL NOT cancel the in-progress Refresh_Cycle.
3. WHEN a Refresh_Cycle is cancelled due to a new file being opened, THE Host_Service SHALL not send any FileOpenedResponse or ErrorResponse for the cancelled refresh to the frontend.
4. THE Host_Service SHALL use a dedicated CancellationTokenSource for refresh operations (`_refreshCts`), separate from the file-open CancellationTokenSource (`_scanCts`), to prevent interference between file-open and refresh operations.
5. THE Host_Service SHALL maintain a master shutdown CancellationTokenSource (`_shutdownCts`) that is cancelled during Shutdown, and all refresh CancellationTokenSources SHALL be linked to it so that in-flight refresh tasks are cancelled on application exit.
6. WHEN the application is shutting down, THE Host_Service SHALL cancel all in-progress operations (scan, refresh, debounce timers) and dispose all resources to ensure the process exits cleanly.
