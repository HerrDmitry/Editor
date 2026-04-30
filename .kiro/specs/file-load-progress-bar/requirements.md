# Requirements Document

## Introduction

Add a progress bar to the status bar that shows file-loading progress for large files (>250 KB). Small files (≤250 KB) load without a visible progress indicator. The backend (C# FileService) reports progress during the line-offset scanning phase of `OpenFileAsync`, and the React frontend renders a progress bar inside the existing `StatusBar` component. Communication uses the existing `MessageRouter` / `InteropService` message envelope pattern.

## Glossary

- **File_Service**: The C# backend service (`FileService`) responsible for opening files, building line-offset indices, and reading line ranges.
- **Status_Bar**: The React `StatusBar` component rendered at the bottom of the application window.
- **Progress_Bar**: A horizontal bar element rendered inside the Status_Bar that visually represents file-loading completion percentage.
- **Message_Router**: The C# `MessageRouter` service that serializes messages into `MessageEnvelope` JSON and sends them to the frontend via the Photino web message bridge.
- **Interop_Service**: The TypeScript `InteropService` that receives `MessageEnvelope` messages from the backend and dispatches them to registered callbacks.
- **Large_File**: A file whose size in bytes is strictly greater than 256,000 (250 KB).
- **Small_File**: A file whose size in bytes is less than or equal to 256,000 (250 KB).
- **Size_Threshold**: The constant value 256,000 bytes (250 KB) that separates Small_File from Large_File.
- **Scanning_Phase**: The portion of `FileService.OpenFileAsync` that reads raw bytes to build the line-offset index.

## Requirements

### Requirement 1: Progress Reporting During File Scanning

**User Story:** As a user, I want to see how far along the file loading is, so that I know the application has not frozen when opening a large file.

#### Acceptance Criteria

1. WHILE the Scanning_Phase is in progress for a Large_File, THE File_Service SHALL send progress messages to the Message_Router at a regular interval, where each message contains the percentage of bytes scanned relative to the total file size.
2. WHEN the Scanning_Phase begins for a Large_File, THE File_Service SHALL send an initial progress message with a percentage value of 0.
3. WHEN the Scanning_Phase completes for a Large_File, THE File_Service SHALL send a final progress message with a percentage value of 100.
4. THE File_Service SHALL calculate the progress percentage as `(bytesScannedSoFar / totalFileSize) * 100`, rounded to the nearest integer.
5. THE File_Service SHALL limit progress messages to at most one message per 50 milliseconds to avoid flooding the message bridge.

### Requirement 2: Suppress Progress for Small Files

**User Story:** As a user, I want small files to open instantly without visual clutter, so that the progress bar only appears when it provides useful feedback.

#### Acceptance Criteria

1. WHEN a Small_File is opened, THE File_Service SHALL complete the Scanning_Phase without sending any progress messages.
2. WHEN a Small_File is opened, THE Status_Bar SHALL remain in its default state and not display a Progress_Bar.

### Requirement 3: Progress Message Schema

**User Story:** As a developer, I want a well-defined message type for progress updates, so that the backend and frontend communicate progress consistently.

#### Acceptance Criteria

1. THE Message_Router SHALL transmit progress messages using a `FileLoadProgressMessage` type that implements the `IMessage` interface.
2. THE `FileLoadProgressMessage` SHALL contain a `fileName` field (string) identifying the file being loaded.
3. THE `FileLoadProgressMessage` SHALL contain a `percent` field (integer, 0–100) representing the scanning completion percentage.
4. THE `FileLoadProgressMessage` SHALL contain a `fileSizeBytes` field (long) representing the total size of the file.

### Requirement 4: Status Bar Progress Bar Display

**User Story:** As a user, I want to see a clear visual indicator of loading progress in the status bar, so that I can estimate how long the load will take.

#### Acceptance Criteria

1. WHEN a `FileLoadProgressMessage` with a percent value less than 100 is received, THE Status_Bar SHALL display a Progress_Bar showing the reported percentage.
2. THE Progress_Bar SHALL render as a horizontal bar within the Status_Bar, filling from left to right proportionally to the percent value.
3. THE Progress_Bar SHALL display the current percentage as text (e.g., "Loading: 42%").
4. WHEN a `FileLoadProgressMessage` with a percent value of 100 is received, THE Status_Bar SHALL hide the Progress_Bar and display the normal file metadata items.
5. THE Progress_Bar SHALL be accessible, using `role="progressbar"`, `aria-valuenow`, `aria-valuemin="0"`, and `aria-valuemax="100"` attributes.

### Requirement 5: Progress Bar Styling

**User Story:** As a user, I want the progress bar to match the editor's visual theme, so that it looks like a natural part of the application.

#### Acceptance Criteria

1. THE Progress_Bar background SHALL use a color consistent with the Status_Bar background (`#007acc`).
2. THE Progress_Bar filled portion SHALL use a lighter or contrasting color that is visible against the Status_Bar background.
3. THE Progress_Bar text SHALL use white (`#ffffff`) to match existing Status_Bar text.
4. THE Progress_Bar height SHALL not exceed the Status_Bar height (22px minimum height).

### Requirement 6: Interop Service Progress Callback

**User Story:** As a developer, I want the Interop_Service to support progress message callbacks, so that React components can react to progress updates.

#### Acceptance Criteria

1. THE Interop_Service SHALL provide an `onFileLoadProgress` callback registration method that accepts a function receiving `FileLoadProgressMessage` payload data.
2. WHEN a `MessageEnvelope` with type `FileLoadProgressMessage` is received, THE Interop_Service SHALL invoke all registered `onFileLoadProgress` callbacks with the payload.
3. WHEN the Interop_Service is disposed, THE Interop_Service SHALL clear all registered `onFileLoadProgress` callbacks.

### Requirement 7: Error During Large File Load

**User Story:** As a user, I want the progress bar to disappear if an error occurs during loading, so that I am not left with a stale progress indicator.

#### Acceptance Criteria

1. IF an error occurs during the Scanning_Phase of a Large_File, THEN THE File_Service SHALL stop sending progress messages.
2. IF an error occurs while a Progress_Bar is displayed, THEN THE Status_Bar SHALL hide the Progress_Bar.
3. IF an error occurs during the Scanning_Phase, THEN THE File_Service SHALL propagate the error through the existing error handling path (ErrorResponse message).

### Requirement 8: Size Threshold Constant

**User Story:** As a developer, I want the size threshold to be defined as a named constant, so that it can be adjusted without searching through code.

#### Acceptance Criteria

1. THE File_Service SHALL define the Size_Threshold as a named constant with the value 256,000 bytes.
2. THE File_Service SHALL use the Size_Threshold constant when determining whether to send progress messages for a file.

### Requirement 9: Graceful Cancellation on File Switch

**User Story:** As a user, I want the current file load to be cancelled gracefully when I open a new file, so that I am not blocked by a previous load and the application remains responsive.

#### Acceptance Criteria

1. WHEN the user opens a new file WHILE the Scanning_Phase is in progress for a Large_File, THE File_Service SHALL cancel the in-progress Scanning_Phase before starting the new file load.
2. WHEN the Scanning_Phase is cancelled due to a new file being opened, THE File_Service SHALL stop sending progress messages for the cancelled file.
3. WHEN the Scanning_Phase is cancelled due to a new file being opened, THE Status_Bar SHALL hide the Progress_Bar for the cancelled file.
4. IF the Scanning_Phase is cancelled, THEN THE File_Service SHALL release all resources (file handles, buffers) associated with the cancelled scan without raising unhandled exceptions.
5. WHEN the cancelled Scanning_Phase has completed its cleanup, THE File_Service SHALL proceed to open the newly requested file using the normal `OpenFileAsync` flow.
6. IF the newly requested file is a Large_File, THEN THE Status_Bar SHALL display a new Progress_Bar for the new file load.
