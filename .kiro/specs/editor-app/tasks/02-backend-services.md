# Backend Core Services

## Overview
Implement the C# backend services including data models, FileService, and MessageRouter for interop communication.

## Tasks

- [ ] 2. Implement backend core services
  - [ ] 2.1 Create data models and message schemas
    - Define `FileContent`, `FileMetadata`, `FileOpenResult` records in C#
    - Define `MessageEnvelope`, `FileLoadedResponse`, `ErrorResponse`, `WarningResponse` classes
    - Define error codes enum: `FILE_NOT_FOUND`, `PERMISSION_DENIED`, `FILE_TOO_LARGE`, `INTEROP_FAILURE`, `UNKNOWN_ERROR`
    - _Requirements: 2.2, 2.4, 2.5, 6.1, 6.3, 7.1_

  - [ ]* 2.2 Write property test for message structure
    - **Property 9: Interop message structure correctness**
    - **Validates: Requirements 7.1**
    - Use FsCheck to verify all messages conform to MessageEnvelope schema
    - _Requirements: 7.1_

  - [ ] 2.3 Implement FileService for file system operations
    - Implement `OpenFileDialogAsync()` to display native file picker
    - Implement `ReadFileAsync(string filePath)` with encoding detection
    - Implement `GetFileMetadataAsync(string filePath)` to extract size, line count, encoding, last modified
    - Implement `ValidateFileSize(long fileSize)` with 10 MB warning and 50 MB rejection thresholds
    - Implement encoding detection using BOM detection with UTF-8 fallback
    - _Requirements: 2.1, 2.2, 5.1, 5.2, 5.3, 6.1, 6.3_

  - [ ]* 2.4 Write property tests for FileService
    - **Property 1: Content preservation through load-and-display pipeline**
    - **Validates: Requirements 2.2, 3.1, 3.6**
    - Use FsCheck to generate random file content and verify preservation
    - **Property 6: Line count accuracy**
    - **Validates: Requirements 5.2**
    - Use FsCheck to verify line count matches actual lines in file
    - **Property 8: File size validation thresholds**
    - **Validates: Requirements 6.1, 6.3**
    - Use FsCheck to verify correct warning/rejection behavior for different file sizes
    - _Requirements: 2.2, 3.1, 3.6, 5.2, 6.1, 6.3_

  - [ ]* 2.5 Write unit tests for FileService error handling
    - Test file not found error (Requirement 2.5)
    - Test permission denied error (Requirement 2.4)
    - Test file too large rejection (Requirement 6.3)
    - Test encoding detection for UTF-8, UTF-16, ASCII files
    - _Requirements: 2.4, 2.5, 6.3_

  - [ ] 2.6 Implement MessageRouter for interop communication
    - Implement `RegisterHandler<TRequest>(Func<TRequest, Task> handler)` for incoming messages
    - Implement `SendToUIAsync<TMessage>(TMessage message)` for outgoing messages
    - Implement JSON serialization/deserialization with error handling
    - Implement `StartListening()` to register Photino message handler
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ]* 2.7 Write unit tests for MessageRouter
    - Test message serialization and deserialization
    - Test handler registration and routing
    - Test error handling for malformed JSON
    - _Requirements: 7.1, 7.2, 7.3_

- [ ] 3. Checkpoint - Ensure backend services compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.
