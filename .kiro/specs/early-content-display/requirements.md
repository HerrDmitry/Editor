# Requirements Document

## Introduction

When opening large files (>250 KB), the editor currently blocks the UI until the entire file scan completes — the user sees only a progress bar while the backend builds the full line-offset index. This feature changes that behavior: after scanning the first ~250 KB of a large file, the backend sends a **partial** `FileOpenedResponse` with the line count known so far, allowing the UI to display content immediately. The scan continues in the background, sending progress updates. When the scan completes, the backend sends a **final** `FileOpenedResponse` with the complete line count, and the UI updates the scrollbar range accordingly.

Small files (≤250 KB) are unaffected — they complete scanning instantly and behave as before.

## Glossary

- **File_Service**: The C# backend service (`FileService`) responsible for opening files, building line-offset indices, and reading line ranges.
- **Photino_Host_Service**: The C# service (`PhotinoHostService`) that orchestrates file-open requests, wires progress reporting, and sends messages to the UI via the Message_Router.
- **Message_Router**: The C# `MessageRouter` service that serializes messages into `MessageEnvelope` JSON and sends them to the frontend via the Photino web message bridge.
- **Interop_Service**: The TypeScript `InteropService` that receives `MessageEnvelope` messages from the backend and dispatches them to registered callbacks.
- **Content_Area**: The React component that renders file lines using a sliding-window buffer and a custom scrollbar.
- **Status_Bar**: The React `StatusBar` component rendered at the bottom of the application window, showing file metadata and the progress bar.
- **App_Component**: The root React `App` component that manages application state and coordinates between Interop_Service, Content_Area, and Status_Bar.
- **Line_Offset_Index**: The in-memory data structure (`_lineIndexCache`) mapping line numbers to byte offsets, built during the scanning phase.
- **Partial_Metadata**: A `FileOpenedResponse` sent after scanning the first ~250 KB of a large file, containing the provisional line count known so far and a flag indicating the scan is still in progress.
- **Final_Metadata**: A `FileOpenedResponse` sent after the full scan completes, containing the definitive total line count and a flag indicating the scan is complete.
- **Large_File**: A file whose size in bytes is strictly greater than 256,000 (the existing `SizeThresholdBytes` constant).
- **Small_File**: A file whose size in bytes is less than or equal to 256,000.
- **Custom_Scrollbar**: The reusable `CustomScrollbar` React component driven by `range` and `position` props.
- **Scanning_Phase**: The portion of `FileService.OpenFileAsync` that reads raw bytes to build the Line_Offset_Index.

## Requirements

### Requirement 1: Partial Metadata Emission After Initial Scan Threshold

**User Story:** As a user, I want to see file content as soon as the first 250 KB are scanned, so that I do not have to wait for the entire file to be indexed before I can start reading.

#### Acceptance Criteria

1. WHEN the Scanning_Phase has processed at least 256,000 bytes of a Large_File, THE File_Service SHALL make the partially built Line_Offset_Index available for read operations covering the already-indexed lines.
2. WHEN the Scanning_Phase has processed at least 256,000 bytes of a Large_File, THE Photino_Host_Service SHALL send a Partial_Metadata message to the UI containing the provisional line count, the file size, the detected encoding, and a flag (`isPartial: true`) indicating the scan is still in progress.
3. WHEN a Small_File is opened, THE Photino_Host_Service SHALL send a single Final_Metadata message after the scan completes, with no partial metadata emitted.

### Requirement 2: Continued Scanning After Partial Metadata

**User Story:** As a user, I want the file scan to continue in the background after the initial content is displayed, so that the full file becomes navigable without requiring a second action.

#### Acceptance Criteria

1. AFTER the Partial_Metadata is sent for a Large_File, THE File_Service SHALL continue the Scanning_Phase for the remainder of the file without blocking the UI.
2. WHILE the Scanning_Phase continues after Partial_Metadata emission, THE File_Service SHALL continue sending progress messages (percent updates) to the Status_Bar via the existing `FileLoadProgressMessage` mechanism.
3. WHEN the Scanning_Phase completes for a Large_File, THE Photino_Host_Service SHALL send a Final_Metadata message containing the definitive total line count and a flag (`isPartial: false`) indicating the scan is complete.

### Requirement 3: Partial Line Index Readability

**User Story:** As a developer, I want `ReadLinesAsync` to work with a partially built line index, so that the UI can fetch and display lines before the full scan completes.

#### Acceptance Criteria

1. WHILE the Scanning_Phase is in progress, THE File_Service SHALL allow `ReadLinesAsync` calls for any line range that falls within the already-indexed portion of the Line_Offset_Index.
2. WHILE the Scanning_Phase is in progress, THE File_Service SHALL use the current count of indexed lines as the `totalLines` value in `LinesResult` responses.
3. IF a `ReadLinesAsync` request includes lines beyond the currently indexed range, THEN THE File_Service SHALL return only the lines that have been indexed so far, clamping the range to the available index.

