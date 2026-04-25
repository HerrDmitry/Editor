# Frontend Data Models and Interop Service

## Overview
Implement TypeScript interfaces and the InteropService for frontend-backend communication.

## Tasks

- [ ] 5. Implement frontend data models and interop service
  - [ ] 5.1 Create TypeScript interfaces and types
    - Define `FileContent`, `FileMetadata`, `ErrorInfo`, `WarningInfo` interfaces
    - Define `MessageEnvelope` interface matching backend schema
    - Define message type constants
    - _Requirements: 7.1_

  - [ ] 5.2 Implement InteropService for frontend-backend communication
    - Implement `sendOpenFileRequest()` using `window.external.sendMessage`
    - Implement `onFileLoaded(callback)` to listen for FileLoadedResponse messages
    - Implement `onError(callback)` to listen for ErrorResponse messages
    - Implement `onWarning(callback)` to listen for WarningResponse messages
    - Add message event listener for `window.addEventListener('message')`
    - Add timeout handling (5 seconds) for interop failures
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ]* 5.3 Write unit tests for InteropService
    - Test message sending via `window.external.sendMessage`
    - Test message receiving via `window.addEventListener`
    - Test timeout handling for unresponsive backend
    - _Requirements: 7.2, 7.3_
