# Integration and End-to-End Testing

## Overview
Wire backend and frontend together, test end-to-end flows, and validate cross-platform compatibility.

## Tasks

- [x] 10. Wire backend and frontend together
  - [x] 10.1 Connect MessageRouter to PhotinoHostService
    - Register MessageRouter handlers for `OpenFileRequest`
    - Wire FileService to MessageRouter for sending responses
    - Test message flow: React → Backend → React
    - _Requirements: 7.1, 7.2_

  - [x] 10.2 Connect InteropService to App component
    - Wire `sendOpenFileRequest` to open file button/shortcut
    - Wire `onFileLoaded` callback to update App state
    - Wire `onError` callback to update App error state
    - Wire `onWarning` callback to display warnings
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 10.3 Test end-to-end file open flow
    - Trigger open file action from React UI
    - Verify native file picker appears
    - Select file and verify content loads
    - Verify title bar, content area, and status bar update correctly
    - _Requirements: 2.1, 2.2, 3.1, 4.2, 5.1, 5.2, 5.3_

- [x] 11. Checkpoint - Ensure end-to-end integration works
  - Ensure all tests pass, ask the user if questions arise.

- [ ]* 12. Write integration tests for cross-platform compatibility
  - Test application startup on Windows, macOS, Linux (Requirement 1.4)
  - Test native file picker on each platform (Requirement 2.1)
  - Test keyboard shortcuts on each platform (Ctrl+O vs Cmd+O) (Requirement 8.1)
  - Test single executable deployment on each platform (Requirement 1.3)
  - _Requirements: 1.3, 1.4, 2.1, 8.1_

- [ ]* 13. Write integration tests for interop communication
  - Test message delivery from React to C# backend (Requirement 7.2)
  - Test message delivery from C# backend to React (Requirement 7.1)
  - Test interop failure handling (Requirement 7.3)
  - _Requirements: 7.1, 7.2, 7.3_