### Requirement 4: FileOpenedResponse Schema Extension

**User Story:** As a developer, I want the `FileOpenedResponse` message to indicate whether the metadata is partial or final, so that the frontend can distinguish between provisional and definitive line counts.

#### Acceptance Criteria

1. THE `FileOpenedResponse` message SHALL contain an `isPartial` field (boolean) that is `true` when the metadata represents a partial scan result and `false` when the scan is complete.
2. WHEN `isPartial` is `true`, THE `FileOpenedResponse` SHALL contain the `totalLines` value representing the number of lines indexed so far (provisional count).
3. WHEN `isPartial` is `false`, THE `FileOpenedResponse` SHALL contain the `totalLines` value representing the definitive total line count of the entire file.

### Requirement 5: UI Immediate Content Display on Partial Metadata

**User Story:** As a user, I want to see file content rendered immediately when partial metadata arrives, so that I can start reading without waiting for the full scan.

#### Acceptance Criteria

1. WHEN a Partial_Metadata message is received, THE App_Component SHALL set the file metadata state with the provisional line count and request initial lines from the backend.
2. WHEN a Partial_Metadata message is received, THE Content_Area SHALL render the available lines and display the Custom_Scrollbar with the provisional total line count as the range.
3. WHILE the scan is in progress (after Partial_Metadata), THE Status_Bar SHALL continue displaying the progress bar showing scan completion percentage.

### Requirement 6: Scrollbar Range Update on Final Metadata

**User Story:** As a user, I want the scrollbar to update its range when the full scan completes, so that I can navigate the entire file.

#### Acceptance Criteria

1. WHEN a Final_Metadata message is received, THE App_Component SHALL update the file metadata state with the definitive total line count.
2. WHEN the total line count changes from the provisional to the definitive value, THE Custom_Scrollbar range prop SHALL be updated to reflect the new total line count.
3. WHEN the total line count is updated, THE Content_Area SHALL preserve the current scroll position and visible lines without visual disruption.

### Requirement 7: Progress Bar Behavior During Early Content Display

**User Story:** As a user, I want the progress bar to remain visible while the scan continues after content is displayed, so that I know the file is still being indexed.

#### Acceptance Criteria

1. WHILE the Scanning_Phase is in progress after Partial_Metadata emission, THE Status_Bar SHALL display the progress bar alongside the file metadata items (not replacing them).
2. WHEN the Scanning_Phase completes (Final_Metadata received and progress reaches 100%), THE Status_Bar SHALL hide the progress bar and display only the final file metadata.

### Requirement 8: Thread Safety for Partial Index Access

**User Story:** As a developer, I want concurrent access to the line offset index to be safe, so that `ReadLinesAsync` calls do not corrupt or crash while the scanning thread is still appending to the index.

#### Acceptance Criteria

1. WHILE the Scanning_Phase is appending entries to the Line_Offset_Index, THE File_Service SHALL ensure that concurrent `ReadLinesAsync` calls read a consistent snapshot of the index without data corruption.
2. THE File_Service SHALL use a thread-safe synchronization mechanism (such as a lock or concurrent collection) to protect access to the Line_Offset_Index during concurrent read and write operations.

### Requirement 9: Cancellation During Partial Display

**User Story:** As a user, I want to open a new file while the current large file is still scanning, so that I am not blocked by the ongoing scan.

#### Acceptance Criteria

1. WHEN the user opens a new file WHILE the Scanning_Phase is in progress (after or before Partial_Metadata emission), THE File_Service SHALL cancel the in-progress scan and release all associated resources.
2. WHEN the scan is cancelled, THE App_Component SHALL clear the partial file state (metadata, lines, progress) and proceed with the new file open flow.
3. IF the scan is cancelled after Partial_Metadata was sent, THEN THE App_Component SHALL replace the partially displayed content with the new file's content when it becomes available.

### Requirement 10: Notification Mechanism for Partial Metadata

**User Story:** As a developer, I want a clean callback mechanism for the File_Service to signal when partial metadata is ready, so that the Photino_Host_Service can send it to the UI without tight coupling.

#### Acceptance Criteria

1. THE `OpenFileAsync` method SHALL accept an optional callback or event parameter that is invoked when the partial scan threshold is reached, providing the provisional `FileOpenMetadata`.
2. THE callback SHALL be invoked at most once per file open operation (when the 256,000-byte threshold is first crossed).
3. WHEN the callback is invoked, THE File_Service SHALL have already made the partial Line_Offset_Index available for `ReadLinesAsync` calls.
