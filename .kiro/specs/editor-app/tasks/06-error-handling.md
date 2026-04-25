# Error Handling and Validation

## Overview
Implement comprehensive error handling for file system errors, interop failures, and large file validation.

## Tasks

- [ ] 8. Implement error handling and validation
  - [ ] 8.1 Add backend error handling to FileService
    - Catch `FileNotFoundException` and send `FILE_NOT_FOUND` error
    - Catch `UnauthorizedAccessException` and send `PERMISSION_DENIED` error
    - Catch file size validation failures and send `FILE_TOO_LARGE` error
    - Catch `JsonException` during serialization and send `INTEROP_FAILURE` error
    - Catch generic exceptions and send `UNKNOWN_ERROR` error
    - Add structured logging for all errors with stack traces
    - _Requirements: 2.4, 2.5, 6.3, 7.3_

  - [ ] 8.2 Add frontend error handling to App component
    - Handle `ErrorResponse` messages and update error state
    - Handle `WarningResponse` messages and display warnings
    - Handle interop timeout (5 seconds) and display communication error
    - Clear error state when new file is opened
    - _Requirements: 2.4, 2.5, 6.1, 6.3, 7.3_

  - [ ]* 8.3 Write unit tests for error handling
    - Test backend file not found error handling (Requirement 2.5)
    - Test backend permission denied error handling (Requirement 2.4)
    - Test backend file too large error handling (Requirement 6.3)
    - Test frontend error display
    - Test frontend interop timeout handling (Requirement 7.3)
    - _Requirements: 2.4, 2.5, 6.3, 7.3_

- [ ] 9. Implement large file handling and warnings
  - [ ] 9.1 Add file size validation to FileService
    - Check file size before reading
    - Send `WarningResponse` for files > 10 MB and <= 50 MB
    - Send `ErrorResponse` and reject files > 50 MB
    - _Requirements: 6.1, 6.3_

  - [ ] 9.2 Add warning display to frontend
    - Display warning message for large files (10-50 MB)
    - Allow user to proceed or cancel after warning
    - Display loading indicator while large file is being read
    - _Requirements: 6.1, 6.2_

  - [ ]* 9.3 Write unit tests for large file handling
    - Test warning display for 10-50 MB files
    - Test rejection for files > 50 MB
    - Test loading indicator display during file read
    - _Requirements: 6.1, 6.2, 6.3_
