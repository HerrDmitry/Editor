# Photino Host and Keyboard Shortcuts

## Overview
Implement the Photino window host service and keyboard shortcut handling for the native application window.

## Tasks

- [ ] 4. Implement Photino host and keyboard shortcuts
  - [ ] 4.1 Implement PhotinoHostService
    - Create Photino window with 1200x800 default size, centered, resizable
    - Set initial window title to "Editor"
    - Load embedded React bundle (index.html) into window
    - Register MessageRouter with Photino's `ReceiveMessage` handler
    - Implement `Run()` to start application event loop
    - Implement `Shutdown()` for cleanup
    - _Requirements: 1.1, 1.2, 4.1, 7.2_

  - [ ] 4.2 Implement KeyboardShortcutHandler
    - Register Ctrl+O (Windows/Linux) and Cmd+O (macOS) shortcuts
    - Trigger open file action when shortcut is pressed
    - Integrate with PhotinoHostService
    - _Requirements: 8.1_

  - [ ]* 4.3 Write unit tests for PhotinoHostService initialization
    - Test window creation with correct size and title
    - Test initial state display (empty content area)
    - _Requirements: 1.1, 1.2, 4.1_
