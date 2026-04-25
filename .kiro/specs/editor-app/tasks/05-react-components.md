# React UI Components

## Overview
Implement the React components for the user interface including App, TitleBar, ContentArea, and StatusBar.

## Tasks

- [ ] 6. Implement React UI components
  - [ ] 6.1 Implement App component (root component)
    - Create state management for `fileContent`, `isLoading`, `error`, `titleBarText`
    - Implement layout structure: TitleBar, ContentArea, StatusBar
    - Connect InteropService callbacks to state updates
    - Implement open file action handler
    - Handle keyboard shortcut trigger from backend
    - _Requirements: 1.2, 2.3, 4.1, 4.2, 8.1_

  - [ ]* 6.2 Write property test for dialog cancellation
    - **Property 2: Dialog cancellation idempotence**
    - **Validates: Requirements 2.3**
    - Use fast-check to verify state preservation on dialog cancel
    - **Property 10: Cancellation state preservation**
    - **Validates: Requirements 2.3**
    - Use fast-check to verify previously loaded file remains on cancel
    - _Requirements: 2.3_

  - [ ]* 6.3 Write unit tests for App component
    - Test initial state display (empty content area with prompt)
    - Test loading state display
    - Test error state display
    - Test file loaded state display
    - _Requirements: 1.2, 6.2_

  - [ ] 6.4 Implement TitleBar component
    - Accept `title` prop
    - Display "Editor" when no file is open
    - Display "{fileName} - Editor" when file is loaded
    - _Requirements: 4.1, 4.2_

  - [ ]* 6.5 Write property test for TitleBar formatting
    - **Property 4: Title bar format consistency**
    - **Validates: Requirements 4.2**
    - Use fast-check to verify title format for any file name
    - _Requirements: 4.2_

  - [ ]* 6.6 Write unit tests for TitleBar component
    - Test empty state title display (Requirement 4.1)
    - Test file loaded title display (Requirement 4.2)
    - _Requirements: 4.1, 4.2_

  - [ ] 6.7 Implement ContentArea component
    - Accept `fileContent`, `isLoading`, `error` props
    - Implement empty state: display "Press Ctrl+O to open a file" prompt
    - Implement loading state: display spinner with "Loading file..." message
    - Implement error state: display error message with icon
    - Implement content state: display line-numbered text content
    - Render line numbers in left column (sequential 1 to N)
    - Render content lines in right column with monospaced font
    - Apply CSS: `overflow-y: auto`, `overflow-x: auto`, `white-space: pre`
    - Preserve all whitespace, line endings, and special characters
    - _Requirements: 1.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 6.2_

  - [ ]* 6.8 Write property test for line number generation
    - **Property 3: Line number sequential generation**
    - **Validates: Requirements 3.2**
    - Use fast-check to verify line numbers are sequential 1 to N
    - _Requirements: 3.2_

  - [ ]* 6.9 Write unit tests for ContentArea component
    - Test empty state display (Requirement 1.2)
    - Test loading state display (Requirement 6.2)
    - Test error state display
    - Test monospaced font styling (Requirement 3.3)
    - Test vertical scrolling for large content (Requirement 3.4)
    - Test horizontal scrolling for wide lines (Requirement 3.5)
    - Test whitespace preservation (Requirement 3.6)
    - _Requirements: 1.2, 3.3, 3.4, 3.5, 3.6, 6.2_

  - [ ] 6.10 Implement StatusBar component
    - Accept `metadata` prop (FileMetadata or null)
    - Display empty state when no file is open
    - Display file size in human-readable format (bytes, KB, MB)
    - Display line count as "X lines"
    - Display encoding (UTF-8, ASCII, etc.)
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [ ]* 6.11 Write property tests for StatusBar formatting
    - **Property 5: File size human-readable formatting**
    - **Validates: Requirements 5.1**
    - Use fast-check to verify correct formatting for any file size
    - **Property 7: Encoding detection correctness**
    - **Validates: Requirements 5.3**
    - Use fast-check to verify encoding display matches detected encoding
    - _Requirements: 5.1, 5.3_

  - [ ]* 6.12 Write unit tests for StatusBar component
    - Test empty state display (Requirement 5.4)
    - Test file size formatting for bytes, KB, MB
    - Test line count display
    - Test encoding display
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [ ] 7. Checkpoint - Ensure frontend components render and tests pass
  - Ensure all tests pass, ask the user if questions arise.
